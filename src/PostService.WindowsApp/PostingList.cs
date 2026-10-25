using System.Net;
using PostService.Client;
using PostService.Contracts;

namespace PostService.WindowsApp;

internal sealed class PostingList : Form
{
    private readonly IPostServiceClient _client;
    private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 12) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false, BackgroundColor = Color.White, RowHeadersVisible = false, AccessibleName = "Поштові відправлення" };
    private readonly Label _status = new() { Text = "Готово", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
    private bool _busy;

    public PostingList(IPostServiceClient client)
    {
        _client = client;
        Text = "PostService — ПР-46";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 700);
        MinimumSize = new Size(940, 620);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 248, 252);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "Поштові відправлення", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "Додавайте та редагуйте відправлення. Дані зберігаються на сервері.", AutoSize = true, ForeColor = Color.DimGray }, 0, 1);
        AddButton("Оновити", async () => await ReloadAsync());
        AddButton("Додати", async () => await EditAsync(null));
        AddButton("Редагувати", async () => await EditSelectedAsync());
        AddButton("Видалити", async () => await DeleteSelectedAsync());
        root.Controls.Add(_toolbar, 0, 2);
        string[] properties = ["Id", "From", "To", "Content", "Weight", "Price"];
        string[] titles = ["Id", "Звідки", "Куди", "Вміст", "Вага, кг", "Ціна, грн"];
        for (int i = 0; i < properties.Length; i++)
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = properties[i], HeaderText = titles[i], AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = i == 0 ? 35 : 100 });
        root.Controls.Add(_grid, 0, 3); root.Controls.Add(_status, 0, 4); Controls.Add(root);
        Shown += async (_, _) => await ReloadAsync();
    }

    private void AddButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, Size = new Size(140, 40), AccessibleName = text };
        button.Click += async (_, _) => await action();
        _toolbar.Controls.Add(button);
    }

    private async Task ReloadAsync()
    {
        SetBusy(true);
        try
        {
            bool completed = await RetryOperation.RunAsync(this, async () =>
            {
                PostingGetDto[] items = await _client.GetPostingsAsync();
                _grid.DataSource = items;
                _status.Text = $"Завантажено відправлень: {items.Length}.";
            });
            if (!completed) _status.Text = "Оновлення скасовано. Натисніть «Оновити», щоб повторити.";
        }
        finally { SetBusy(false); }
    }

    private PostingGetDto? Selected() => _grid.CurrentRow?.DataBoundItem as PostingGetDto;

    private async Task EditSelectedAsync()
    {
        PostingGetDto? selected = Selected();
        if (selected is null) { MessageBox.Show(this, "Оберіть відправлення в таблиці."); return; }
        PostingGetDto? current = null;
        bool stale = false;
        SetBusy(true);
        try { await RetryOperation.RunAsync(this, async () => current = await _client.GetPostingByIdAsync(selected.Id)); }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            MessageBox.Show(this, "Вибране відправлення вже відсутнє. Список буде оновлено.", "Застарілі дані");
            stale = true;
        }
        finally { SetBusy(false); }
        if (stale) { await ReloadAsync(); return; }
        if (current is null) return;
        await EditAsync(current);
    }

    private async Task EditAsync(PostingGetDto? item)
    {
        using var editor = new PostingEditor(_client, item);
        DialogResult result = editor.ShowDialog(this);
        if (result is DialogResult.OK or DialogResult.Retry) await ReloadAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        PostingGetDto? selected = Selected();
        if (selected is null) { MessageBox.Show(this, "Оберіть відправлення в таблиці."); return; }
        if (MessageBox.Show(this, "Видалити вибране відправлення?", "Підтвердження", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        bool refresh = false;
        SetBusy(true);
        try { refresh = await RetryOperation.RunAsync(this, () => _client.DeletePostingAsync(selected.Id)); }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            MessageBox.Show(this, "Вибране відправлення вже відсутнє. Список буде оновлено.", "Застарілі дані");
            refresh = true;
        }
        finally { SetBusy(false); }
        if (refresh) await ReloadAsync();
    }

    private void SetBusy(bool busy) { _busy = busy; _toolbar.Enabled = !busy; _grid.Enabled = !busy; UseWaitCursor = busy; }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
