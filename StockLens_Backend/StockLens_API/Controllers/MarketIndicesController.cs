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
    }
}
