using PostService.Contracts;

namespace PostService.Client;

public interface IPostServiceClient
{
    Task<PostingGetDto[]> GetPostingsAsync();
    Task<PostingGetDto> GetPostingByIdAsync(int id);
    Task<PostingGetDto> CreatePostingAsync(PostingPostDto posting);
    Task<PostingGetDto> UpdatePostingAsync(int id, PostingPutDto posting);
    Task DeletePostingAsync(int id);
}
