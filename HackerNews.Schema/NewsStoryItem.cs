namespace HackerNews.Schema
{
    public class NewsStoryItem
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string By { get; set; } = string.Empty;
        public int Time { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int Score { get; set; }
        public int Descendants { get; set; } // Tracks comment count
    }
}
