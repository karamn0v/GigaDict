using System.Threading.Channels;
using System.Reflection;
using GigaDict.Audio;
using GigaDict.Core.Configuration;
using GigaDict.Core.Input;
using GigaDict.Core.Queue;
using GigaDict.Core.Tasks;
using GigaDict.Input;
using GigaDict.Recognition;
using Microsoft.Win32;

namespace GigaDict.App;

internal sealed class GigaDictApplicationContext : ApplicationContext
{
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GigaDict", "settings.json");
    private readonly SettingsStore _store;
    private readonly Icon _appIcon;
    private readonly TrayIconSet _trayIcons;
    private readonly NotifyIcon _tray;
    private readonly WasapiRecorder _recorder = new();
    private readonly FocusGuard _focus = new();
    private readonly Channel<DictationTask> _insertions = Channel.CreateUnbounded<DictationTask>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Control _dispatcher = new();
    private AppSettings _settings;
    private RecognitionQueue _queue;
    private GlobalHotkeyHook _hook;
    private System.Windows.Forms.Timer? _stateResetTimer;
    private Task _insertionWorker;
    private Guid? _recordingId;
    private FocusIdentity? _recordingFocus;
    private long _sequence;
    private bool _sessionAvailable = true;
    private bool _exiting;

    internal GigaDictApplicationContext()
    {
        _store = new SettingsStore(_settingsPath);
        _settings = LoadSettings();
        _appIcon = LoadAppIcon();
        _trayIcons = new TrayIconSet(_appIcon);
        _dispatcher.CreateControl();
        _queue = CreateQueue(_settings);
        _hook = CreateHook(_settings.Hotkey);
        _tray = new NotifyIcon
        {
            Icon = _trayIcons.Idle,
            Text = "GigaDict — ожидание",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _recorder.LimitReached += async (_, _) => await CompleteRecordingAsync();
        _recorder.CaptureFailed += (_, exception) => SetState("Ошибка микрофона: " + exception.Message, TrayState.Error);
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _hook.Install();
        _insertionWorker = Task.Run(ProcessInsertionsAsync);
    }

    private RecognitionQueue CreateQueue(AppSettings settings)
    {
        var http = new RetryingTransport(new HttpTranscriptionTransport(), settings.Retry);
        var ws = new RetryingTransport(new WebSocketTranscriptionTransport(), settings.Retry);
        var queue = new RecognitionQueue([http, ws], settings.MaximumQueueItems, settings.MaximumAudioMemoryMiB * 1024L * 1024L, settings.HistorySize);
        queue.ResultReady += item => { item.TransitionTo(DictationStatus.WaitingForInsertion); return _insertions.Writer.WriteAsync(item, _shutdown.Token).AsTask(); };
        return queue;
    }

    private GlobalHotkeyHook CreateHook(string value)
    {
        var hook = new GlobalHotkeyHook(HotkeyParser.Parse(value));
        hook.Action += OnHotkeyAction;
        return hook;
    }

    private async void OnHotkeyAction(object? sender, HotkeyAction action)
    {
        try
        {
            if (!_sessionAvailable) return;
            switch (action)
            {
                case HotkeyAction.StartRecording: StartRecording(); break;
                case HotkeyAction.StopRecording: await CompleteRecordingAsync(); break;
                case HotkeyAction.CancelRecording: CancelRecording(); break;
            }
        }
        catch (Exception exception) { SetState("Ошибка: " + exception.Message, TrayState.Error); }
    }

    private void StartRecording()
    {
        if (_recordingId is not null) return;
        var id = Guid.NewGuid();
        var focus = _focus.Capture();
        if (focus is not null) _focus.Track(id, focus, _settings.FocusProtection);
        _recordingId = id;
        _recordingFocus = focus;
        var taskBytes = Math.Min(_settings.MaximumAudioMemoryMiB * 1024L * 1024L, 32000L * _settings.MaximumRecordingSeconds);
        _recorder.Start(_settings.CaptureDeviceId, TimeSpan.FromSeconds(_settings.MaximumRecordingSeconds), taskBytes);
        SetState("Идёт запись — Esc для отмены", TrayState.Recording);
    }

    private async Task CompleteRecordingAsync()
    {
        if (_recordingId is not { } id) return;
        _recordingId = null;
        byte[] audio;
        try { audio = await _recorder.StopAsync(_shutdown.Token); }
        catch (Exception exception) { _focus.Forget(id); SetState("Ошибка микрофона: " + exception.Message, TrayState.Error); return; }
        if (audio.Length == 0) { _focus.Forget(id); SetState("Пустая запись", TrayState.Warning); return; }
        var task = new DictationTask(Interlocked.Increment(ref _sequence), audio, _recordingFocus, _settings, id);
        if (!_focus.IsStillSafe(id)) task.InvalidateInsertion();
        if (!_queue.TryEnqueue(task))
        {
            task.Dispose(); _focus.Forget(id); SetState("Очередь или лимит RAM заполнены", TrayState.Error); return;
        }
        SetState($"В очереди: {_queue.Count}", TrayState.Working);
    }

    private void CancelRecording()
    {
        if (_recordingId is not { } id) return;
        _recordingId = null;
        _recorder.Cancel();
        _focus.Forget(id);
        SetState("Диктовка отменена", TrayState.Idle);
    }

    private async Task ProcessInsertionsAsync()
    {
        try
        {
            await foreach (var item in _insertions.Reader.ReadAllAsync(_shutdown.Token))
            {
                while (_hook.IsHeld) await Task.Delay(25, _shutdown.Token);
                var blockReason = GetInsertionBlockReason(item);
                if (blockReason is not null)
                {
                    item.Error = blockReason;
                    item.TransitionTo(DictationStatus.SavedToHistory);
                    _queue.UpdateHistory(item);
                    _focus.Forget(item.Id);
                    SetState(blockReason, TrayState.Warning);
                    continue;
                }
                try
                {
                    await InvokeOnUiAsync(async () =>
                    {
                        var safe = () => _focus.IsStillSafe(item.Id) && !_hook.IsHeld;
                        var text = TextBoundary.Prepare(item.Result!, null, null, false, item.Settings.AutomaticSpaces);
                        if (item.Settings.InsertionMethod == InsertionMethod.UnicodeSendInput) UnicodeTextInserter.Insert(text, safe);
                        else await ClipboardTextInserter.InsertAsync(text, safe, _shutdown.Token);
                    });
                    item.TransitionTo(DictationStatus.Inserted);
                    _queue.UpdateHistory(item);
                    SetState("Вставлено", TrayState.Success, resetAfterMilliseconds: 2000);
                }
                catch (Exception exception)
                {
                    item.Error = "Ошибка вставки: " + exception.Message;
                    item.TransitionTo(DictationStatus.SavedToHistory);
                    _queue.UpdateHistory(item);
                    SetState(item.Error, TrayState.Error);
                }
                finally { _focus.Forget(item.Id); }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private string? GetInsertionBlockReason(DictationTask item)
    {
        if (string.IsNullOrWhiteSpace(item.Result)) return "Не вставлено: сервер вернул пустой текст";
        if (item.WasRetried) return "Не вставлено: запрос потребовал сетевого повтора";
        if (!item.AutomaticInsertionAllowed) return "Не вставлено: поле ввода не распознано как безопасное";
        if (!_focus.IsStillSafe(item.Id)) return "Не вставлено: фокус или поле ввода изменились";
        return null;
    }

    private Task InvokeOnUiAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.BeginInvoke(async () => { try { await action(); completion.SetResult(); } catch (Exception e) { completion.SetException(e); } });
        return completion.Task;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Состояние: ожидание").Enabled = false;
        menu.Items.Add("Копировать последний текст", null, (_, _) => CopyLast());
        menu.Items.Add("История", null, (_, _) => ShowHistory());
        menu.Items.Add("Настройки", null, async (_, _) => await ShowSettingsAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, async (_, _) => await ExitAsync());
        menu.Opening += (_, _) => menu.Items[0].Text = $"Состояние: {_tray.Text.Replace("GigaDict — ", "")} · очередь {_queue.Count}";
        return menu;
    }

    private void CopyLast()
    {
        var text = _queue.History.FirstOrDefault(x => !string.IsNullOrEmpty(x.Text))?.Text;
        if (text is not null) Clipboard.SetText(text);
    }

    private void ShowHistory()
    {
        using var form = new Form { Text = "История GigaDict (только RAM)", Icon = _appIcon, Width = 650, Height = 350, StartPosition = FormStartPosition.CenterScreen };
        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (var entry in _queue.History)
        {
            var status = entry.Inserted ? "Вставлено" : entry.Error ?? "Сохранено без вставки";
            var text = string.IsNullOrWhiteSpace(entry.Text) ? "" : $" — {entry.Text}";
            list.Items.Add($"{entry.CreatedAt.ToLocalTime():HH:mm:ss}  {status}{text}");
        }
        form.Controls.Add(list); form.ShowDialog();
    }

    private async Task ShowSettingsAsync()
    {
        using var form = new SettingsForm(_settings);
        form.Icon = _appIcon;
        if (form.ShowDialog() != DialogResult.OK || !form.TryBuild(out var updated)) return;
        await _store.SaveAsync(updated);
        AutoStartManager.SetEnabled(updated.AutoStart);
        if (!string.Equals(updated.Hotkey, _settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            _hook.Dispose(); _hook = CreateHook(updated.Hotkey); _hook.Install();
        }
        _settings = updated;
    }

    private AppSettings LoadSettings()
    {
        try { return _store.LoadAsync().GetAwaiter().GetResult(); }
        catch { return new AppSettings(); }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        _sessionAvailable = args.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.SessionLogon;
        if (!_sessionAvailable) CancelRecording();
    }

    private void SetState(string text, TrayState state, int resetAfterMilliseconds = 0)
    {
        if (_dispatcher.InvokeRequired) { _dispatcher.BeginInvoke(() => SetState(text, state, resetAfterMilliseconds)); return; }
        _stateResetTimer?.Stop();
        _stateResetTimer?.Dispose();
        _stateResetTimer = null;
        _tray.Text = ("GigaDict — " + text)[..Math.Min(63, ("GigaDict — " + text).Length)];
        _tray.Icon = _trayIcons.Get(state);
        if (resetAfterMilliseconds <= 0) return;

        _stateResetTimer = new System.Windows.Forms.Timer { Interval = resetAfterMilliseconds };
        _stateResetTimer.Tick += (_, _) =>
        {
            _stateResetTimer?.Stop();
            _stateResetTimer?.Dispose();
            _stateResetTimer = null;
            SetState("Ожидание", TrayState.Idle);
        };
        _stateResetTimer.Start();
    }

    internal static Icon LoadAppIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GigaDict.App.gigadict.ico")
            ?? throw new InvalidOperationException("Встроенная иконка GigaDict не найдена.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        if (_queue.Count > 0 && MessageBox.Show("В очереди есть записи. Выйти и удалить их из RAM?", "GigaDict", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _exiting = true;
        _hook.Dispose(); CancelRecording(); _insertions.Writer.TryComplete(); _shutdown.Cancel();
        await _queue.DisposeAsync();
        try { await _insertionWorker; } catch (OperationCanceledException) { }
        _stateResetTimer?.Stop(); _stateResetTimer?.Dispose();
        _tray.Visible = false; _tray.Dispose(); _trayIcons.Dispose(); _appIcon.Dispose(); _focus.Dispose(); _recorder.Dispose(); _dispatcher.Dispose();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _shutdown.Dispose(); ExitThread();
    }
}
