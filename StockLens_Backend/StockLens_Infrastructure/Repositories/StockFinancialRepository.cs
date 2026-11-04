using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class StockFinancialRepository : IStockFinancialRepository
    {
        private readonly StockLensDataContext _context;

        public StockFinancialRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<List<StockFinancial>> GetFinancialsByStockIdAsync(int stockId, string periodType = "annual", int limit = 3)
        {
            var cleanType = string.IsNullOrWhiteSpace(periodType) ? "annual" : periodType.Trim().ToLowerInvariant();

            return await _context.StockFinancials
                .AsNoTracking()
                .Where(f => f.StockId == stockId && f.PeriodType == cleanType)
                .OrderByDescending(f => f.PeriodEndDate)
                .ThenByDescending(f => f.FiscalYear)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<StockFinancial?> GetByStockIdAndPeriodKeyAsync(int stockId, string periodKey)
        {
            if (string.IsNullOrWhiteSpace(periodKey)) return null;

            var cleanKey = periodKey.Trim().ToLowerInvariant();
            return await _context.StockFinancials
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.StockId == stockId && f.PeriodKey == cleanKey);
        }

        public async Task<StockFinancial> AddAsync(StockFinancial financial)
        {
            var entry = await _context.StockFinancials.AddAsync(financial);
            return entry.Entity;
        }

        public Task UpdateAsync(StockFinancial financial)
        {
            _context.StockFinancials.Update(financial);
            return Task.CompletedTask;
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }
    }
}
