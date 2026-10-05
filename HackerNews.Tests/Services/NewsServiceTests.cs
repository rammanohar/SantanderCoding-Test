using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.Models;
using HackerNews.Schema;
using HackerNews.Service;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HackerNews.Tests;

[TestClass]
public class NewsServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private MemoryCache _cache = null!;
    private Mock<ILogger<NewsService>> _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = new Mock<ILogger<NewsService>>();
    }

    [TestCleanup]
    public void Cleanup() => _cache.Dispose();

    [TestMethod]
    public async Task GetBestStoriesAsync_WhenCountIsZero_ReturnsEmptyAndDoesNotCallApi()
    {
        var handler = new StubHttpMessageHandler();
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetBestStoriesAsync(0, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count());
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetBestStoriesAsync_WhenCountIsNegative_ReturnsEmptyAndDoesNotCallApi()
    {
        var handler = new StubHttpMessageHandler();
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetBestStoriesAsync(-1, CancellationToken.None);

        Assert.AreEqual(0, result.Count());
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetBestStoriesAsync_ReturnsRequestedNumberOfStoriesOrderedByScoreDescending()
    {
        var firstId = 101;
        var secondId = 102;
        var thirdId = 103;

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString().EndsWith("beststories.json"))
                return JsonResponse(new[] { firstId, secondId, thirdId });
            var lastSegment = request.RequestUri!.Segments[^1];
            var cleanIdString = lastSegment.Replace(".json", "");
            var id = int.Parse(cleanIdString);
            var story = id switch
            {
                101 => new NewsStoryItem { Title = "First", Url = "https://one",  By   = "a", Time = 1000, Score = 10, Descendants = 1 },
                102 => new NewsStoryItem { Title = "Second",Url = "https://two", By = "b", Time = 2000, Score = 30, Descendants = 2 },
                103 => new NewsStoryItem { Title = "Third", Url = "https://three", By = "c", Time = 3000, Score = 20, Descendants = 3 },
                _ => null
            };

            return JsonResponse(story);
        });

        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = (await service.GetBestStoriesAsync(3, CancellationToken.None)).ToList();

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("Second", result[0].Title);
        Assert.AreEqual("Third", result[1].Title);
        Assert.AreEqual("First", result[2].Title);
    }

    [TestMethod]
    public async Task GetBestStoriesAsync_WhenCountIsLessThanAvailableOnlyRequestsThatManyStories()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString().EndsWith("beststories.json"))
                return JsonResponse(new[] { 1, 2, 3 });

            return JsonResponse(new NewsStoryItem { Title = "Story", Score = 1, Time = 1000 });
        });

        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetBestStoriesAsync(2, CancellationToken.None);

        Assert.AreEqual(2, result.Count());
        Assert.AreEqual(3, handler.RequestCount); // beststories + 2 item requests
    }

    [TestMethod]
    public async Task GetBestStoryIdsAsync_WhenCached_ReturnsCachedIdsWithoutCallingApi()
    {
        IReadOnlyList<int> cached = new[] { 10, 20, 30 };
        _cache.Set("BestStoriesIds", cached);

        var handler = new StubHttpMessageHandler();
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetBestStoryIdsAsync(CancellationToken.None);

        CollectionAssert.AreEqual(cached.ToArray(), result.ToArray());
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetBestStoryIdsAsync_WhenNotCached_CallsApiAndCachesIds()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new[] { 7, 8, 9 }));
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetBestStoryIdsAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 7, 8, 9 }, result.ToArray());
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsTrue(_cache.TryGetValue("BestStoriesIds", out IReadOnlyList<int>? cached));
        CollectionAssert.AreEqual(new[] { 7, 8, 9 }, cached!.ToArray());
    }

    [TestMethod]
    public async Task GetBestStoryIdsAsync_WhenApiReturnsNull_ReturnsEmptyListAndCachesIt()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse<int[]?>(null));
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetBestStoryIdsAsync(CancellationToken.None);

        Assert.AreEqual(0, result.Count);
        Assert.IsTrue(_cache.TryGetValue("BestStoriesIds", out IReadOnlyList<int>? cached));
        Assert.AreEqual(0, cached!.Count);
    }

    [TestMethod]
    public async Task GetStoryByIdAsync_WhenCached_ReturnsCachedStoryWithoutCallingApi()
    {
        var story = new NewsStoryResponse("Cached", "https://cached", "author", DateTimeOffset.UnixEpoch, 99, 5);
        _cache.Set("Story_123", story);

        var handler = new StubHttpMessageHandler();
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetStoryByIdAsync(123, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("Cached", result.Title);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetStoryByIdAsync_WhenApiReturnsStory_MapsAndCachesResponse()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new NewsStoryItem
        {
            Title = "Test story",
            Url = "https://example.com/story",
            By = "alice",
            Time = 1700000000,
            Score = 42,
            Descendants = 12
        }));

        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetStoryByIdAsync(123, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("Test story", result.Title);
        Assert.AreEqual("https://example.com/story", result.Uri);
        Assert.AreEqual("alice", result.PostedBy);
        Assert.AreEqual(42, result.Score);
        Assert.AreEqual(12, result.CommentCount);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000000), result.Time);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsTrue(_cache.TryGetValue("Story_123", out NewsStoryResponse? cached));
        Assert.AreEqual("Test story", cached!.Title);
    }

    [TestMethod]
    public async Task GetStoryByIdAsync_WhenApiReturnsNull_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse<NewsStoryItem?>(null));
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetStoryByIdAsync(123, CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetStoryByIdAsync_WhenHttpRequestFails_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("API unavailable"));
        using var client = CreateClient(handler);
        var service = CreateService(client);

        var result = await service.GetStoryByIdAsync(123, CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetBestStoryIdsAsync_WhenHttpRequestFails_RethrowsException()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("API unavailable"));
        using var client = CreateClient(handler);
        var service = CreateService(client);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => service.GetBestStoryIdsAsync(CancellationToken.None));
    }

    

    private NewsService CreateService(HttpClient client) =>
        new(client, _cache, _logger.Object);

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.local/")
        };
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value, options: JsonOptions)
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _handler;

        public int RequestCount { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage>? handler = null)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_handler is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(_handler(request));
        }
    }
}
