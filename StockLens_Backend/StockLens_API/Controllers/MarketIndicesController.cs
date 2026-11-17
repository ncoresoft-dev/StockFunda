using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/v1/indices")]
    [Route("api/indices")]
    [Produces("application/json")]
    public class MarketIndicesController : ControllerBase
    {
        private readonly IMarketIndicesService _marketIndicesService;

        public MarketIndicesController(IMarketIndicesService marketIndicesService)
        {
            _marketIndicesService = marketIndicesService;
        }

        /// <summary>
        /// Retrieves the list of Nifty 50 constituent companies.
        /// </summary>
        [HttpGet("nifty-50")]
        [ProducesResponseType(typeof(IEnumerable<MarketIndexCompanyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTop50Companies(CancellationToken cancellationToken)
        {
            var result = await _marketIndicesService.GetTop50CompaniesAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves the list of BSE Sensex constituent companies.
        /// </summary>
        [HttpGet("sensex")]
        [ProducesResponseType(typeof(IEnumerable<MarketIndexCompanyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSensexCompanies(CancellationToken cancellationToken)
        {
            var result = await _marketIndicesService.GetSensexCompaniesAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves the list of Nifty Bank constituent companies.
        /// </summary>
        [HttpGet("bank-nifty")]
        [ProducesResponseType(typeof(IEnumerable<MarketIndexCompanyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetBankNiftyCompanies(CancellationToken cancellationToken)
        {
            var result = await _marketIndicesService.GetBankNiftyCompaniesAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves company index data for comma-separated stock symbols.
        /// </summary>
        [HttpGet("stocks-by-symbols")]
        [ProducesResponseType(typeof(IEnumerable<MarketIndexCompanyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetStocksBySymbols([FromQuery] string symbols, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(symbols))
            {
                return Ok(System.Array.Empty<MarketIndexCompanyDto>());
            }

            var symbolList = symbols.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            var result = await _marketIndicesService.GetStocksBySymbolsAsync(symbolList, cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves index attribution analytics (Tug of war, Top Lifters/Draggers, Circle slices, and Point Contributions).
        /// Supports "nifty-50", "sensex", and "bank-nifty".
        /// </summary>
        [HttpGet("attribution/{indexKey}")]
        [ProducesResponseType(typeof(IndexAttributionResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetIndexAttribution(
            string indexKey,
            [FromQuery] bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var result = await _marketIndicesService.GetIndexAttributionAsync(indexKey, forceRefresh, cancellationToken);
            if (result == null)
            {
                return NotFound(new { message = $"Attribution data for index '{indexKey}' could not be computed." });
            }
            return Ok(result);
        }
    }
}
