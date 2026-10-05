using HackerNews.Models;

namespace IHackerNews
{
    public interface INewsService
    {
        Task<IEnumerable<NewsStoryResponse>> GetBestStoriesAsync(int count, CancellationToken cancellationToken);
        Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken);
          
        Task<NewsStoryResponse> GetStoryByIdAsync(int Id, CancellationToken cancellationToken);

        
    }
}
