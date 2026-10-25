using System.Net;
using PostService.Client;
using PostService.Contracts;

namespace PostService.WindowsApp;

internal sealed class PostingEditor : Form
{
    private readonly IPostServiceClient _client;
    private readonly PostingGetDto? _original;
    private readonly Dictionary<string, Control> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly ErrorProvider _errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly Button _save = new() { Text = "Зберегти", Dock = DockStyle.Fill };
    private readonly Button _cancel = new() { Text = "Скасувати", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
    private readonly Label _status = new() { Text = "Заповніть поля й натисніть «Зберегти».", Dock = DockStyle.Fill, AutoSize = true };
    private bool _busy;

    public PostingEditor(IPostServiceClient client, PostingGetDto? original)
    {
        _client = client; _original = original;
        Text = original is null ? "Нове відправлення" : $"Редагування відправлення №{original.Id}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(740, 740);
        MinimumSize = new Size(660, 750);
        Font = new Font("Segoe UI", 10);
        _errors.ContainerControl = this;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 13 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        Add(root, 0, "From", "Пункт відправлення", new TextBox { Text = original?.From ?? "Київ" });
        Add(root, 1, "To", "Пункт отримання", new TextBox { Text = original?.To ?? "Львів" });
        Add(root, 2, "Content", "Вміст", new TextBox { Text = original?.Content ?? "Книги" });
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        type.Items.AddRange(["Відділення", "Кур’єр", "Експрес-кур’єр"]); type.SelectedIndex = original?.DeliveryType ?? 0;
        Add(root, 3, "DeliveryType", "Спосіб доставки", type);
        Add(root, 4, "Weight", "Вага, кг", Number(original?.Weight ?? 2.5m));
        Add(root, 5, "Width", "Ширина, см", Number(original?.Width ?? 20m));
        Add(root, 6, "Height", "Висота, см", Number(original?.Height ?? 10m));
        Add(root, 7, "Depth", "Довжина, см", Number(original?.Depth ?? 30m));
        Add(root, 8, "Value", "Оголошена вартість, грн", Number(original?.Value ?? 500m));
        Add(root, 9, "Price", "Ціна доставки, грн", Number(original?.Price ?? 85m));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(_status, 0, 10); root.SetColumnSpan(_status, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(_save, 0, 11); root.Controls.Add(_cancel, 1, 11);
        Controls.Add(root); AcceptButton = _save; CancelButton = _cancel;
        _save.Click += async (_, _) => await SaveAsync();
    }

    private void Add(TableLayoutPanel table, int row, string key, string caption, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 5, 24, 7); control.AccessibleName = caption;
        table.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        table.Controls.Add(control, 1, row); _fields.Add(key, control);
    }

    private static NumericUpDown Number(decimal value) => new() { Minimum = 0, Maximum = 1000000, DecimalPlaces = 2, Value = value };
    private decimal Value(string name) => ((NumericUpDown)_fields[name]).Value;

    private async Task SaveAsync()
    {
        _errors.Clear(); SetBusy(true);
        try
        {
            var data = new PostingPutDto
            {
                Id = _original?.Id ?? 0, From = _fields["From"].Text, To = _fields["To"].Text, Content = _fields["Content"].Text,
                DeliveryType = ((ComboBox)_fields["DeliveryType"]).SelectedIndex,
                Weight = Value("Weight"), Width = Value("Width"), Height = Value("Height"), Depth = Value("Depth"), Value = Value("Value"), Price = Value("Price")
            };
            bool saved = await RetryOperation.RunAsync(this, async () =>
            {
                if (_original is null) await _client.CreatePostingAsync(data);
                else await _client.UpdatePostingAsync(_original.Id, data);
            });
            if (saved) { DialogResult = DialogResult.OK; Close(); }
            else _status.Text = "Запит скасовано. Дані залишилися у формі.";
        }
        catch (PostingValidationException error)
        {
            var messages = new List<string>();
            foreach (var pair in error.Errors)
            {
                string text = string.Join(Environment.NewLine, pair.Value);
                if (_fields.TryGetValue(pair.Key, out var control)) _errors.SetError(control, text);
                messages.Add(pair.Key + ": " + text);
            }
            _status.Text = "Виправте позначені поля й збережіть ще раз.";
            MessageBox.Show(this, string.Join(Environment.NewLine, messages), "Перевірте введені дані", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound && _original is not null)
        {
            MessageBox.Show(this, "Відправлення вже видалено на сервері. Список буде оновлено.", "Застарілі дані");
            DialogResult = DialogResult.Retry; Close();
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        _busy = value; _save.Enabled = !value; _cancel.Enabled = !value;
        foreach (var field in _fields.Values) field.Enabled = !value;
        UseWaitCursor = value;
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy && e.CloseReason == CloseReason.UserClosing && DialogResult is not (DialogResult.OK or DialogResult.Retry)) e.Cancel = true;
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) { if (disposing) _errors.Dispose(); base.Dispose(disposing); }
}
