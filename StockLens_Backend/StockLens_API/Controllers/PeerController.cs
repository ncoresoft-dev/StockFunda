using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/peers")]
    [Produces("application/json")]
    public class PeerController : ControllerBase
    {
        private readonly IPeerService _peerService;

        public PeerController(IPeerService peerService)
        {
            _peerService = peerService;
        }

        /// <summary>
        /// Retrieves sector and industry peer comparison metrics for a stock by symbol.
        /// Supports both standard route GET /api/peers/{symbol} and legacy GET /api/peers/symbol/{symbol}.
        /// </summary>
        /// <param name="symbol">Stock symbol (e.g. RELIANCE, TCS, INFY).</param>
        /// <param name="exchange">Exchange code (NSE or BSE, default: NSE).</param>
        /// <param name="forceRefresh">Force fresh synchronization from providers.</param>
        [HttpGet("{symbol}")]
        [HttpGet("symbol/{symbol}")]
        [ProducesResponseType(typeof(List<PeerDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetStockPeersBySymbol(
            [FromRoute] string symbol,
            [FromQuery] string? exchange = "NSE",
            [FromQuery] bool forceRefresh = false)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "Stock symbol is required." });
            }

            var peers = await _peerService.GetStockPeersAsync(symbol, exchange, forceRefresh, HttpContext.RequestAborted);
            return Ok(peers);
        }
    }
}
