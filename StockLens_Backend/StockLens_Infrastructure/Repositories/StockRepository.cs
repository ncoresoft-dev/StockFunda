using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class StockRepository : IStockRepository
    {
        private readonly StockLensDataContext _context;

        public StockRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<Stock?> GetByIdAsync(int id)
        {
            return await _context.Stocks
                .Include(s => s.Company)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<Stock?> GetBySymbolAsync(string symbol, string? exchange = null)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cleanExchange = exchange?.Trim().ToUpperInvariant();

            var query = _context.Stocks.Include(s => s.Company).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(cleanExchange))
            {
                return await query.FirstOrDefaultAsync(s => s.Symbol == cleanSymbol && s.Exchange == cleanExchange);
            }

            return await query.FirstOrDefaultAsync(s => s.Symbol == cleanSymbol);
        }

        public async Task<IEnumerable<Stock>> GetAllStocksAsync()
        {
            return await _context.Stocks
                .Include(s => s.Company)
                .AsNoTracking()
                .OrderBy(s => s.Symbol)
                .ToListAsync();
        }

        public async Task<Stock> AddAsync(Stock stock)
        {
            var entry = await _context.Stocks.AddAsync(stock);
            return entry.Entity;
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _stockLocks = new();

        public async Task<Stock> GetOrCreateStockAsync(
            string symbol, 
            string? exchange = "NSE", 
            string? companyName = null, 
            string? industry = null, 
            System.Threading.CancellationToken cancellationToken = default)
        {
            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
            var lockKey = $"{cleanSymbol}_{cleanExchange}";

            var existing = await GetBySymbolAsync(cleanSymbol, cleanExchange);
            if (existing != null)
            {
                return existing;
            }

            var semaphore = _stockLocks.GetOrAdd(lockKey, _ => new System.Threading.SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                // Double check inside lock
                existing = await GetBySymbolAsync(cleanSymbol, cleanExchange);
                if (existing != null)
                {
                    return existing;
                }

                // Check if company exists in CompanyMaster
                var existingCompany = await _context.CompanyMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Symbol == cleanSymbol, cancellationToken);

                var stock = new Stock
                {
                    Symbol = cleanSymbol,
                    Exchange = cleanExchange,
                    CreatedAt = System.DateTime.UtcNow,
                    UpdatedAt = System.DateTime.UtcNow
                };

                if (existingCompany != null)
                {
                    stock.CompanyId = existingCompany.Id;
                    stock.Company = existingCompany;
                }
                else
                {
                    stock.Company = new Company
                    {
                        CompanyName = !string.IsNullOrWhiteSpace(companyName) ? companyName : $"{cleanSymbol} Limited",
                        Symbol = cleanSymbol,
                        Industry = industry,
                        CreatedAt = System.DateTime.UtcNow,
                        UpdatedAt = System.DateTime.UtcNow
                    };
                }

                try
                {
                    await _context.Stocks.AddAsync(stock, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    return stock;
                }
                catch (DbUpdateException)
                {
                    // Fallback for concurrent INSERT race across transactions/instances
                    _context.ChangeTracker.Clear();
                    var created = await GetBySymbolAsync(cleanSymbol, cleanExchange);
                    if (created != null)
                    {
                        return created;
                    }
                    throw;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
