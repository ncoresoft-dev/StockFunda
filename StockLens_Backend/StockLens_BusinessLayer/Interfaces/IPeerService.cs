using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IPeerService
    {
        Task<List<PeerDto>> GetStockPeersAsync(string symbol, string? exchange = "NSE", bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task<List<PeerDto>> GetStockPeersByStockIdAsync(int stockId, bool forceRefresh = false, CancellationToken cancellationToken = default);
    }
}
