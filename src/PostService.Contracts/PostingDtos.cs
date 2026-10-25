namespace PostService.Contracts;

public sealed record PostingGetDto(int Id, string From, string To, string Content, int DeliveryType,
    decimal Weight, decimal Width, decimal Height, decimal Depth, decimal Value, decimal Price);

public class PostingPostDto
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Content { get; set; }
    public int? DeliveryType { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? Depth { get; set; }
    public decimal? Value { get; set; }
    public decimal? Price { get; set; }
}

public sealed class PostingPutDto : PostingPostDto
{
    public int Id { get; set; }
}
