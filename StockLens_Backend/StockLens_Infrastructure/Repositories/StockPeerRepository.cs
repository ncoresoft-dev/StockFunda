using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class StockPeerRepository : IStockPeerRepository
    {
        private readonly StockLensDataContext _context;

        public StockPeerRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<List<StockPeer>> GetPeersByStockIdAsync(int stockId)
        {
            return await _context.StockPeers
                .Where(p => p.StockId == stockId)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task SavePeersAsync(int stockId, List<StockPeer> peers)
        {
            var existingPeers = await _context.StockPeers.Where(p => p.StockId == stockId).ToListAsync();
            _context.StockPeers.RemoveRange(existingPeers);
            
            foreach (var peer in peers)
            {
                peer.StockId = stockId;
                _context.StockPeers.Add(peer);
            }
            
            await _context.SaveChangesAsync();
        }
    }
}
