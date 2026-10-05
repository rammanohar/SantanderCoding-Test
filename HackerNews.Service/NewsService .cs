using HackerNews.Models;
using HackerNews.Schema;
using IHackerNews;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace HackerNews.Service
{

    public class NewsService : INewsService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<NewsService> _logger;

        private readonly SemaphoreSlim _bestStoriesLock = new(1, 1);

        private readonly ConcurrentDictionary<int, SemaphoreSlim> _storyLocks = new();

        private const string BestStoriesCacheKey = "BestStoriesIds";
        private const string StoryCacheKeyPrefix = "Story_";

        public NewsService(HttpClient httpClient, IMemoryCache cache, ILogger<NewsService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IEnumerable<NewsStoryResponse>> GetBestStoriesAsync(int count, CancellationToken cancellationToken)
        {

            _logger.LogInformation("GetBestStoriesAsync");
            if (count <= 0) return Enumerable.Empty<NewsStoryResponse>();

            // Get the cached Hacker News best-story IDs
            var storyIds = await GetBestStoryIdsAsync(cancellationToken);

            var targetIds = storyIds.Take(count).ToList();

            var storiesBag = new ConcurrentBag<NewsStoryResponse>();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 10, // Throttles network traffic to 10 simultaneous requests
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(targetIds, parallelOptions, async (id, token) =>
            {
                var story = await GetStoryByIdAsync(id, token);
                if (story != null)
                {
                    storiesBag.Add(story);
                }
            });

            return storiesBag.OrderByDescending(s => s.Score);

        }

        public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
        {

            if (_cache.TryGetValue(BestStoriesCacheKey, out IReadOnlyList<int>? cachedIds))
            {
                _logger.LogDebug("Best stories retrieved from cache");
                return cachedIds!;
            }

            // 2. Only one request should refresh beststories.json
            await _bestStoriesLock.WaitAsync(cancellationToken);

            try
            {
                // 3. Double-check after acquiring the lock
                if (_cache.TryGetValue(BestStoriesCacheKey, out cachedIds))
                {
                    return cachedIds!;
                }

                // 4. Get IDs from Hacker News
                _logger.LogInformation("Refreshing best stories from Hacker News API");
                var result = await _httpClient.GetFromJsonAsync<int[]>("beststories.json", cancellationToken);

                var ids = result ?? Array.Empty<int>();

                _logger.LogInformation("Retrieved {Count} best story IDs from Hacker News", ids.Count());


                // 5. Cache for 5 minutes
                _cache.Set(BestStoriesCacheKey, ids, TimeSpan.FromMinutes(5));

                return ids;
            }
            catch (Exception ex)
            {
                _logger.LogError(" Exception at beststories.json  : {Message}", ex.Message);
                throw;
            }
            finally
            {
                _bestStoriesLock.Release();
            }
        }

        public async Task<NewsStoryResponse?> GetStoryByIdAsync(int id, CancellationToken cancellationToken)
        {
            string cacheKey = $"{StoryCacheKeyPrefix}{id}";

            // 1. Fast path - story already cached
            if (_cache.TryGetValue(cacheKey, out NewsStoryResponse? cachedStory))
            {
                _logger.LogDebug("Story {StoryId} retrieved from cache", id);
                return cachedStory;
            }

            // 2. Get a lock specifically for this story ID
            var storyLock = _storyLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

            await storyLock.WaitAsync(cancellationToken);

            try
            {
                // 3. Double-check after acquiring the lock
                if (_cache.TryGetValue(cacheKey, out cachedStory))
                {
                    return cachedStory;
                }

                _logger.LogDebug("Retrieving story {StoryId} from Hacker News API", id);

                // 4. Retrieve story from Hacker News
                var item = await _httpClient.GetFromJsonAsync<NewsStoryItem>($"item/{id}.json", cancellationToken);

                if (item == null)
                {
                    return null;
                }

                _logger.LogDebug("Retrieved story {StoryId} with score {Score}", id, item.Score);

                // 5. Convert Hacker News model to our API response
                var dto = new NewsStoryResponse
                (
                    item.Title,
                    item.Url,
                    item.By,
                    DateTimeOffset.FromUnixTimeSeconds(item.Time),
                    item.Score,
                    item.Descendants
                );

                // 6. Cache story
                _cache.Set(cacheKey, dto, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow =
                            TimeSpan.FromHours(1),

                    SlidingExpiration =
                            TimeSpan.FromMinutes(15)
                });

                return dto;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to retrieve Hacker News story {StoryId}", id);

                return null;
            }
            finally
            {
                storyLock.Release();
            }
        }


    }

}
