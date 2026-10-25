using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PostService.Contracts;

namespace PostService.Client;

public sealed class PostServiceClient : IPostServiceClient, IDisposable
{
    private readonly HttpClient _httpClient;
    public Uri BaseAddress => _httpClient.BaseAddress!;

    public PostServiceClient(Uri? baseAddress = null, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = baseAddress ?? new Uri("http://localhost:5211/");
        _httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<PostingGetDto[]> GetPostingsAsync()
    {
        using var response = await _httpClient.GetAsync("postings");
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<PostingGetDto[]>(response);
    }

    public async Task<PostingGetDto> GetPostingByIdAsync(int id)
    {
        using var response = await _httpClient.GetAsync($"postings/{id}");
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<PostingGetDto>(response);
    }

    public async Task<PostingGetDto> CreatePostingAsync(PostingPostDto posting)
    {
        using var response = await _httpClient.PostAsJsonAsync("postings", posting);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<PostingGetDto>(response);
    }

    public async Task<PostingGetDto> UpdatePostingAsync(int id, PostingPutDto posting)
    {
        using var response = await _httpClient.PutAsJsonAsync($"postings/{id}", posting);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<PostingGetDto>(response);
    }

    public async Task DeletePostingAsync(int id)
    {
        using var response = await _httpClient.DeleteAsync($"postings/{id}");
        await EnsureSuccessAsync(response);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();
            if (problem?.Errors is null || problem.Errors.Count == 0)
                throw new JsonException("Відповідь 400 не містить очікуваного словника errors.");
            throw new PostingValidationException(problem.Errors);
        }
        throw new HttpRequestException($"Сервер повернув помилку {(int)response.StatusCode}.", null, response.StatusCode);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new JsonException("Сервер повернув null замість очікуваних даних.");
    }

    public void Dispose() => _httpClient.Dispose();
}
