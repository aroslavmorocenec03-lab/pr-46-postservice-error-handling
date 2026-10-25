using PostService.Contracts;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://localhost:5211");
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
var store = new PostingStore();
var faults = new DemoFaults();
bool demoFaults = args.Contains("--demo-faults");

app.MapGet("/postings", async () =>
{
    // Тестові збої доступні лише при запуску з --demo-faults.
    if (faults.Mode == "500") throw new InvalidOperationException("Навчальна помилка 500 у GET /postings.");
    if (faults.Mode == "503") return (IResult)Results.StatusCode(503);
    if (faults.Mode == "bad-json") return Results.Text("{broken-json", "application/json");
    if (faults.Mode == "timeout") await Task.Delay(TimeSpan.FromSeconds(10));
    lock (store.Sync) return Results.Ok(store.Items.Values.OrderBy(x => x.Id).ToArray());
});

app.MapGet("/postings/{id:int}", (int id) =>
{
    lock (store.Sync) return store.Items.TryGetValue(id, out var item)
        ? Results.Ok(item) : (IResult)Results.NotFound(new { message = "Відправлення не знайдено." });
});

app.MapPost("/postings", (PostingPostDto input) =>
{
    var errors = InputValidation.Validate(input);
    if (errors.Count > 0) return (IResult)Results.ValidationProblem(errors);
    lock (store.Sync)
    {
        var item = InputValidation.ToPosting(input, store.NextId++);
        store.Items.Add(item.Id, item);
        return Results.Created($"/postings/{item.Id}", item);
    }
});

app.MapPut("/postings/{id:int}", (int id, PostingPutDto input) =>
{
    lock (store.Sync)
    {
        if (!store.Items.ContainsKey(id)) return (IResult)Results.NotFound();
        var errors = InputValidation.Validate(input);
        if (input.Id != id) errors["Id"] = ["Id у тілі та адресі запиту мають збігатися."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var item = InputValidation.ToPosting(input, id);
        store.Items[id] = item;
        return Results.Ok(item);
    }
});

app.MapDelete("/postings/{id:int}", (int id) =>
{
    lock (store.Sync) return store.Items.Remove(id) ? (IResult)Results.NoContent() : Results.NotFound();
});

if (demoFaults)
{
    app.MapPost("/__demo/fault/{mode}", (string mode) =>
    {
        if (mode is not ("none" or "500" or "503" or "bad-json" or "timeout")) return (IResult)Results.BadRequest();
        faults.Mode = mode;
        return Results.Ok(new { mode });
    });
}

app.Run();

sealed class DemoFaults { public volatile string Mode = "none"; }

sealed class PostingStore
{
    public object Sync { get; } = new();
    public int NextId { get; set; } = 4;
    public Dictionary<int, PostingGetDto> Items { get; } = new()
    {
        [1] = new(1, "Рівне", "Луцьк", "Книги", 0, 2.5m, 20, 10, 30, 500, 85),
        [2] = new(2, "Київ", "Львів", "Одяг", 1, 1.2m, 25, 12, 35, 1200, 110),
        [3] = new(3, "Одеса", "Дніпро", "Подарунок", 0, 0.8m, 15, 15, 20, 750, 90)
    };
}

static class InputValidation
{
    public static Dictionary<string, string[]> Validate(PostingPostDto input)
    {
        var errors = new Dictionary<string, string[]>();
        void Text(string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200)
                errors[field] = ["Введіть текст від 1 до 200 символів."];
        }
        void Positive(string field, decimal? value)
        {
            if (value is null or <= 0) errors[field] = ["Значення має бути більшим за нуль."];
        }
        Text("From", input.From); Text("To", input.To); Text("Content", input.Content);
        if (input.DeliveryType is null or < 0 or > 2) errors["DeliveryType"] = ["Оберіть тип доставки від 0 до 2."];
        Positive("Weight", input.Weight); Positive("Width", input.Width);
        Positive("Height", input.Height); Positive("Depth", input.Depth);
        if (input.Value is null or < 0) errors["Value"] = ["Оголошена вартість не може бути від’ємною."];
        if (input.Price is null or < 0) errors["Price"] = ["Ціна не може бути від’ємною."];
        return errors;
    }

    public static PostingGetDto ToPosting(PostingPostDto value, int id) => new(id,
        value.From!.Trim(), value.To!.Trim(), value.Content!.Trim(), value.DeliveryType!.Value,
        value.Weight!.Value, value.Width!.Value, value.Height!.Value, value.Depth!.Value, value.Value!.Value, value.Price!.Value);
}
