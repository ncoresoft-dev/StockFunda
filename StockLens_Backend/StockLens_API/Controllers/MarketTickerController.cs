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
    [Route("api/market")]
    [Produces("application/json")]
    public class MarketTickerController : ControllerBase
    {
        private readonly IMarketTickerService _tickerService;
        private readonly IExploreLensService _exploreLensService;

        public MarketTickerController(
            IMarketTickerService tickerService,
            IExploreLensService exploreLensService)
        {
            _tickerService = tickerService;
            _exploreLensService = exploreLensService;
        }

        /// <summary>
        /// Retrieves live market index & currency quotes (NIFTY 50, SENSEX, BANK NIFTY, NIFTY IT, USD/INR, etc.).
        /// Reads from database cache or updates from Yahoo Finance API.
        /// </summary>
        /// <param name="forceRefresh">Optionally force re-fetching from Yahoo Finance API.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("ticker")]
        [ProducesResponseType(typeof(IEnumerable<MarketTickerQuoteDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMarketTicker(
            [FromQuery] bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var quotes = await _tickerService.GetMarketTickerQuotesAsync(forceRefresh, cancellationToken);
            return Ok(quotes);
        }

        /// <summary>
        /// Retrieves real-time company orbital radar & 7-day sparkline trend data for the Explore by Lens view.
        /// </summary>
        /// <param name="lens">The lens category (Growth, Cash Flow, Ownership, Risk).</param>
        /// <param name="forceRefresh">Force cache refresh from Yahoo Finance.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("explore-lens")]
        [ProducesResponseType(typeof(ExploreLensResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetExploreLens(
            [FromQuery] string lens = "Growth",
            [FromQuery] bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var data = await _exploreLensService.GetExploreLensDataAsync(lens, forceRefresh, cancellationToken);
            return Ok(data);
        }
    }
}
