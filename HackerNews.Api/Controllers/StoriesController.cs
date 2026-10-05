namespace HackerNews.Controllers
{
    using HackerNews.Models;
    using HackerNews.Service;
    using IHackerNews;
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route("api/[controller]")]
    public class StoriesController : ControllerBase
    {
        private readonly INewsService _newsService;
        private readonly ILogger<StoriesController> _logger;
        public StoriesController(INewsService newsService,  ILogger<StoriesController> logger)
        {
            _newsService = newsService;
            _logger = logger;
        }

        [HttpGet("best")]
        [ProducesResponseType(typeof(IEnumerable<NewsStoryResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetBestStories([FromQuery] int count, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Request received to retrieve {Count} best Hacker News stories", count);

            if (count <= 0)
            {
                _logger.LogWarning("Invalid story count requested: {Count}",  count);
                return BadRequest("The parameter 'count' must be a positive integer greater than zero.");
            }

            try
            {
                var stories =  await _newsService.GetBestStoriesAsync(count, cancellationToken);

                _logger.LogInformation("Successfully retrieved {Count} Hacker News stories", stories.Count());

                return Ok(stories);
            }
            catch (OperationCanceledException)  when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning( "Request for {Count} stories was cancelled", count);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving {Count} best Hacker News stories", count);

                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving stories.");
            }
        }
    }

}
