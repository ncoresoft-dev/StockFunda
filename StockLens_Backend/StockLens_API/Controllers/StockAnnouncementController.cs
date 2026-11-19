using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/corporate-announcements")]
    [Route("api/announcements")]
    [Produces("application/json")]
    public class StockAnnouncementController : ControllerBase
    {
        private readonly IStockAnnouncementService _announcementService;

        public StockAnnouncementController(IStockAnnouncementService announcementService)
        {
            _announcementService = announcementService;
        }

        /// <summary>
        /// Retrieves corporate announcements from the local database.
        /// Never calls the NSE API directly on user requests.
        /// </summary>
        /// <param name="symbol">Optional stock symbol filter (e.g., RELIANCE, TCS).</param>
        /// <param name="symbols">Optional comma-separated list of symbols (e.g., RELIANCE,TCS,INFY) for watchlist feeds.</param>
        /// <param name="search">Optional text query searching symbols, company names, subjects, and text.</param>
        /// <param name="category">Optional category/subject filter.</param>
        /// <param name="limit">Max items per page (default: 30, max: 100).</param>
        /// <param name="page">Page index (1-based).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet]
        [ProducesResponseType(typeof(StockAnnouncementResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnnouncements(
            [FromQuery] string? symbol = null,
            [FromQuery] string? symbols = null,
            [FromQuery] string? search = null,
            [FromQuery] string? category = null,
            [FromQuery] int limit = 30,
            [FromQuery] int page = 1,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<string>? symbolList = null;
            if (!string.IsNullOrWhiteSpace(symbols))
            {
                symbolList = symbols.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            }

            var result = await _announcementService.GetAnnouncementsAsync(symbol, symbolList, search, category, limit, page, cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves distinct announcement categories / subjects recorded in the database.
        /// </summary>
        [HttpGet("categories")]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCategories(CancellationToken cancellationToken = default)
        {
            var categories = await _announcementService.GetCategoriesAsync(cancellationToken);
            return Ok(categories);
        }

        /// <summary>
        /// Manually triggers a sync from NSE for the past 3 months (or specified months) and persists new announcements.
        /// </summary>
        [HttpPost("sync")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> TriggerSync([FromQuery] int months = 3, CancellationToken cancellationToken = default)
        {
            var added = await _announcementService.SyncLatestAnnouncementsAsync(months, cancellationToken);
            return Ok(new { success = true, newRecordsAdded = added });
        }
    }
}
