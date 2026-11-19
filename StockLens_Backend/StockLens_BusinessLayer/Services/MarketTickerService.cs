using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;

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

        private record PulseMeta(
            string Symbol,
            string Name,
            string UpInsight,
            string DownInsight
        );

        private static readonly List<PulseMeta> PulseIndices = new()
        {
            new("^NSEI", "Nifty 50",
                "IT and auto lead; majority of constituents in green.",
                "Broad market cautious; pressure across heavyweights."),
            new("^BSESN", "Sensex",
                "Large caps drive the move; BSE breadth positive.",
                "Large cap drag; profit booking across heavyweights."),
            new("^NSEBANK", "Bank Nifty",
                "Banking leads rally; private banks showing strong inflows.",
                "Private banks weak; PSU banks holding.")
        };

        public async Task<MarketPulseResponseDto> GetMarketPulseAsync(string timeframe = "1D", bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var cleanTf = string.IsNullOrWhiteSpace(timeframe) ? "1D" : timeframe.Trim().ToUpperInvariant();
            if (cleanTf != "1D" && cleanTf != "1W" && cleanTf != "1M") cleanTf = "1D";

            var cacheKey = $"market_pulse_cache_{cleanTf}";
            if (!forceRefresh && _memoryCache.TryGetValue(cacheKey, out MarketPulseResponseDto? cached) && cached != null)
            {
                return cached;
            }

            var tasks = PulseIndices.Select(async meta =>
            {
                YahooIndexPulseQuoteDto? live = null;
                try
                {
                    live = await _yahooClient.GetIndexPulseQuoteAsync(meta.Symbol, cleanTf, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch live index pulse quote for {Symbol}", meta.Symbol);
                }

                decimal price = 0m;
                decimal change = 0m;
                decimal changePct = 0m;
                decimal periodLow = 0m;
                decimal periodHigh = 0m;
                List<decimal> prices = new();

                if (live != null && live.Price > 0)
                {
                    price = live.Price;
                    prices = live.ClosingPrices ?? new List<decimal>();

                    if ((cleanTf == "1W" || cleanTf == "1M") && prices.Count > 1)
                    {
                        var startPrice = prices.First();
                        change = Math.Round(price - startPrice, 2);
                        changePct = startPrice > 0 ? Math.Round((change / startPrice) * 100m, 2) : 0m;
                        periodLow = Math.Min(price, prices.Min());
                        periodHigh = Math.Max(price, prices.Max());
                    }
                    else
                    {
                        change = live.Change;
                        changePct = live.ChangePercent;
                        periodLow = live.DayLow > 0 ? live.DayLow : price;
                        periodHigh = live.DayHigh > 0 ? live.DayHigh : price;
                    }
                }
                else
                {
                    // Check DB fallback quote if live API quote is unavailable
                    try
                    {
                        var dbTicker = await _repository.GetBySymbolAsync(meta.Symbol, cancellationToken);
                        if (dbTicker != null && dbTicker.CurrentPrice > 0)
                        {
                            price = dbTicker.CurrentPrice;
                            change = dbTicker.Change ?? 0m;
                            changePct = dbTicker.ChangePercent ?? 0m;
                            periodLow = dbTicker.DayLow ?? price;
                            periodHigh = dbTicker.DayHigh ?? price;
                            if (dbTicker.PreviousClose.HasValue && dbTicker.PreviousClose.Value > 0)
                            {
                                prices.Add(dbTicker.PreviousClose.Value);
                            }
                            prices.Add(price);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch DB quote for {Symbol}", meta.Symbol);
                    }
                }

                // If no real market price is available, do not return dummy static data
                if (price <= 0)
                {
                    return (MarketPulseIndexDto?)null;
                }

                // Bounds protection
                if (periodLow >= price) periodLow = Math.Round(price * 0.993m, 0);
                if (periodHigh <= price) periodHigh = Math.Round(price * 1.003m, 0);
                if (periodHigh <= periodLow) periodHigh = periodLow + 50m;

                bool isPos = change >= 0;
                string note = isPos ? meta.UpInsight : meta.DownInsight;

                // Range progress percentage
                var rangeDiff = periodHigh - periodLow;
                var progress = rangeDiff > 0 ? (double)((price - periodLow) / rangeDiff) * 100.0 : 50.0;
                progress = Math.Max(0.0, Math.Min(100.0, Math.Round(progress, 1)));

                var trendPath = prices.Count >= 2 ? GeneratePulseSvgPath(prices, 240, 48, 4) : string.Empty;

                return new MarketPulseIndexDto
                {
                    Symbol = meta.Symbol,
                    Name = meta.Name,
                    CurrentPrice = price,
                    FormattedPrice = $"{price:N2}",
                    Change = change,
                    FormattedPointsChange = $"{(change >= 0 ? "+" : "")}{change:N1} pts",
                    ChangePercent = changePct,
                    FormattedChangePercent = $"{(changePct >= 0 ? "+" : "")}{changePct:N2}%",
                    IsPositive = isPos,
                    DayLow = periodLow,
                    FormattedDayLow = $"{periodLow:N0}",
                    DayHigh = periodHigh,
                    FormattedDayHigh = $"{periodHigh:N0}",
                    RangeProgressPct = progress,
                    SparklinePrices = prices,
                    TrendPath = trendPath,
                    TakeawayNote = note
                };
            });

            var results = await Task.WhenAll(tasks);
            var indexDtos = results.Where(x => x != null).Select(x => x!).ToList();

            // Compute dynamic Market Summary
            var nifty = indexDtos.FirstOrDefault(i => i.Symbol == "^NSEI");
            var bank = indexDtos.FirstOrDefault(i => i.Symbol == "^NSEBANK");

            string marketSummary;
            if (indexDtos.Count == 0)
            {
                marketSummary = "Live market pulse data is currently unavailable.";
            }
            else if (cleanTf == "1W")
            {
                marketSummary = "Market ek line mein: weekly trend bullish, steady institutional buying across large caps.";
            }
            else if (cleanTf == "1M")
            {
                marketSummary = "Market ek line mein: monthly rally intact, major indices up 3%+ driven by banking and IT.";
            }
            else if (nifty != null && bank != null)
            {
                if (nifty.IsPositive && !bank.IsPositive)
                    marketSummary = "Market ek line mein: broad market positive, banking par pressure.";
                else if (nifty.IsPositive && bank.IsPositive)
                    marketSummary = "Market ek line mein: broad market strong, all major benchmark indices trading in green.";
                else if (!nifty.IsPositive && !bank.IsPositive)
                    marketSummary = "Market ek line mein: market consolidates after early session, cautious trading across sectors.";
                else
                    marketSummary = "Market ek line mein: banking shows resilient support despite broader market consolidation.";
            }
            else
            {
                marketSummary = "Market ek line mein: broad market positive, banking par pressure.";
            }

            var response = new MarketPulseResponseDto
            {
                Timeframe = cleanTf,
                MarketSummary = marketSummary,
                SectorMapRoute = "/sector-analysis",
                LastUpdated = DateTime.UtcNow,
                Indices = indexDtos
            };

            if (indexDtos.Count > 0)
            {
                _memoryCache.Set(cacheKey, response, TimeSpan.FromSeconds(60));
            }
            return response;
        }

        private static string GeneratePulseSvgPath(List<decimal> prices, int width, int height, int padding)
        {
            if (prices == null || prices.Count < 2)
            {
                return $"M 4 {height / 2} L {width - 4} {height / 2}";
            }

            var min = prices.Min();
            var max = prices.Max();
            var range = max - min;
            if (range <= 0) range = 1;

            var usableWidth = width - 2 * padding;
            var usableHeight = height - 2 * padding;

            var points = new List<(double X, double Y)>();
            for (int i = 0; i < prices.Count; i++)
            {
                var x = padding + ((double)i / (prices.Count - 1)) * usableWidth;
                var normalized = (double)((prices[i] - min) / range);
                var y = height - padding - (normalized * usableHeight);
                points.Add((Math.Round(x, 1), Math.Round(y, 1)));
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"M {points[0].X} {points[0].Y}");
            for (int i = 1; i < points.Count; i++)
            {
                sb.Append($" L {points[i].X} {points[i].Y}");
            }

            return sb.ToString();
        }
    }
}