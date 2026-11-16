using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/sectors")]
    [Produces("application/json")]
    public class SectorAnalysisController : ControllerBase
    {
        private readonly ISectorAnalysisService _sectorService;

        public SectorAnalysisController(ISectorAnalysisService sectorService)
        {
            _sectorService = sectorService;
        }

        /// <summary>
        /// Retrieves the list of all available industry sectors tracked in the platform.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSectors(CancellationToken cancellationToken)
        {
            var sectors = await _sectorService.GetAvailableSectorsAsync(cancellationToken);
            return Ok(sectors);
        }

        /// <summary>
        /// Retrieves comprehensive sector analysis, stock heatmap constituents, market breadth, and peer comparison rankings.
        /// </summary>
        /// <param name="sector">The sector name (e.g., Information Technology, Financial Services, Automobile and Auto Components).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("analysis")]
        [ProducesResponseType(typeof(SectorAnalysisResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSectorAnalysis([FromQuery] string? sector, CancellationToken cancellationToken)
        {
            var result = await _sectorService.GetSectorAnalysisAsync(sector, cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves live institutional money flow, sector inflow/outflow, and top moving stocks (Choice A: Top 4-5 per sector).
        /// </summary>
        /// <param name="topPerSector">Number of top stocks per sector to evaluate (defaults to 5).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("money-flow")]
        [ProducesResponseType(typeof(MoneyFlowResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMoneyFlow([FromQuery] int topPerSector = 5, CancellationToken cancellationToken = default)
        {
            var result = await _sectorService.GetMoneyFlowAsync(topPerSector, cancellationToken);
            return Ok(result);
        }
    }
}

