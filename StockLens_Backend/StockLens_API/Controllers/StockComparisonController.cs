using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/stocks/compare")]
    [Produces("application/json")]
    public class StockComparisonController : ControllerBase
    {
        private readonly IStockComparisonService _comparisonService;

        public StockComparisonController(IStockComparisonService comparisonService)
        {
            _comparisonService = comparisonService;
        }

        /// <summary>
        /// Retrieves a side-by-side comparison of 2 stocks including valuation, health score, quarterly results, shareholding, and normalized performance history.
        /// </summary>
        /// <param name="stock1">First stock symbol (e.g. TCS, HDFCBANK, RELIANCE).</param>
        /// <param name="stock2">Second stock symbol (e.g. INFY, ICICIBANK, ADANIENT).</param>
        /// <param name="exchange">Exchange code (NSE or BSE, default: NSE).</param>
        /// <param name="period">Historical period for comparative returns chart (e.g. 1mo, 6mo, 1yr, 5yr).</param>
        /// <param name="refresh">Force fresh data synchronization from providers.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet]
        [ProducesResponseType(typeof(StockComparisonDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CompareStocks(
            [FromQuery] string stock1,
            [FromQuery] string stock2,
            [FromQuery] string? exchange = "NSE",
            [FromQuery] string period = "1yr",
            [FromQuery] bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(stock1) || string.IsNullOrWhiteSpace(stock2))
            {
                return BadRequest(new { message = "Both 'stock1' and 'stock2' query parameters are required." });
            }

            if (stock1.Trim().Equals(stock2.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Please select two different stocks to compare." });
            }

            try
            {
                var result = await _comparisonService.CompareStocksAsync(
                    stock1,
                    stock2,
                    exchange,
                    period,
                    refresh,
                    cancellationToken);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while compiling stock comparison.",
                    detail = ex.Message
                });
            }
        }

        /// <summary>
        /// Retrieves popular stock comparison presets (e.g., TCS vs INFY, HDFC vs ICICI Bank).
        /// </summary>
        [HttpGet("presets")]
        [ProducesResponseType(typeof(IEnumerable<ComparisonPresetDto>), StatusCodes.Status200OK)]
        public IActionResult GetComparisonPresets()
        {
            var presets = _comparisonService.GetComparisonPresets();
            return Ok(presets);
        }
    }
}

