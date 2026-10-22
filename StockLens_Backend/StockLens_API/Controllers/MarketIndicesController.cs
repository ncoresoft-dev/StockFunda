using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Api.Controllers
{
    [ApiController]
    [Route("api/v1/indices")]
    public class MarketIndicesController : ControllerBase
    {
        private readonly IMarketIndicesService _marketIndicesService;

        public MarketIndicesController(IMarketIndicesService marketIndicesService)
        {
            _marketIndicesService = marketIndicesService;
        }

        [HttpGet("nifty-50")]
        public async Task<IActionResult> GetTop50Companies(CancellationToken cancellationToken)
        {
            var result = await _marketIndicesService.GetTop50CompaniesAsync(cancellationToken);
            return Ok(result);
        }

        [HttpGet("stocks-by-symbols")]
        public async Task<IActionResult> GetStocksBySymbols([FromQuery] string symbols, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(symbols))
            {
                return Ok(System.Array.Empty<object>());
            }

            var symbolList = symbols.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            var result = await _marketIndicesService.GetStocksBySymbolsAsync(symbolList, cancellationToken);
            return Ok(result);
        }
    }
}
