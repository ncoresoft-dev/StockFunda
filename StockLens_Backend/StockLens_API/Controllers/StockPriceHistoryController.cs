using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/stocks")]
    public class StockPriceHistoryController : ControllerBase
    {
        private readonly IStockPriceHistoryService _priceHistoryService;

        public StockPriceHistoryController(IStockPriceHistoryService priceHistoryService)
        {
            _priceHistoryService = priceHistoryService;
        }

        [HttpGet("prices")]
        [ProducesResponseType(typeof(PriceHistoryResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(429)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetPriceHistoryBySymbol(
            [FromQuery] string symbol, 
            [FromQuery] string? exchange = null, 
            [FromQuery] string period = "5yr",
            [FromQuery] bool refresh = false,
            [FromQuery] string filter = "price",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "Symbol query parameter is required." });
            }

            try
            {
                var result = await _priceHistoryService.GetPriceHistoryBySymbolAsync(symbol, exchange, period, refresh, filter, cancellationToken);
                return Ok(result);

            }
            catch (System.Collections.Generic.KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions.ProviderNotFoundException ex)
            {
                return NotFound(new { message = ex.Message, ticker = ex.Ticker });
            }
            catch (StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions.ProviderRateLimitException ex)
            {
                if (ex.RetryAfter.HasValue)
                {
                    Response.Headers["Retry-After"] = ((int)ex.RetryAfter.Value.TotalSeconds).ToString();
                }
                return StatusCode(429, new { message = ex.Message });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving price history.", detail = ex.Message });
            }
        }

        [HttpGet("{stockId}/prices")]
        [ProducesResponseType(typeof(PriceHistoryResponseDto), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(429)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetPriceHistoryByStockId(
            int stockId, 
            [FromQuery] string period = "5yr",
            [FromQuery] bool refresh = false,
            [FromQuery] string filter = "price",
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _priceHistoryService.GetPriceHistoryByStockIdAsync(stockId, period, refresh, filter, cancellationToken);
                return Ok(result);
            }
            catch (System.Collections.Generic.KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions.ProviderNotFoundException ex)
            {
                return NotFound(new { message = ex.Message, ticker = ex.Ticker });
            }
            catch (StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions.ProviderRateLimitException ex)
            {
                if (ex.RetryAfter.HasValue)
                {
                    Response.Headers["Retry-After"] = ((int)ex.RetryAfter.Value.TotalSeconds).ToString();
                }
                return StatusCode(429, new { message = ex.Message });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving price history.", detail = ex.Message });
            }
        }
    }
}
