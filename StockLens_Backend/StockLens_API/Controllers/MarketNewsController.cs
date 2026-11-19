using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/market-news")]
    [Produces("application/json")]
    public class MarketNewsController : ControllerBase
    {
        private readonly IMarketNewsService _marketNewsService;

        public MarketNewsController(IMarketNewsService marketNewsService)
        {
            _marketNewsService = marketNewsService;
        }

        /// <summary>
        /// Retrieves the latest market news (Sensex, Nifty, Stock Market) from the background cache.
        /// Does NOT invoke external RSS feeds on user requests.
        /// </summary>
        /// <param name="limit">Max number of news items to return (default: 10, max: 50).</param>
        /// <param name="category">Optional category filter (e.g. 'Market Indices', 'Economy & Policy', 'Corporate', 'Global Markets').</param>
        [HttpGet]
        [HttpGet("latest")]
        [ProducesResponseType(typeof(MarketNewsResponseDto), StatusCodes.Status200OK)]
        public IActionResult GetMarketNews([FromQuery] int limit = 10, [FromQuery] string? category = null)
        {
            var response = _marketNewsService.GetMarketNews(limit, category);
            return Ok(response);
        }
    }
}
