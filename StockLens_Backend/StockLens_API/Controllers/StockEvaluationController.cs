using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/stocks")]
    [Produces("application/json")]
    public class StockEvaluationController : ControllerBase
    {
        private readonly IStockEvaluationService _evaluationService;

        public StockEvaluationController(IStockEvaluationService evaluationService)
        {
            _evaluationService = evaluationService;
        }

        /// <summary>
        /// Evaluates a stock against 23 fundamental, technical, and institutional metrics to generate a 0-100 score, buy/hold signal, pros/cons, and category breakdowns.
        /// </summary>
        /// <param name="symbol">Stock ticker symbol (e.g., RELIANCE, TCS, INFY, TATAMOTORS).</param>
        /// <param name="exchange">Exchange code (NSE or BSE, default: NSE).</param>
        /// <param name="refresh">Force fresh calculation & synchronization.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("evaluation")]
        [ProducesResponseType(typeof(StockHealthScoreDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetStockEvaluation(
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
                var response = await _evaluationService.EvaluateStockAsync(symbol, exchange, refresh, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while evaluating stock investment health.",
                    detail = ex.Message
                });
            }
        }

        /// <summary>
        /// Evaluates Multi-Year Breakouts and Volume Spikes for a stock.
        /// </summary>
        [HttpGet("evaluation/breakouts")]
        [ProducesResponseType(typeof(BreakoutEvaluationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetBreakoutAnalysis(
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
                var response = await _evaluationService.GetBreakoutAnalysisAsync(symbol, exchange, refresh, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while evaluating stock breakouts.",
                    detail = ex.Message
                });
            }
        }
    }
}
