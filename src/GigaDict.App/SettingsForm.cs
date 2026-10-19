using GigaDict.Audio;
using GigaDict.Core.Configuration;
using GigaDict.Core.Input;

namespace GigaDict.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _server = new() { Width = 320 };
    private readonly ComboBox _device = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly TextBox _hotkey = new() { Width = 150 };
    private readonly ComboBox _insertion = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _focusProtection = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckBox _spaces = new() { Text = "Автоматические пробелы" };
    private readonly NumericUpDown _duration = new() { Minimum = 1, Maximum = 3600 };
    private readonly NumericUpDown _queue = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _memory = new() { Minimum = 16, Maximum = 2048 };
    private readonly CheckBox _autoStart = new() { Text = "Запускать при входе в Windows" };
    private readonly Label _status = new() { AutoSize = true };
    private readonly AppSettings _original;

    internal SettingsForm(AppSettings settings)
    {
        _original = settings;
        Text = "Настройки GigaDict";
        Width = 520; Height = 520; StartPosition = FormStartPosition.CenterScreen;
        _insertion.Items.AddRange(Enum.GetNames<InsertionMethod>());
        _focusProtection.Items.AddRange([
            new FocusModeItem(FocusProtectionMode.Strict, "Строгий"),
            new FocusModeItem(FocusProtectionMode.Compatible, "Совместимый")
        ]);
        LoadDevices();
        _server.Text = settings.ServerBaseUri.ToString();
        _hotkey.Text = settings.Hotkey;
        _insertion.SelectedItem = settings.InsertionMethod.ToString();
        _focusProtection.SelectedItem = _focusProtection.Items.Cast<FocusModeItem>().First(x => x.Mode == settings.FocusProtection);
        _spaces.Checked = settings.AutomaticSpaces;
        _duration.Value = settings.MaximumRecordingSeconds;
        _queue.Value = settings.MaximumQueueItems;
        _memory.Value = settings.MaximumAudioMemoryMiB;
        _autoStart.Checked = settings.AutoStart;
        if (settings.CaptureDeviceId is { } id)
            _device.SelectedItem = _device.Items.Cast<DeviceItem>().FirstOrDefault(x => x.Id == id);

        var test = new Button { Text = "Проверить соединение", AutoSize = true };
        test.Click += async (_, _) => await TestConnectionAsync();
        var save = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, AutoSize = true };
        save.Click += (sender, args) => { if (!TryBuild(out _)) DialogResult = DialogResult.None; };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, AutoScroll = true };
        Add(layout, "Адрес GigaSTT", _server); Add(layout, "Микрофон", _device);
        Add(layout, "Горячая клавиша", _hotkey); Add(layout, "Метод вставки", _insertion);
        Add(layout, "Защита фокуса", _focusProtection); Add(layout, "", _spaces);
        Add(layout, "Макс. длительность, с", _duration); Add(layout, "Размер очереди", _queue); Add(layout, "Лимит RAM, МиБ", _memory);
        Add(layout, "", _autoStart); Add(layout, "", test); Add(layout, "", _status); Add(layout, "", save);
        Controls.Add(layout); AcceptButton = save;
    }

    internal bool TryBuild(out AppSettings settings)
    {
        settings = _original;
        try
        {
            settings = _original with
            {
                ServerBaseUri = new Uri(_server.Text, UriKind.Absolute),
                CaptureDeviceId = (_device.SelectedItem as DeviceItem)?.Id, Hotkey = _hotkey.Text,
                InsertionMethod = Enum.Parse<InsertionMethod>((string)_insertion.SelectedItem!),
                FocusProtection = ((FocusModeItem)_focusProtection.SelectedItem!).Mode, AutomaticSpaces = _spaces.Checked,
                MaximumRecordingSeconds = (int)_duration.Value, MaximumQueueItems = (int)_queue.Value,
                MaximumAudioMemoryMiB = (int)_memory.Value, AutoStart = _autoStart.Checked
            };
            var errors = settings.Validate();
            if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            return true;
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Некорректные настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }

    private async Task TestConnectionAsync()
    {
        if (!Uri.TryCreate(_server.Text, UriKind.Absolute, out var uri)) { _status.Text = "Некорректный адрес"; return; }
        try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) }; using var response = await client.GetAsync(new Uri(uri, "/ready")); _status.Text = $"GigaSTT: HTTP {(int)response.StatusCode}"; }
        catch (Exception exception) { _status.Text = "Ошибка: " + exception.Message; }
    }

    private void LoadDevices()
    {
        _device.Items.Add(new DeviceItem(null, "Микрофон Windows по умолчанию"));
        try { foreach (var device in WasapiRecorder.EnumerateDevices()) _device.Items.Add(new DeviceItem(device.Id, device.Name)); } catch { }
        _device.SelectedIndex = 0;
    }

    private static void Add(TableLayoutPanel panel, string label, Control control) { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 8, 3) }); panel.Controls.Add(control); }
    private sealed record DeviceItem(string? Id, string Name) { public override string ToString() => Name; }
    private sealed record FocusModeItem(FocusProtectionMode Mode, string Name) { public override string ToString() => Name; }
}
