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
    public class StockNewsController : ControllerBase
    {
        private readonly IStockNewsService _stockNewsService;

        public StockNewsController(IStockNewsService stockNewsService)
        {
            _stockNewsService = stockNewsService;
        }

        /// <summary>
        /// Retrieves the list of all available stocks.
        /// </summary>
        [HttpGet]
        [HttpGet("all")]
        [ProducesResponseType(typeof(IEnumerable<StockDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStocks()
        {
            var stocks = await _stockNewsService.GetAllStocksAsync();
            return Ok(stocks);
        }

        /// <summary>
        /// Retrieves latest news for a stock by symbol and exchange (NSE/BSE).
        /// </summary>
        /// <param name="symbol">Stock ticker symbol (e.g., RELIANCE, TCS, INFY).</param>
        /// <param name="exchange">Stock exchange (NSE or BSE, default: NSE).</param>
        /// <param name="limit">Max number of news items to return (default: 5, max: 100).</param>
        /// <param name="page">Page index.</param>
        /// <param name="refresh">Force cache refresh from IndianAPI / Google News.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("news")]
        [ProducesResponseType(typeof(StockNewsResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetNewsBySymbol(
            [FromQuery] string symbol,
            [FromQuery] string? exchange = "NSE",
            [FromQuery] int limit = 5,
            [FromQuery] int page = 1,
            [FromQuery] bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "The 'symbol' parameter is required." });
            }

            var response = await _stockNewsService.GetLatestNewsBySymbolAsync(symbol, exchange, limit, page, refresh, cancellationToken);
            return Ok(response);
        }
    }
}
