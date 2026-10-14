using GigaDict.Input;

namespace GigaDict.App;

internal static class InputSelfTest
{
    private const string Expected = "Привет 🎤";

    internal static int Run()
    {
        var result = 1;
        using var firstForm = CreateForm("GigaDict self-test: исходное поле", out var firstInput);
        using var secondForm = CreateForm("GigaDict self-test: новое приложение", out var secondInput);
        firstForm.Shown += async (_, _) =>
        {
            await Task.Delay(200);
            firstInput.Focus();
            using var guard = new FocusGuard();
            var firstIdentity = guard.Capture();
            if (firstIdentity is null) { result = 2; firstForm.Close(); return; }
            var firstTask = Guid.NewGuid();
            guard.Track(firstTask, firstIdentity);

            secondForm.Show(firstForm);
            secondInput.Focus();
            await Task.Delay(300);
            var secondIdentity = guard.Capture();
            if (secondIdentity is null) { result = 7; firstForm.Close(); return; }
            if (guard.IsStillSafe(firstTask)) { result = 6; firstForm.Close(); return; }
            var secondTask = Guid.NewGuid();
            guard.Track(secondTask, secondIdentity);
            if (!guard.IsStillSafe(secondTask)) { result = 5; firstForm.Close(); return; }

            try { UnicodeTextInserter.Insert(Expected, () => guard.IsStillSafe(secondTask)); }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "GigaDict-input-self-test.txt"), exception.ToString());
                result = 3; firstForm.Close(); return;
            }
            await Task.Delay(300);
            result = secondInput.Text == Expected ? 0 : 4;
            firstForm.Close();
        };
        Application.Run(firstForm);
        return result;
    }

    private static Form CreateForm(string title, out TextBox input)
    {
        var form = new Form
        {
            Text = title,
            Width = 420,
            Height = 120,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        input = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16) };
        form.Controls.Add(input);
        return form;
    }
}
