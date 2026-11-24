using BookManagerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatisticsController : ControllerBase
    {
        private readonly IStatisticsService _statisticsService;

        public StatisticsController(IStatisticsService statisticsService)
        {
            _statisticsService = statisticsService;
        }

        [HttpGet("book-growth")]
        public async Task<IActionResult> GetBookGrowth(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string bucket = "month")
        {
            var normalizedBucket = (bucket ?? "month").Trim().ToLowerInvariant();
            if (normalizedBucket is not ("day" or "month"))
                return BadRequest("bucket must be 'day' or 'month'.");

            try
            {
                var rows = await _statisticsService.GetBookGrowthAsync(fromUtc, toUtc, normalizedBucket);
                return Ok(rows);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("top-subcategories")]
        public async Task<IActionResult> GetTopSubcategories(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int top = 10)
        {
            try
            {
                var rows = await _statisticsService.GetTopSubcategoriesAsync(fromUtc, toUtc, top);
                return Ok(rows);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
