using StockLens_DataLayer.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StockLens_DataLayer.Interfaces
{
    public interface IStockPeerRepository
    {
        Task<List<StockPeer>> GetPeersByStockIdAsync(int stockId);
        Task SavePeersAsync(int stockId, List<StockPeer> peers);
    }
}
