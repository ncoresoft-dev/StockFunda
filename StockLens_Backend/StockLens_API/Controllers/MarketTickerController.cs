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
        /// Retrieves dynamic 3-card Market Pulse telemetry (Nifty 50, Sensex, Bank Nifty) with live quotes,
        /// sparkline curves, day low-high range slider positions, and AI market breadth insights.
        /// </summary>
        /// <param name="timeframe">Timeframe resolution: 1D, 1W, or 1M.</param>
        /// <param name="forceRefresh">Force live refresh from Yahoo Finance.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("pulse")]
        [ProducesResponseType(typeof(MarketPulseResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMarketPulse(
            [FromQuery] string timeframe = "1D",
            [FromQuery] bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var pulse = await _tickerService.GetMarketPulseAsync(timeframe, forceRefresh, cancellationToken);
            return Ok(pulse);
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
