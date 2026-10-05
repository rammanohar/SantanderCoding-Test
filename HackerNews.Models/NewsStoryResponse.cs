namespace HackerNews.Models
{

    public record NewsStoryResponse
    (
            string Title,
            string? Uri,
            string PostedBy,
            DateTimeOffset Time,
            int Score,
            int CommentCount
    );
}
