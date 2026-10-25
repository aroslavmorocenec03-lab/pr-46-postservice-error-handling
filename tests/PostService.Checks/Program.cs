using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PostService.Client;
using PostService.Contracts;

Console.OutputEncoding = Encoding.UTF8;
var checks = new List<(string Name, Func<Task> Run)>
{
    ("Усі п’ять методів: HTTP-методи, адреси, DTO та 204 без JSON", async () =>
    {
        var requests = new List<string>();
        using var client = new PostServiceClient(handler: new StubHandler(async (request, _) =>
        {
            requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Equal("A", body.RootElement.GetProperty("from").GetString());
                if (request.Method == HttpMethod.Put) Equal(7, body.RootElement.GetProperty("id").GetInt32());
            }
            if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.RequestUri!.AbsolutePath == "/postings" && request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { Item() }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Item()) };
        }));
        Equal(7, (await client.GetPostingsAsync())[0].Id);
        Equal(7, (await client.GetPostingByIdAsync(7)).Id);
        Equal(7, (await client.CreatePostingAsync(Input())).Id);
        Equal(7, (await client.UpdatePostingAsync(7, Put(7))).Id);
        await client.DeletePostingAsync(7);
        Equal("GET /postings|GET /postings/7|POST /postings|PUT /postings/7|DELETE /postings/7", string.Join("|", requests));
    }),
    ("400: власний виняток з усіма помилками полів", async () =>
    {
        using var client = Response(HttpStatusCode.BadRequest, "{\"errors\":{\"From\":[\"Обов’язкове поле\"],\"Weight\":[\"Більше нуля\",\"Перевірте вагу\"]}}");
        var error = await Throws<PostingValidationException>(() => client.CreatePostingAsync(Input()));
        Equal(2, error.Errors.Count); Equal(2, error.Errors["Weight"].Length);
    }),
    ("404: зберігається StatusCode", async () => await CheckStatus(HttpStatusCode.NotFound)),
    ("500: зберігається StatusCode, помилка не приховується", async () => await CheckStatus(HttpStatusCode.InternalServerError)),
    ("503: зберігається StatusCode", async () => await CheckStatus(HttpStatusCode.ServiceUnavailable)),
    ("Мережевий збій: StatusCode залишається null", async () =>
    {
        using var client = new PostServiceClient(handler: new StubHandler((_, _) => throw new HttpRequestException("Network unavailable")));
        var error = await Throws<HttpRequestException>(() => client.GetPostingsAsync());
        Equal<HttpStatusCode?>(null, error.StatusCode);
    }),
    ("Успішний HTTP із некоректним JSON: JsonException", async () =>
    {
        using var client = Response(HttpStatusCode.OK, "{broken");
        await Throws<JsonException>(() => client.GetPostingsAsync());
    }),
    ("JSON null замість DTO: JsonException", async () =>
    {
        using var client = Response(HttpStatusCode.OK, "null");
        await Throws<JsonException>(() => client.GetPostingByIdAsync(1));
    }),
    ("400 без errors не маскується під валідацію", async () =>
    {
        using var client = Response(HttpStatusCode.BadRequest, "{\"title\":\"Unknown\"}");
        await Throws<JsonException>(() => client.CreatePostingAsync(Input()));
    }),
    ("400 з некоректним JSON: JsonException", async () =>
    {
        using var client = Response(HttpStatusCode.BadRequest, "broken");
        await Throws<JsonException>(() => client.CreatePostingAsync(Input()));
    }),
    ("Тайм-аут HttpClient не губиться у перетворенні статусів", async () =>
    {
        using var client = new PostServiceClient(handler: new StubHandler(async (_, token) =>
        {
            await Task.Delay(2000, token); return new HttpResponseMessage(HttpStatusCode.OK);
        }), timeout: TimeSpan.FromMilliseconds(50));
        await Throws<TaskCanceledException>(() => client.GetPostingsAsync());
    }),
    ("Діагностика: час, тип, причина, стек і InnerException", () =>
    {
        Exception actual;
        try { throw new InvalidOperationException("ROOT-CAUSE", new FormatException("INNER-CAUSE")); }
        catch (Exception error) { actual = error; }
        var wrapped = new TargetInvocationException(new TargetInvocationException(actual));
        Equal(actual, ExceptionDiagnostics.Unwrap(wrapped));
        string report = ExceptionDiagnostics.Format(wrapped);
        Contains(report, "System.InvalidOperationException"); Contains(report, "ROOT-CAUSE");
        Contains(report, "INNER-CAUSE"); Contains(report, "TargetInvocationException");
        Contains(report, actual.StackTrace!); Contains(report, "[20");
        return Task.CompletedTask;
    })
};

if (args.Length == 2 && args[0] == "--integration")
{
    var server = new Uri(args[1]);
    checks.Add(("LIVE: список, створення, читання, оновлення, видалення", async () =>
    {
        using var client = new PostServiceClient(server);
        await client.GetPostingsAsync();
        PostingGetDto created = await client.CreatePostingAsync(Input());
        try
        {
            Equal("A", (await client.GetPostingByIdAsync(created.Id)).From);
            PostingPutDto changed = Put(created.Id); changed.Content = "UPDATED";
            Equal("UPDATED", (await client.UpdatePostingAsync(created.Id, changed)).Content);
            Equal("UPDATED", (await client.GetPostingByIdAsync(created.Id)).Content);
        }
        finally { await client.DeletePostingAsync(created.Id); }
        var missing = await Throws<HttpRequestException>(() => client.GetPostingByIdAsync(created.Id));
        Equal<HttpStatusCode?>(HttpStatusCode.NotFound, missing.StatusCode);
    }));
    checks.Add(("LIVE: серверні помилки валідації 400", async () =>
    {
        using var client = new PostServiceClient(server);
        var error = await Throws<PostingValidationException>(() => client.CreatePostingAsync(new PostingPostDto()));
        Equal(true, error.Errors.ContainsKey("From")); Equal(true, error.Errors.ContainsKey("Weight"));
    }));
    checks.Add(("LIVE: 404 при GET, PUT та DELETE відсутнього запису", async () =>
    {
        using var client = new PostServiceClient(server);
        foreach (Func<Task> operation in new Func<Task>[] { () => client.GetPostingByIdAsync(999999), () => client.UpdatePostingAsync(999999, Put(999999)), () => client.DeletePostingAsync(999999) })
        {
            var error = await Throws<HttpRequestException>(operation);
            Equal<HttpStatusCode?>(HttpStatusCode.NotFound, error.StatusCode);
        }
    }));
    foreach ((string mode, HttpStatusCode code) in new[] { ("500", HttpStatusCode.InternalServerError), ("503", HttpStatusCode.ServiceUnavailable) })
        checks.Add(($"LIVE: реальний сервер повертає {mode}", async () =>
        {
            using var control = new HttpClient { BaseAddress = server };
            using var client = new PostServiceClient(server);
            (await control.PostAsync("__demo/fault/" + mode, null)).EnsureSuccessStatusCode();
            try
            {
                var error = await Throws<HttpRequestException>(() => client.GetPostingsAsync());
                Equal<HttpStatusCode?>(code, error.StatusCode);
            }
            finally { (await control.PostAsync("__demo/fault/none", null)).EnsureSuccessStatusCode(); }
        }));
    checks.Add(("LIVE: помилковий JSON від сервера", async () =>
    {
        using var control = new HttpClient { BaseAddress = server };
        using var client = new PostServiceClient(server);
        (await control.PostAsync("__demo/fault/bad-json", null)).EnsureSuccessStatusCode();
        try { await Throws<JsonException>(() => client.GetPostingsAsync()); }
        finally { (await control.PostAsync("__demo/fault/none", null)).EnsureSuccessStatusCode(); }
    }));
}

int failures = 0;
foreach (var check in checks)
{
    try { await check.Run(); Console.WriteLine("PASS: " + check.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL: " + check.Name + " — " + error); }
}
Console.WriteLine($"\nУспішно: {checks.Count - failures}/{checks.Count}");
return failures == 0 ? 0 : 1;

static PostingGetDto Item() => new(7, "A", "B", "Books", 0, 2, 10, 20, 30, 100, 80);
static PostingPostDto Input() => new() { From = "A", To = "B", Content = "Books", DeliveryType = 0, Weight = 2, Width = 10, Height = 20, Depth = 30, Value = 100, Price = 80 };
static PostingPutDto Put(int id) => new() { Id = id, From = "A", To = "B", Content = "Books", DeliveryType = 0, Weight = 2, Width = 10, Height = 20, Depth = 30, Value = 100, Price = 80 };
static PostServiceClient Response(HttpStatusCode code, string body) => new(handler: new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") })));
static async Task CheckStatus(HttpStatusCode code)
{
    using var client = Response(code, "{}");
    var error = await Throws<HttpRequestException>(() => client.GetPostingsAsync());
    Equal<HttpStatusCode?>(code, error.StatusCode);
}
static async Task<T> Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T error) { return error; }
    throw new InvalidOperationException("Очікувався виняток " + typeof(T).Name);
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Очікувалося {expected}, отримано {actual}");
}
static void Contains(string text, string value)
{
    if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException("Відсутнє у звіті: " + value);
}
sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => handle(request, token);
}
