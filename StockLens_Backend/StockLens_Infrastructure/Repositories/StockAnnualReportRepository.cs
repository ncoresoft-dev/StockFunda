using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class StockAnnualReportRepository : IStockAnnualReportRepository
    {
        private readonly StockLensDataContext _context;

        public StockAnnualReportRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<StockAnnualReport?> GetLatestByStockIdAsync(int stockId, CancellationToken cancellationToken = default)
        {
            return await _context.StockAnnualReports
                .AsNoTracking()
                .Where(r => r.StockId == stockId)
                .OrderByDescending(r => r.Year)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<StockAnnualReport?> GetLatestBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            return await _context.StockAnnualReports
                .AsNoTracking()
                .Include(r => r.Stock)
                .Where(r => r.Stock.Symbol == cleanSymbol)
                .OrderByDescending(r => r.Year)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task AddAsync(StockAnnualReport report, CancellationToken cancellationToken = default)
        {
            await _context.StockAnnualReports.AddAsync(report, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAsync(StockAnnualReport report, CancellationToken cancellationToken = default)
        {
            var tracked = _context.StockAnnualReports.Local.FirstOrDefault(e => e.Id == report.Id);
            if (tracked != null && !ReferenceEquals(tracked, report))
            {
                _context.Entry(tracked).State = EntityState.Detached;
            }
            _context.StockAnnualReports.Update(report);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
