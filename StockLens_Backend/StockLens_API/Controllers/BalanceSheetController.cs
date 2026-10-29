using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.Models;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/stocks")]
    [Produces("application/json")]
    public class BalanceSheetController : ControllerBase
    {
        private readonly IStockBalanceSheetService _balanceSheetService;

        public BalanceSheetController(IStockBalanceSheetService balanceSheetService)
        {
            _balanceSheetService = balanceSheetService;
        }

        /// <summary>
        /// Retrieves annual balance sheet statements and metrics for a stock by symbol and exchange.
        /// </summary>
        /// <param name="symbol">Stock ticker symbol (e.g. RELIANCE, TCS, INFY).</param>
        /// <param name="exchange">Exchange code (NSE or BSE, default: NSE).</param>
        /// <param name="refresh">Force fresh synchronization from external providers.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("balancesheet")]
        [ProducesResponseType(typeof(BalanceSheetResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetBalanceSheet(
            [FromQuery] string symbol, 
            [FromQuery] string? exchange = "NSE",
            [FromQuery] bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "The 'symbol' query parameter is required." });
            }

            var result = await _balanceSheetService.GetBalanceSheetAsync(symbol, exchange, refresh, cancellationToken);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            return Ok(result);
        }
    }
}
