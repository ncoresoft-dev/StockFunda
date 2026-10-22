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

        /// <summary>
        /// Allows a user to request an Annual Report for a company.
        /// </summary>
        [HttpPost("{symbol}/annual-reports/request")]
        [ProducesResponseType(typeof(StockAnnualReportsResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RequestAnnualReport(
            [FromRoute] string symbol,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "Stock symbol is required." });
            }

            try
            {
                var response = await _annualReportService.RequestAnnualReportAsync(symbol, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to request annual report.", error = ex.Message });
            }
        }

        /// <summary>
        /// Admin endpoint to save annual report metadata and trigger Gemini AI extraction.
        /// </summary>
        [HttpPost("annual-reports/admin/extract")]
        [ProducesResponseType(typeof(StockAnnualReportsResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AdminSaveAndExtractAnnualReport(
            [FromBody] AdminSaveAndExtractReportRequestDto request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Symbol) || string.IsNullOrWhiteSpace(request.PdfUrl))
            {
                return BadRequest(new { message = "Symbol and PdfUrl are required." });
            }

            try
            {
                var response = await _annualReportService.AdminSaveAndExtractAnnualReportAsync(request, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to extract annual report insights.", error = ex.Message });
            }
        }

        /// <summary>
        /// Returns directory list of companies and their annual report statuses.
        /// </summary>
        [HttpGet("annual-reports/directory")]
        [ProducesResponseType(typeof(IReadOnlyList<AnnualReportDirectoryItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAnnualReportsDirectory(
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var list = await _annualReportService.GetAnnualReportsDirectoryAsync(search, status, cancellationToken);
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to load directory.", error = ex.Message });
            }
        }

        /// <summary>
        /// Returns all annual reports currently in 'Requested' status (for admin notification badge & list).
        /// </summary>
        [HttpGet("annual-reports/requested")]
        [ProducesResponseType(typeof(IReadOnlyList<AnnualReportDirectoryItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetRequestedReports(CancellationToken cancellationToken = default)
        {
            try
            {
                var list = await _annualReportService.GetRequestedReportsAsync(cancellationToken);
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to load requested reports.", error = ex.Message });
            }
        }

        /// <summary>
        /// Suggests the official PDF URL from Screener for a given symbol without saving to database.
        /// </summary>
        [HttpGet("{symbol}/annual-reports/suggest-pdf")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<IActionResult> SuggestScreenerPdf(
            [FromRoute] string symbol,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "Stock symbol is required." });
            }

            var pdfUrl = await _annualReportService.SuggestScreenerPdfUrlAsync(symbol, cancellationToken);
            return Ok(new { symbol = symbol.Trim().ToUpperInvariant(), pdfUrl });
        }
    }
}
