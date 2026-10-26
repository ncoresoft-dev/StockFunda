using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class MarketTickerService : IMarketTickerService
    {
        private readonly IMarketTickerRepository _repository;
        private readonly IYahooFinanceClient _yahooClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<MarketTickerService> _logger;

        private const string CacheKey = "market_ticker_quotes_cache";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DbFreshnessTtl = TimeSpan.FromMinutes(10);

        private static readonly List<(string Symbol, string DisplayName, string ShortName, string InstrumentType, int Order)> DefaultTickers = new()
        {
            ("^NSEI", "NIFTY 50", "NIFTY", "INDEX", 1),
            ("^BSESN", "S&P BSE SENSEX", "SENSEX", "INDEX", 2),
            ("^NSEBANK", "NIFTY BANK", "BANK NIFTY", "INDEX", 3),
            ("^CNXIT", "NIFTY IT", "NIFTY IT", "INDEX", 4),
            ("INR=X", "USD/INR", "USD/INR", "CURRENCY", 5),
            ("^INDIAVIX", "INDIA VIX", "INDIA VIX", "INDEX", 6),
            ("^CNXAUTO", "NIFTY AUTO", "NIFTY AUTO", "INDEX", 7),
            ("^CNXPHARMA", "NIFTY PHARMA", "NIFTY PHARMA", "INDEX", 8)
        };

        public MarketTickerService(
            IMarketTickerRepository repository,
            IYahooFinanceClient yahooClient,
            IMemoryCache memoryCache,
            ILogger<MarketTickerService> logger)
        {
            _repository = repository;
            _yahooClient = yahooClient;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task<IEnumerable<MarketTickerQuoteDto>> GetMarketTickerQuotesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            // 1. Check in-memory cache first if not force refresh
            if (!forceRefresh && _memoryCache.TryGetValue(CacheKey, out List<MarketTickerQuoteDto>? cachedDtos) && cachedDtos != null && cachedDtos.Count > 0)
            {
                return cachedDtos;
            }

            // 2. Check Database records
            var dbQuotes = await _repository.GetAllQuotesAsync(cancellationToken);
            var isDbFresh = dbQuotes.Count >= DefaultTickers.Count &&
                            dbQuotes.All(q => (DateTime.UtcNow - q.LastUpdated) < DbFreshnessTtl);

            if (!forceRefresh && isDbFresh)
            {
                var dtos = dbQuotes.Select(MapToDto).ToList();
                _memoryCache.Set(CacheKey, dtos, CacheTtl);
                return dtos;
            }

            // 3. Stale or forceRefresh: Fetch live from Yahoo Finance API in parallel
            _logger.LogInformation("Updating Market Ticker Quotes from Yahoo Finance API (ForceRefresh: {ForceRefresh})", forceRefresh);
            var updatedEntities = new List<MarketTickerQuote>();

            var fetchTasks = DefaultTickers.Select(async ticker =>
            {
                try
                {
                    var liveQuote = await _yahooClient.GetLiveQuoteAsync(ticker.Symbol, cancellationToken: cancellationToken);
                    if (liveQuote != null && liveQuote.Price.HasValue && liveQuote.Price.Value > 0)
                    {
                        var entity = new MarketTickerQuote
                        {
                            Symbol = ticker.Symbol,
                            DisplayName = ticker.DisplayName,
                            ShortName = ticker.ShortName,
                            InstrumentType = ticker.InstrumentType,
                            DisplayOrder = ticker.Order,
                            CurrentPrice = Math.Round(liveQuote.Price.Value, 2),
                            PreviousClose = liveQuote.PreviousClose.HasValue ? Math.Round(liveQuote.PreviousClose.Value, 2) : null,
                            Change = liveQuote.Change.HasValue ? Math.Round(liveQuote.Change.Value, 2) : null,
                            ChangePercent = liveQuote.ChangePercent.HasValue ? Math.Round(liveQuote.ChangePercent.Value, 2) : null,
                            DayHigh = liveQuote.DayHigh.HasValue ? Math.Round(liveQuote.DayHigh.Value, 2) : null,
                            DayLow = liveQuote.DayLow.HasValue ? Math.Round(liveQuote.DayLow.Value, 2) : null,
                            LastUpdated = DateTime.UtcNow
                        };
                        return entity;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch live quote for ticker {Symbol} from Yahoo Finance", ticker.Symbol);
                }

                // Fallback to existing DB quote if live fetch failed
                var existing = dbQuotes.FirstOrDefault(q => q.Symbol.ToUpper() == ticker.Symbol.ToUpper());
                return existing;
            });

            var fetchedResults = await Task.WhenAll(fetchTasks);
            foreach (var res in fetchedResults)
            {
                if (res != null)
                {
                    updatedEntities.Add(res);
                }
            }

            // 4. Persist updated quotes in Database
            if (updatedEntities.Count > 0)
            {
                try
                {
                    await _repository.SaveOrUpdateQuotesAsync(updatedEntities, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save updated market ticker quotes to database");
                }
            }

            // 5. Final fallback to existing DB if updatedEntities is empty
            var finalEntities = updatedEntities.Count > 0 ? updatedEntities : dbQuotes;
            var resultDtos = finalEntities.OrderBy(q => q.DisplayOrder).Select(MapToDto).ToList();

            _memoryCache.Set(CacheKey, resultDtos, CacheTtl);
            return resultDtos;
        }

        private static MarketTickerQuoteDto MapToDto(MarketTickerQuote entity)
        {
            var isCurrency = string.Equals(entity.InstrumentType, "CURRENCY", StringComparison.OrdinalIgnoreCase);
            var isVix = entity.Symbol.Contains("VIX", StringComparison.OrdinalIgnoreCase);

            string formattedPrice;
            if (isCurrency)
            {
                formattedPrice = $"₹{entity.CurrentPrice:N2}";
            }
            else if (isVix)
            {
                formattedPrice = $"{entity.CurrentPrice:N2}";
            }
            else
            {
                formattedPrice = $"{entity.CurrentPrice:N0}";
            }

            var sign = (entity.ChangePercent ?? 0) >= 0 ? "+" : "";
            var formattedChange = $"{sign}{entity.ChangePercent ?? 0:N1}%";

            return new MarketTickerQuoteDto
            {
                Symbol = entity.Symbol,
                DisplayName = entity.DisplayName,
                ShortName = entity.ShortName,
                CurrentPrice = entity.CurrentPrice,
                PreviousClose = entity.PreviousClose,
                Change = entity.Change,
                ChangePercent = entity.ChangePercent,
                DayHigh = entity.DayHigh,
                DayLow = entity.DayLow,
                InstrumentType = entity.InstrumentType,
                DisplayOrder = entity.DisplayOrder,
                LastUpdated = entity.LastUpdated,
                FormattedPrice = formattedPrice,
                FormattedChange = formattedChange
            };
        }
    }
}
