using GigaDict.Audio;
using GigaDict.Core.Configuration;
using GigaDict.Core.Input;

namespace GigaDict.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _server = new() { Width = 320 };
    private readonly ComboBox _device = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly TextBox _hotkey = new() { Width = 150 };
    private readonly ComboBox _insertion = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _focusProtection = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckBox _spaces = new() { Text = "Автоматические пробелы", AutoSize = true };
    private readonly NumericUpDown _duration = new() { Minimum = 1, Maximum = 3600 };
    private readonly NumericUpDown _queue = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _memory = new() { Minimum = 16, Maximum = 2048 };
    private readonly CheckBox _autoStart = new() { Text = "Запускать при входе в Windows", AutoSize = true };
    private readonly Label _autoStartHint = new() { AutoSize = true, MaximumSize = new Size(320, 0) };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) };
    private readonly AppSettings _original;

    internal SettingsForm(AppSettings settings)
    {
        _original = settings;
        SuspendLayout();
        // Sizes below are written for 96 DPI and scaled to the monitor, as the designer does.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Настройки GigaDict";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
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
        _autoStart.CheckedChanged += (_, _) => UpdateAutoStartHint();
        UpdateAutoStartHint();
        if (settings.CaptureDeviceId is { } id)
            _device.SelectedItem = _device.Items.Cast<DeviceItem>().FirstOrDefault(x => x.Id == id);

        var test = new Button { Text = "Проверить соединение", AutoSize = true };
        test.Click += async (_, _) => await TestConnectionAsync();
        var connection = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        connection.Controls.Add(test); connection.Controls.Add(_status);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 12, 12, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var row = 0;
        // Explicit cells: a hidden control must not shift the following ones.
        void AddRow(string label, Control control)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (label.Length != 0) fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            fields.Controls.Add(control, 1, row++);
        }
        AddRow("Адрес GigaSTT", _server); AddRow("", connection); AddRow("Микрофон", _device);
        AddRow("Горячая клавиша", _hotkey); AddRow("Метод вставки", _insertion);
        AddRow("Защита фокуса", _focusProtection); AddRow("", _spaces);
        AddRow("Макс. длительность, с", _duration); AddRow("Размер очереди", _queue); AddRow("Лимит RAM, МиБ", _memory);
        AddRow("", _autoStart); AddRow("", _autoStartHint);

        var save = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(88, 0) };
        save.Click += (sender, args) => { if (!TryBuild(out _)) DialogResult = DialogResult.None; };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(88, 0) };
        // Right-to-left flow: Cancel is rightmost, as in standard Windows dialogs.
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 12) };
        buttons.Controls.Add(cancel); buttons.Controls.Add(save);

        // Added last, so docked first: the button bar takes the bottom, fields the rest.
        Controls.Add(fields); Controls.Add(buttons);
        AcceptButton = save; CancelButton = cancel;
        ResumeLayout(false);
        PerformLayout();
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

    private void UpdateAutoStartHint()
    {
        var networkPath = Environment.ProcessPath is { } path && AutoStartRules.IsNetworkPath(path);
        _autoStartHint.Text = _autoStart.Checked && networkPath
            ? "GigaDict запущен из сетевой папки, при входе в Windows она может быть недоступна. Скопируйте GigaDict.exe на локальный диск и включите автозапуск оттуда."
            : "";
        _autoStartHint.Visible = _autoStartHint.Text.Length != 0;
    }

    private void LoadDevices()
    {
        _device.Items.Add(new DeviceItem(null, "Микрофон Windows по умолчанию"));
        try { foreach (var device in WasapiRecorder.EnumerateDevices()) _device.Items.Add(new DeviceItem(device.Id, device.Name)); } catch { }
        _device.SelectedIndex = 0;
    }

    private sealed record DeviceItem(string? Id, string Name) { public override string ToString() => Name; }
    private sealed record FocusModeItem(FocusProtectionMode Mode, string Name) { public override string ToString() => Name; }
}
