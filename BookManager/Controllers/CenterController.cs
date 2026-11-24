using BookManagerApi.DTOs;
using BookManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BookManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CenterController : ControllerBase
    {
        private readonly ICenterService _centerService;

        public CenterController(ICenterService centerService)
        {
            _centerService = centerService;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetCenters()
            => Ok(await _centerService.GetCentersAsync());

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateCenter([FromBody] CenterUpsertDto dto)
        {
            try
            {
                var created = await _centerService.CreateCenterAsync(dto?.Name ?? string.Empty);
                return Ok(created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateCenter(int id, [FromBody] CenterUpsertDto dto)
        {
            try
            {
                var updated = await _centerService.UpdateCenterAsync(id, dto?.Name ?? string.Empty);
                if (updated is null) return NotFound();
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteCenter(int id)
        {
            try
            {
                var deleted = await _centerService.DeleteCenterAsync(id);
                if (!deleted) return NotFound();
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpPost("bulk-price")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> BulkPrice([FromBody] BulkCenterPriceDto dto)
        {
            if (dto.Items == null || dto.Items.Count == 0)
                return BadRequest("No prices submitted.");

            var updated = await _centerService.BulkUpdatePricesAsync(dto.Items);
            return Ok(new { updated });
        }

        [HttpGet("prices")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Prices()
               => Ok(await _centerService.GetCenterPricesAsync());

        [HttpGet("prices/export")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Export()
        {
            var bytes = await _centerService.ExportCenterPricesXlsxAsync();
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"center-prices-{DateTime.UtcNow:yyyy-MM-dd}.xlsx");
        }

        [HttpPost("prices/reset")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> ResetPrices()
        {
            var count = await _centerService.ResetPricesAsync();
            return Ok(new { updated = count });
        }

    }
}
