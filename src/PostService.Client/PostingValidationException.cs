namespace PostService.Client;

public sealed class ValidationErrorResponse
{
    public Dictionary<string, string[]>? Errors { get; set; }
}

public sealed class PostingValidationException : Exception
{
    public Dictionary<string, string[]> Errors { get; }

    public PostingValidationException(Dictionary<string, string[]> errors)
        : base("Сервер відхилив дані відправлення.")
    {
        Errors = errors;
    }
}
