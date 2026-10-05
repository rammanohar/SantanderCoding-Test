using HackerNews.Controllers;
using HackerNews.Models;
using IHackerNews;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HackerNews.Tests;

[TestClass]
public class StoriesControllerTests
{
    private Mock<INewsService> _newsService = null!;
    private Mock<ILogger<StoriesController>> _logger = null!;
    private StoriesController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _newsService = new Mock<INewsService>();
        _logger = new Mock<ILogger<StoriesController>>();
        _controller = new StoriesController(_newsService.Object, _logger.Object);
    }

    [TestMethod]
    public async Task GetBestStories_WhenCountIsZero_ReturnsBadRequest()
    {
        var result = await _controller.GetBestStories(0, CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest);
        Assert.AreEqual(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.AreEqual("The parameter 'count' must be a positive integer greater than zero.", badRequest.Value);
        _newsService.Verify(x => x.GetBestStoriesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetBestStories_WhenCountIsNegative_ReturnsBadRequest()
    {
        var result = await _controller.GetBestStories(-5, CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest);
        Assert.AreEqual(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        _newsService.Verify(x => x.GetBestStoriesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetBestStories_WhenServiceSucceeds_ReturnsOkWithStories()
    {
        var stories = new List<NewsStoryResponse>
        {
            new("Story 1", "https://one", "alice", DateTimeOffset.UnixEpoch, 100, 10),
            new("Story 2", "https://two", "bob", DateTimeOffset.UnixEpoch, 90, 5)
        };

        _newsService
            .Setup(x => x.GetBestStoriesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stories);

        var result = await _controller.GetBestStories(2, CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(StatusCodes.Status200OK, ok.StatusCode);
        Assert.AreSame(stories, ok.Value);
        _newsService.Verify(x => x.GetBestStoriesAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetBestStories_PassesCancellationTokenToService()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var stories = Array.Empty<NewsStoryResponse>();

        _newsService
            .Setup(x => x.GetBestStoriesAsync(3, token))
            .ReturnsAsync(stories);

        await _controller.GetBestStories(3, token);

        _newsService.Verify(x => x.GetBestStoriesAsync(3, token), Times.Once);
    }

    [TestMethod]
    public async Task GetBestStories_WhenServiceThrowsOperationCanceledException_RethrowsIt()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var token = cts.Token;

        _newsService
            .Setup(x => x.GetBestStoriesAsync(3, token))
            .ThrowsAsync(new OperationCanceledException(token));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _controller.GetBestStories(3, token));
    }

    [TestMethod]
    public async Task GetBestStories_WhenServiceThrowsException_ReturnsInternalServerError()
    {
        _newsService
            .Setup(x => x.GetBestStoriesAsync(3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected failure"));

        var result = await _controller.GetBestStories(3, CancellationToken.None);

        var objectResult = result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        Assert.AreEqual("An error occurred while retrieving stories.", objectResult.Value);
    }
}
