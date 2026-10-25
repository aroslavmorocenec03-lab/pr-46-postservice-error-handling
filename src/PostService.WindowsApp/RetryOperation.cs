using System.Net;

namespace PostService.WindowsApp;

internal static class RetryOperation
{
    public static async Task<bool> RunAsync(IWin32Window owner, Func<Task> operation)
    {
        while (true)
        {
            try { await operation(); return true; }
            catch (HttpRequestException error) when (error.StatusCode is null or HttpStatusCode.ServiceUnavailable)
            {
                using var dialog = new RetryDialog("Сервер недоступний. Перевірте, чи він працює, і повторіть операцію.");
                if (dialog.ShowDialog(owner) != DialogResult.Retry) return false;
            }
            catch (TaskCanceledException)
            {
                using var dialog = new RetryDialog("Час очікування відповіді вичерпано. Спробуйте повторити операцію.");
                if (dialog.ShowDialog(owner) != DialogResult.Retry) return false;
            }
        }
    }
}

internal sealed class RetryDialog : Form
{
    public RetryDialog(string message)
    {
        Text = "Немає зв’язку із сервером";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(530, 190);
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label { Text = message, Dock = DockStyle.Fill }, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Скасувати", DialogResult = DialogResult.Cancel, Size = new Size(125, 36) };
        var retry = new Button { Text = "Повторити", DialogResult = DialogResult.Retry, Size = new Size(125, 36) };
        buttons.Controls.Add(cancel); buttons.Controls.Add(retry);
        layout.Controls.Add(buttons, 0, 1);
        Controls.Add(layout);
        AcceptButton = retry; CancelButton = cancel;
    }
}
