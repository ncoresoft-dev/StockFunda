using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockLens_DataLayer.Entities;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Seeders
{
    public class DailyValuationUpdateResult
    {
        public int TotalStocksFound { get; set; }
        public int TotalUpdated { get; set; }
        public int TotalFailed { get; set; }
        public List<string> UpdatedSymbols { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public TimeSpan ElapsedTime { get; set; }
    }

    public interface IDailyValuationService
    {
        Task<DailyValuationUpdateResult> UpdateDailyValuationsAsync(CancellationToken cancellationToken = default);
    }

    public class DailyValuationService : IDailyValuationService
    {
        private readonly StockLensDataContext _dbContext;
        private readonly IYahooFinanceClient _yahooClient;
        private readonly ILogger<DailyValuationService> _logger;

        public DailyValuationService(
            StockLensDataContext dbContext,
            IYahooFinanceClient yahooClient,
            ILogger<DailyValuationService> logger)
        {
            _dbContext = dbContext;
            _yahooClient = yahooClient;
            _logger = logger;
        }

        /// <summary>
        /// Screener.in / Trendlyne style Daily EOD Update:
        /// 1. Iterates over all stocks currently tracked in DB.
        /// 2. Fetches today's closing price and day change via Yahoo Finance (0 IndianAPI calls spent).
        /// 3. Recalculates valuation multiples dynamically:
        ///    - MarketCap = Price * TotalShares
        ///    - PE = Price / TTM EPS
        ///    - PB = Price / BookValue
        /// 4. Updates StockFinancials and MarketTickerQuotes with today's figures.
        /// </summary>
        public async Task<DailyValuationUpdateResult> UpdateDailyValuationsAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var result = new DailyValuationUpdateResult();

            // 1. Fetch all stocks that have existing financial records
            var seededStocks = await _dbContext.Stocks
                .Include(s => s.Company)
                .Include(s => s.Financials)
                .Where(s => s.Financials.Any())
                .ToListAsync(cancellationToken);

            result.TotalStocksFound = seededStocks.Count;
            _logger.LogInformation("Starting Daily EOD Valuation Update for {Count} seeded stocks", seededStocks.Count);

            var now = DateTime.UtcNow;

            foreach (var stock in seededStocks)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var symbol = stock.Symbol.Trim().ToUpperInvariant();

                try
                {
                    // 2. Derive existing baseline valuation parameters
                    var latestFin = stock.Financials
                        .OrderByDescending(f => f.PeriodEndDate ?? f.LastSyncedAt)
                        .FirstOrDefault();

                    if (latestFin == null)
                    {
                        continue;
                    }

                    // Total Shares = MarketCap / Price
                    decimal? totalSharesCr = null;
                    if (latestFin.MarketCap.HasValue && latestFin.CurrentPrice.HasValue && latestFin.CurrentPrice.Value > 0)
                    {
                        totalSharesCr = latestFin.MarketCap.Value / latestFin.CurrentPrice.Value;
                    }

                    // TTM EPS = Sum of last 4 quarters' EPS, or derived from Price / PE
                    var last4Quarters = stock.Financials
                        .Where(f => f.PeriodType == "quarterly" && f.Eps.HasValue)
                        .OrderByDescending(f => f.PeriodEndDate ?? f.LastSyncedAt)
                        .Take(4)
                        .ToList();

                    decimal? ttmEps = null;
                    if (last4Quarters.Count >= 2)
                    {
                        // Annualized or actual sum
                        var sumEps = last4Quarters.Sum(q => q.Eps!.Value);
                        ttmEps = last4Quarters.Count == 4 ? sumEps : (sumEps / last4Quarters.Count) * 4;
                    }
                    else if (latestFin.CurrentPrice.HasValue && latestFin.PeRatio.HasValue && latestFin.PeRatio.Value > 0)
                    {
                        ttmEps = latestFin.CurrentPrice.Value / latestFin.PeRatio.Value;
                    }

                    var bookValue = latestFin.BookValue;

                    // 3. Fetch today's closing quote from Yahoo Finance API (free, reliable, zero IndianAPI cost)
                    var quote = await _yahooClient.GetLiveQuoteAsync(symbol, stock.Exchange, cancellationToken);
                    if (quote == null || !quote.Price.HasValue || quote.Price.Value <= 0)
                    {
                        _logger.LogWarning("Daily update: Could not fetch quote for {Symbol}", symbol);
                        result.TotalFailed++;
                        continue;
                    }

                    var currentPrice = Math.Round(quote.Price.Value, 2);
                    var change = quote.Change.HasValue ? Math.Round(quote.Change.Value, 2) : (decimal?)null;
                    var changePercent = quote.ChangePercent.HasValue ? Math.Round(quote.ChangePercent.Value, 2) : (decimal?)null;
                    var prevClose = quote.PreviousClose.HasValue
                        ? Math.Round(quote.PreviousClose.Value, 2)
                        : (change.HasValue ? currentPrice - change.Value : (decimal?)null);

                    // 4. Recalculate multiples dynamically
                    decimal? recalculatedMarketCap = null;
                    if (totalSharesCr.HasValue && totalSharesCr.Value > 0)
                    {
                        recalculatedMarketCap = Math.Round(currentPrice * totalSharesCr.Value, 2);
                    }
                    else if (latestFin.MarketCap.HasValue)
                    {
                        // Scale proportionally by price ratio if total shares was missing
                        if (latestFin.CurrentPrice.HasValue && latestFin.CurrentPrice.Value > 0)
                        {
                            recalculatedMarketCap = Math.Round(latestFin.MarketCap.Value * (currentPrice / latestFin.CurrentPrice.Value), 2);
                        }
                    }

                    decimal? recalculatedPe = null;
                    if (ttmEps.HasValue && ttmEps.Value > 0)
                    {
                        recalculatedPe = Math.Round(currentPrice / ttmEps.Value, 2);
                    }

                    decimal? recalculatedPb = null;
                    if (bookValue.HasValue && bookValue.Value > 0)
                    {
                        recalculatedPb = Math.Round(currentPrice / bookValue.Value, 2);
                    }

                    // 5. Update StockFinancials (snapshot multiples for all rows of this stock)
                    foreach (var fin in stock.Financials)
                    {
                        fin.CurrentPrice = currentPrice;
                        if (recalculatedMarketCap.HasValue) fin.MarketCap = recalculatedMarketCap.Value;
                        if (recalculatedPe.HasValue) fin.PeRatio = recalculatedPe.Value;
                        if (recalculatedPb.HasValue) fin.PbRatio = recalculatedPb.Value;
                        fin.LastSyncedAt = now;
                        fin.UpdatedAt = now;
                    }

                    // 6. Upsert MarketTickerQuote with today's price and daily movement
                    var existingQuote = await _dbContext.MarketTickerQuotes
                        .FirstOrDefaultAsync(q => q.Symbol.ToUpper() == symbol, cancellationToken);

                    if (existingQuote == null)
                    {
                        var newQuote = new MarketTickerQuote
                        {
                            Symbol = symbol,
                            DisplayName = stock.Company?.CompanyName ?? symbol,
                            ShortName = symbol,
                            InstrumentType = "EQUITY",
                            CurrentPrice = currentPrice,
                            PreviousClose = prevClose,
                            Change = change,
                            ChangePercent = changePercent,
                            DayHigh = quote.DayHigh,
                            DayLow = quote.DayLow,
                            DisplayOrder = 100,
                            LastUpdated = now
                        };
                        await _dbContext.MarketTickerQuotes.AddAsync(newQuote, cancellationToken);
                    }
                    else
                    {
                        existingQuote.CurrentPrice = currentPrice;
                        existingQuote.PreviousClose = prevClose;
                        existingQuote.Change = change;
                        existingQuote.ChangePercent = changePercent;
                        if (quote.DayHigh.HasValue) existingQuote.DayHigh = quote.DayHigh.Value;
                        if (quote.DayLow.HasValue) existingQuote.DayLow = quote.DayLow.Value;
                        existingQuote.LastUpdated = now;
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    result.TotalUpdated++;
                    result.UpdatedSymbols.Add(symbol);
                    _logger.LogDebug("Daily update succeeded for {Symbol}: Price ₹{Price}, MCap ₹{MCap}Cr, PE {PE}",
                        symbol, currentPrice, recalculatedMarketCap, recalculatedPe);

                    // Polite 100ms throttle between stocks
                    await Task.Delay(100, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Daily update failed for {Symbol}", symbol);
                    result.TotalFailed++;
                    result.Errors.Add($"{symbol}: {ex.Message}");
                }
            }

            sw.Stop();
            result.ElapsedTime = sw.Elapsed;
            _logger.LogInformation("Daily EOD Valuation Update completed: {Updated}/{Total} stocks in {Elapsed:g}",
                result.TotalUpdated, result.TotalStocksFound, result.ElapsedTime);

            return result;
        }
    }
}

