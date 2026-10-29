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
    [Route("api/stocks")]
    [Produces("application/json")]
    public class StockQuarterlyResultsController : ControllerBase
    {
        private readonly IStockQuarterlyResultsService _quarterlyResultsService;

        public StockQuarterlyResultsController(IStockQuarterlyResultsService quarterlyResultsService)
        {
            _quarterlyResultsService = quarterlyResultsService;
        }

        /// <summary>
        /// Retrieves the latest quarterly results (Revenue, Operating Profit, Depreciation, Tax, Net Profit, EPS) and historical quarter trends for a stock by symbol and exchange.
        /// </summary>
        /// <param name="symbol">Stock ticker symbol (e.g. RELIANCE, TCS, INFY, HDFCBANK).</param>
        /// <param name="exchange">Exchange code (NSE or BSE, default: NSE).</param>
        /// <param name="refresh">Force fresh synchronization from external Indian APIs.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("quarterly-results")]
        [ProducesResponseType(typeof(StockQuarterlyResultsResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetQuarterlyResultsBySymbol(
            [FromQuery] string symbol,
            [FromQuery] string? exchange = "NSE",
            [FromQuery] bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "The 'symbol' query parameter is required." });
            }

            try
            {
                var response = await _quarterlyResultsService.GetQuarterlyResultsBySymbolAsync(symbol, exchange, refresh, cancellationToken);
                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while retrieving quarterly results.",
                    detail = ex.Message
                });
            }
        }
    }
}
