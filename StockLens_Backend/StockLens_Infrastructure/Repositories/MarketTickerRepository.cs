using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class MarketTickerRepository : IMarketTickerRepository
    {
        private readonly StockLensDataContext _context;

        public MarketTickerRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<List<MarketTickerQuote>> GetAllQuotesAsync(CancellationToken cancellationToken = default)
        {
            return await _context.MarketTickerQuotes
                .OrderBy(q => q.DisplayOrder)
                .ThenBy(q => q.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<MarketTickerQuote?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            return await _context.MarketTickerQuotes
                .FirstOrDefaultAsync(q => q.Symbol.ToUpper() == cleanSymbol, cancellationToken);
        }

        public async Task SaveOrUpdateQuotesAsync(IEnumerable<MarketTickerQuote> quotes, CancellationToken cancellationToken = default)
        {
            if (quotes == null) return;

            foreach (var quote in quotes)
            {
                var existing = await _context.MarketTickerQuotes
                    .FirstOrDefaultAsync(q => q.Symbol.ToUpper() == quote.Symbol.ToUpper(), cancellationToken);

                if (existing != null)
                {
                    existing.CurrentPrice = quote.CurrentPrice;
                    existing.PreviousClose = quote.PreviousClose;
                    existing.Change = quote.Change;
                    existing.ChangePercent = quote.ChangePercent;
                    existing.DayHigh = quote.DayHigh;
                    existing.DayLow = quote.DayLow;
                    existing.DisplayName = string.IsNullOrWhiteSpace(quote.DisplayName) ? existing.DisplayName : quote.DisplayName;
                    existing.ShortName = string.IsNullOrWhiteSpace(quote.ShortName) ? existing.ShortName : quote.ShortName;
                    existing.InstrumentType = quote.InstrumentType;
                    existing.DisplayOrder = quote.DisplayOrder != 0 ? quote.DisplayOrder : existing.DisplayOrder;
                    existing.LastUpdated = DateTime.UtcNow;
                }
                else
                {
                    quote.LastUpdated = DateTime.UtcNow;
                    await _context.MarketTickerQuotes.AddAsync(quote, cancellationToken);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
