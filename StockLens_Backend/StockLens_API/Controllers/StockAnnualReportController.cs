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
    public class StockAnnualReportController : ControllerBase
    {
        private readonly IStockAnnualReportService _annualReportService;

        public StockAnnualReportController(IStockAnnualReportService annualReportService)
        {
            _annualReportService = annualReportService;
        }

        /// <summary>
        /// Retrieves the latest/current year Annual Report with official PDF download link.
        /// </summary>
        /// <param name="symbol">Stock symbol (e.g. TCS, RELIANCE, INFY, TATAMOTORS).</param>
        /// <param name="refresh">Force cache refresh if needed (defaults to false).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("{symbol}/annual-reports")]
        [ProducesResponseType(typeof(StockAnnualReportsResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAnnualReportsBySymbol(
            [FromRoute] string symbol,
            [FromQuery] bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "Stock symbol is required." });
            }

            try
            {
                var response = await _annualReportService.GetAnnualReportsBySymbolAsync(symbol, refresh, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to fetch annual reports.", error = ex.Message });
            }
        }
    }
}
