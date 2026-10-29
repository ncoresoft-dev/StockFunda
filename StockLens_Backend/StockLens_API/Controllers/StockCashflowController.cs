using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/stocks")]
    [Produces("application/json")]
    public class StockCashflowController : ControllerBase
    {
        private readonly IStockCashflowService _cashflowService;

        public StockCashflowController(IStockCashflowService cashflowService)
        {
            _cashflowService = cashflowService;
        }

        /// <summary>
        /// Retrieves annual cash flow metrics, Free Cash Flow, CFO/OP, and financial statements by ticker symbol.
        /// </summary>
        /// <param name="symbol">Stock ticker symbol (e.g. RELIANCE, TCS, INFY).</param>
        /// <param name="exchange">Exchange code (NSE or BSE, default: NSE).</param>
        /// <param name="refresh">Force fresh synchronization from BharatStock API.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("cashflow")]
        [ProducesResponseType(typeof(StockCashflowResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCashflowBySymbol(
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
                var response = await _cashflowService.GetCashflowBySymbolAsync(symbol, exchange, refresh, cancellationToken);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ProviderNotFoundException ex)
            {
                return NotFound(new { message = ex.Message, ticker = ex.Ticker });
            }
            catch (ProviderRateLimitException ex)
            {
                if (ex.RetryAfter.HasValue)
                {
                    Response.Headers["Retry-After"] = ((int)ex.RetryAfter.Value.TotalSeconds).ToString();
                }
                return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while retrieving cash flow and financial data.",
                    detail = ex.Message
                });
            }
        }
    }
}

