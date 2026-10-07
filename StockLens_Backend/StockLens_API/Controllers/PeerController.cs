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
    public class PeerController : ControllerBase
    {
        private readonly IPeerService _peerService;

        public PeerController(IPeerService peerService)
        {
            _peerService = peerService;
        }

        [HttpGet("{stockId:int}")]
        [ProducesResponseType(typeof(List<PeerDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStockPeersByStockId(
            [FromRoute] int stockId,
            [FromQuery] bool forceRefresh = false)
        {
            try
            {
                var peers = await _peerService.GetStockPeersByStockIdAsync(stockId, forceRefresh, HttpContext.RequestAborted);
                return Ok(peers);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
        }

        [HttpGet("symbol/{symbol}")]
        [ProducesResponseType(typeof(List<PeerDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStockPeersBySymbol(
            [FromRoute] string symbol,
            [FromQuery] string? exchange = "NSE",
            [FromQuery] bool forceRefresh = false)
        {
            var peers = await _peerService.GetStockPeersAsync(symbol, exchange, forceRefresh, HttpContext.RequestAborted);
            return Ok(peers);
        }
    }
}
