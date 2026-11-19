using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Helpers;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;

namespace StockLens_BusinessLayer.Services
{
    public class ExploreLensService : IExploreLensService
    {
        private readonly IYahooFinanceClient _yahooClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<ExploreLensService> _logger;
        private readonly StockLensDataContext? _dbContext;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        // Core Tracked Companies Metadata
        private record CompanyMeta(
            string Symbol, 
            string ShortName, 
            string Name, 
            string Sector, 
            int Angle, 
            string ColorBg, 
            string ColorBorder,
            string? Isin = null
        );

        private static readonly List<CompanyMeta> TrackedCompanies = new()
        {
            new("RELIANCE", "RELIANCE", "Reliance Industries Limited", "Energy & Conglomerate", 270, "#d4f77c", "#aee838", "INE002A01018"),
            new("TCS", "TCS", "Tata Consultancy Services", "Technology", 315, "#bee6fd", "#83cefc", "INE467B01029"),
            new("HDFCBANK", "HDFC", "HDFC Bank Limited", "Banking & Financials", 15, "#fed7aa", "#fca565", "INE040A01034"),
            new("INFY", "INFY", "Infosys Limited", "Technology", 55, "#d0cefd", "#9b97fb", "INE009A01021"),
            new("SUNPHARMA", "SUN", "Sun Pharmaceutical", "Healthcare & Pharma", 95, "#99f6e4", "#5eead4", "INE044A01036"),
            new("TATAMOTORS", "TATA", "Tata Motors Limited", "Automobile", 135, "#fecaca", "#fca5a5", "INE155A01022"),
            new("NUVOCO", "NUVOCO", "Nuvoco Vistas Corp", "Cement & Materials", 180, "#fed7aa", "#fca565", "INE118D01016"),
            new("SENCO", "SENCO", "Senco Gold Limited", "Consumer Discretionary", 225, "#e9d5ff", "#d8b4fe", "INE602W01027")
        };

        public ExploreLensService(
            IYahooFinanceClient yahooClient,
            IMemoryCache memoryCache,
            ILogger<ExploreLensService> logger,
            StockLensDataContext? dbContext = null)
        {
            _yahooClient = yahooClient;
            _memoryCache = memoryCache;
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task<ExploreLensResponseDto> GetExploreLensDataAsync(
            string lens = "Growth", 
            bool forceRefresh = false, 
            CancellationToken cancellationToken = default)
        {
            var cleanLens = string.IsNullOrWhiteSpace(lens) ? "Growth" : lens.Trim();
            var cacheKey = $"explore_lens_cache_{cleanLens.ToLowerInvariant()}";

            if (!forceRefresh && _memoryCache.TryGetValue(cacheKey, out ExploreLensResponseDto? cached) && cached != null)
            {
                return cached;
            }

            _logger.LogInformation("Generating Explore Lens data for '{Lens}' via live Yahoo Finance API (ForceRefresh: {ForceRefresh})", cleanLens, forceRefresh);

            // Fetch live 7-day sparklines AND live Yahoo metrics for all 8 companies in parallel,
            // cached for 15 minutes so tab switching (Growth -> Cash Flow -> Ownership -> Risk) uses 0 extra API calls!
            const string rawDataCacheKey = "explore_lens_raw_companies_data";
            if (forceRefresh || !_memoryCache.TryGetValue(rawDataCacheKey, out (CompanyMeta meta, YahooSparklineQuoteDto? quote, YahooGrowthMetricsDto? growth)[]? fetchedResults) || fetchedResults == null)
            {
                var fetchTasks = TrackedCompanies.Select(async meta =>
                {
                    var quoteTask = _yahooClient.GetSparklineQuoteAsync(meta.Symbol, "NSE", 7, cancellationToken);
                    var growthTask = _yahooClient.GetGrowthMetricsAsync(meta.Symbol, "NSE", cancellationToken);

                    try
                    {
                        await Task.WhenAll(quoteTask, growthTask);
                        return (meta, quote: await quoteTask, growth: await growthTask);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch live Yahoo Finance data for {Symbol}", meta.Symbol);
                        YahooSparklineQuoteDto? q = null;
                        YahooGrowthMetricsDto? g = null;
                        try { q = await quoteTask; } catch { }
                        try { g = await growthTask; } catch { }
                        return (meta, quote: q, growth: g);
                    }
                });

                fetchedResults = await Task.WhenAll(fetchTasks);
                _memoryCache.Set(rawDataCacheKey, fetchedResults, TimeSpan.FromMinutes(15));
            }

            // 1. Fetch DB company logos and DB fallback quotes if DbContext is available
            Dictionary<string, string?> dbLogos = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, MarketTickerQuote> dbQuotes = new(StringComparer.OrdinalIgnoreCase);
            if (_dbContext != null)
            {
                try
                {
                    var symbols = TrackedCompanies.Select(t => t.Symbol.ToUpperInvariant()).ToList();
                    var companies = await _dbContext.CompanyMaster
                        .AsNoTracking()
                        .Where(c => symbols.Contains(c.Symbol.ToUpper()))
                        .ToListAsync(cancellationToken);

                    foreach (var c in companies)
                    {
                        if (!string.IsNullOrWhiteSpace(c.LogoUrl))
                        {
                            dbLogos[c.Symbol.ToUpperInvariant()] = c.LogoUrl;
                        }
                    }

                    var tickerQuotes = await _dbContext.MarketTickerQuotes
                        .AsNoTracking()
                        .Where(q => symbols.Contains(q.Symbol.ToUpper()))
                        .ToListAsync(cancellationToken);

                    foreach (var tq in tickerQuotes)
                    {
                        dbQuotes[tq.Symbol.ToUpperInvariant()] = tq;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not query CompanyMaster or MarketTickerQuotes for ExploreLens");
                }
            }

            var companyDtos = new List<ExploreLensCompanyDto>();
            foreach (var (meta, quote, growth) in fetchedResults)
            {
                var price = quote != null && quote.Price > 0 ? quote.Price : 0m;
                var change = quote?.Change ?? 0m;
                var changePercent = quote?.ChangePercent ?? 0m;

                // Fallback to real DB quote if live quote returned price <= 0
                if (price <= 0 && dbQuotes.TryGetValue(meta.Symbol, out var dbQ) && dbQ.CurrentPrice > 0)
                {
                    price = dbQ.CurrentPrice;
                    change = dbQ.Change ?? 0m;
                    changePercent = dbQ.ChangePercent ?? 0m;
                }

                var isPos = changePercent >= 0;

                var prices = (quote?.ClosingPrices != null && quote.ClosingPrices.Count >= 2)
                    ? quote.ClosingPrices
                    : new List<decimal>();

                if (prices.Count < 2 && price > 0)
                {
                    var prevPrice = (price - change > 0) ? (price - change) : price;
                    prices = new List<decimal> { prevPrice, price };
                }

                var trendPath = prices.Count >= 2 ? GenerateSvgSparklinePath(prices, 280, 70, 10) : string.Empty;
                var firstPrice = prices.Count >= 2 ? prices.First() : 0m;
                var lastPrice = prices.Count >= 2 ? prices.Last() : 0m;
                var trendPos = lastPrice >= firstPrice;
                decimal sevenDayChangePercent = firstPrice > 0
                    ? Math.Round(((lastPrice - firstPrice) / firstPrice) * 100m, 1)
                    : 0m;
                string formattedSevenDayChange = prices.Count >= 2
                    ? $"{(sevenDayChangePercent >= 0 ? "+" : "")}{sevenDayChangePercent:N1}%"
                    : "—";

                var (lensMetrics, dynamicInsight) = BuildLensPillsAndInsight(cleanLens, meta, growth);

                // Priority: DB LogoUrl -> CompanyLogoHelper.BuildLogoUrl(ISIN)
                var logoUrl = (dbLogos.TryGetValue(meta.Symbol, out var dbLogo) && !string.IsNullOrWhiteSpace(dbLogo))
                    ? dbLogo
                    : CompanyLogoHelper.BuildLogoUrl(meta.Isin);

                companyDtos.Add(new ExploreLensCompanyDto
                {
                    Symbol = meta.Symbol,
                    ShortName = meta.ShortName,
                    Name = meta.Name,
                    Sector = meta.Sector,
                    LogoUrl = logoUrl,
                    CurrentPrice = price,
                    FormattedPrice = price > 0 ? $"₹{price:N0}" : "—",
                    Change = change,
                    ChangePercent = changePercent,
                    FormattedChange = price > 0 ? $"{(changePercent >= 0 ? "+" : "")}{changePercent:N1}%" : "—",
                    IsPositive = isPos,
                    Score = 3,
                    OrbitRadius = 135,
                    OrbitAngle = meta.Angle,
                    ColorBg = meta.ColorBg,
                    ColorBorder = meta.ColorBorder,
                    SevenDayPrices = prices,
                    SevenDayChangePercent = sevenDayChangePercent,
                    FormattedSevenDayChange = formattedSevenDayChange,
                    TrendPath = trendPath,
                    TrendPositive = trendPos,
                    Metrics = lensMetrics,
                    Insight = dynamicInsight
                });
            }

            // Dynamic non-overlapping orbit radii from Inner Ring (95px) to Outer Ring (175px)
            // Strongest company on this lens sits closest to the center, lowest sits farthest out
            var lensRadii = new[] { 95, 106, 118, 129, 141, 152, 164, 175 };
            var lensScores = new[] { 5, 5, 4, 4, 4, 3, 3, 2 };

            var l = cleanLens.Trim().ToLowerInvariant();
            List<ExploreLensCompanyDto> rankedCompanies;

            if (l.Contains("cash"))
            {
                // Cash Flow: Rank by Operating Margin and operational cash generation
                rankedCompanies = companyDtos.OrderByDescending(c =>
                {
                    var marginStr = c.Metrics.FirstOrDefault(m => m.Label == "Operating Margin")?.Value.Replace("%", "").Trim();
                    return decimal.TryParse(marginStr, out var mv) ? mv : 0m;
                }).ToList();
            }
            else if (l.Contains("ownership"))
            {
                // Ownership: Rank by Strong Hands holding (Promoter + 0.9 * Institutional + 0.02 * InstCount)
                rankedCompanies = companyDtos.OrderByDescending(c =>
                {
                    var pStr = c.Metrics.FirstOrDefault(m => m.Label == "Promoter Holding")?.Value.Replace("%", "").Trim();
                    var iStr = c.Metrics.FirstOrDefault(m => m.Label == "Institutional Holding")?.Value.Replace("%", "").Trim();
                    var cStr = c.Metrics.FirstOrDefault(m => m.Label == "Institutions Count")?.Value.Replace("Funds", "").Trim();

                    var prom = decimal.TryParse(pStr, out var pv) ? pv : 0m;
                    var inst = decimal.TryParse(iStr, out var iv) ? iv : 0m;
                    var cnt = int.TryParse(cStr, out var cv) ? cv : 0;

                    return prom + (inst * 0.9m) + (cnt * 0.02m);
                }).ToList();
            }
            else if (l.Contains("risk"))
            {
                // Risk: LOWER RISK = BETTER / CLOSER TO CENTER (Score 5, Radius 95px)
                // Low Debt to Equity + Low Beta + Healthy Current Ratio
                rankedCompanies = companyDtos.OrderBy(c =>
                {
                    var deStr = c.Metrics.FirstOrDefault(m => m.Label == "Debt to Equity")?.Value.Replace("x", "").Trim();
                    var betaStr = c.Metrics.FirstOrDefault(m => m.Label.StartsWith("Beta"))?.Value.Trim();
                    var crStr = c.Metrics.FirstOrDefault(m => m.Label == "Current Ratio")?.Value.Replace("x", "").Trim();

                    var de = decimal.TryParse(deStr, out var dev) ? dev : 10m;
                    var beta = decimal.TryParse(betaStr, out var bv) ? bv : 2m;
                    var cr = decimal.TryParse(crStr, out var crv) ? crv : 0m;

                    return (de * 50m) + (beta * 50m) - (cr * 10m);
                }).ToList();
            }
            else
            {
                // Growth: 40% Sales + 40% Profit + 20% 1-Year Return
                rankedCompanies = companyDtos.OrderByDescending(c =>
                {
                    var s = decimal.TryParse(c.Metrics.FirstOrDefault(m => m.Label == "Sales Growth YoY")?.Value.Replace("%", "").Replace("+", "").Trim(), out var sv) ? sv : 0m;
                    var p = decimal.TryParse(c.Metrics.FirstOrDefault(m => m.Label == "Profit Growth YoY")?.Value.Replace("%", "").Replace("+", "").Trim(), out var pv) ? pv : 0m;
                    var r = decimal.TryParse(c.Metrics.FirstOrDefault(m => m.Label == "1-Year Return")?.Value.Replace("%", "").Replace("+", "").Trim(), out var rv) ? rv : 0m;

                    var bs = Math.Clamp(s, -40m, 100m);
                    var bp = Math.Clamp(p, -40m, 100m);
                    var br = Math.Clamp(r, -50m, 50m);
                    return (bs * 0.40m) + (bp * 0.40m) + (br * 0.20m);
                }).ToList();
            }

            for (int i = 0; i < rankedCompanies.Count; i++)
            {
                rankedCompanies[i].Score = lensScores[Math.Min(i, lensScores.Length - 1)];
                rankedCompanies[i].OrbitRadius = lensRadii[Math.Min(i, lensRadii.Length - 1)];
            }

            // Order so strongest company on this lens is first (e.g. Rank 1 with Radius 95px)
            var sortedCompanies = companyDtos.OrderByDescending(c => c.Score)
                                             .ThenBy(c => c.OrbitRadius)
                                             .ToList();

            var response = new ExploreLensResponseDto
            {
                ActiveLens = cleanLens,
                Companies = sortedCompanies,
                LastUpdated = DateTime.UtcNow
            };

            _memoryCache.Set(cacheKey, response, CacheDuration);
            return response;
        }

        private static (List<LensMetricDto> Metrics, string Insight) BuildLensPillsAndInsight(
            string lens,
            CompanyMeta meta,
            YahooGrowthMetricsDto? growth)
        {
            var l = lens.Trim().ToLowerInvariant();

            if (l.Contains("cash"))
            {
                var margin = growth?.OperatingMargin;
                var fcf = !string.IsNullOrWhiteSpace(growth?.FreeCashFlowFormatted) ? growth.FreeCashFlowFormatted : null;
                var ocf = !string.IsNullOrWhiteSpace(growth?.OperatingCashFlowFormatted) ? growth.OperatingCashFlowFormatted : null;

                var metrics = new List<LensMetricDto>
                {
                    new() { Label = "Operating Margin", Value = margin.HasValue ? $"{margin.Value:N1}%" : "N/A", IsPositive = margin.HasValue ? margin.Value >= 15m : (bool?)null },
                    new() { Label = "Free Cash Flow", Value = fcf ?? "N/A", IsPositive = fcf != null },
                    new() { Label = "Operating Cash Flow", Value = ocf ?? "N/A", IsPositive = ocf != null }
                };

                var insight = (margin.HasValue || fcf != null || ocf != null)
                    ? GenerateDynamicCashFlowInsight(meta.Symbol, margin ?? 0m, fcf ?? "—", ocf ?? "—")
                    : $"Cash flow metrics currently unavailable from data provider for {meta.Symbol}.";
                return (metrics, insight);
            }

            if (l.Contains("ownership"))
            {
                var promoter = growth?.PromoterHolding > 0 ? growth.PromoterHolding : null;
                var institutional = growth?.InstitutionalHolding > 0 ? growth.InstitutionalHolding : null;
                var instCount = growth?.InstitutionsCount > 0 ? growth.InstitutionsCount : null;

                var metrics = new List<LensMetricDto>
                {
                    new() { Label = "Promoter Holding", Value = promoter.HasValue ? $"{promoter.Value:N1}%" : "N/A", IsPositive = promoter.HasValue ? promoter.Value >= 50m : (bool?)null },
                    new() { Label = "Institutional Holding", Value = institutional.HasValue ? $"{institutional.Value:N1}%" : "N/A", IsPositive = institutional.HasValue ? institutional.Value >= 20m : (bool?)null },
                    new() { Label = "Institutions Count", Value = instCount.HasValue ? $"{instCount.Value} Funds" : "N/A", IsPositive = instCount.HasValue ? instCount.Value >= 100 : (bool?)null }
                };

                var insight = (promoter.HasValue || institutional.HasValue || instCount.HasValue)
                    ? GenerateDynamicOwnershipInsight(meta.Symbol, promoter ?? 0m, institutional ?? 0m, instCount ?? 0)
                    : $"Shareholding structure data currently unavailable for {meta.Symbol}.";
                return (metrics, insight);
            }

            if (l.Contains("risk"))
            {
                var de = growth?.DebtToEquity > 0 ? growth.DebtToEquity : null;
                var beta = growth?.Beta > 0 ? growth.Beta : null;
                var cr = growth?.CurrentRatio > 0 ? growth.CurrentRatio : null;

                var metrics = new List<LensMetricDto>
                {
                    new() { Label = "Debt to Equity", Value = de.HasValue ? $"{de.Value:N2}x" : "N/A", IsPositive = de.HasValue ? de.Value <= 0.5m : (bool?)null },
                    new() { Label = "Beta (Volatility)", Value = beta.HasValue ? $"{beta.Value:N2}" : "N/A", IsPositive = beta.HasValue ? beta.Value <= 0.5m : (bool?)null },
                    new() { Label = "Current Ratio", Value = cr.HasValue ? $"{cr.Value:N2}x" : "N/A", IsPositive = cr.HasValue ? cr.Value >= 1.2m : (bool?)null }
                };

                var insight = (de.HasValue || beta.HasValue || cr.HasValue)
                    ? GenerateDynamicRiskInsight(meta.Symbol, de ?? 0m, beta ?? 0m, cr ?? 0m)
                    : $"Risk ratios currently unavailable from data provider for {meta.Symbol}.";
                return (metrics, insight);
            }

            // Default: Growth Lens
            var sales = growth?.SalesGrowthYoY;
            var profit = growth?.ProfitGrowthYoY;
            var ret1y = growth?.OneYearReturn;

            static string FormatMetricValue(decimal? v) => !v.HasValue ? "N/A" : (v.Value == 0m ? "0.0%" : $"{(v.Value > 0 ? "+" : "")}{v.Value:N1}%");
            static bool? GetMetricIsPositive(decimal? v) => !v.HasValue ? null : (v.Value == 0m ? (bool?)null : v.Value > 0);

            var growthMetrics = new List<LensMetricDto>
            {
                new() { Label = "Sales Growth YoY", Value = FormatMetricValue(sales), IsPositive = GetMetricIsPositive(sales) },
                new() { Label = "Profit Growth YoY", Value = FormatMetricValue(profit), IsPositive = GetMetricIsPositive(profit) },
                new() { Label = "1-Year Return", Value = FormatMetricValue(ret1y), IsPositive = GetMetricIsPositive(ret1y) }
            };

            var growthInsight = (sales.HasValue || profit.HasValue || ret1y.HasValue)
                ? GenerateDynamicGrowthInsight(meta.Symbol, sales ?? 0m, profit ?? 0m, ret1y ?? 0m)
                : $"Growth metrics currently unavailable from data provider for {meta.Symbol}.";
            return (growthMetrics, growthInsight);
        }

        private static string GenerateDynamicCashFlowInsight(string symbol, decimal margin, string fcf, string ocf)
        {
            if (margin >= 25m)
            {
                return $"Superior operating margin of {margin:N1}% driving high cash conversion, with robust OCF {ocf}.";
            }
            if (margin >= 18m)
            {
                return $"Strong operational cash efficiency with {margin:N1}% operating margin and healthy free cash flow ({fcf}).";
            }
            if (margin >= 10m)
            {
                return $"Stable operating cash generation at {margin:N1}% margin supporting ongoing capex and debt servicing ({ocf}).";
            }
            return $"Operating margin at {margin:N1}% reflecting ongoing working capital requirements and seasonal cash cycles ({ocf}).";
        }

        private static string GenerateDynamicOwnershipInsight(string symbol, decimal promoter, decimal institutional, int instCount)
        {
            if (promoter >= 60m)
            {
                return $"Exceptional promoter commitment at {promoter:N1}% with {instCount} institutional funds holding {institutional:N1}%.";
            }
            if (institutional >= 45m)
            {
                return $"High institutional confidence with {institutional:N1}% held across {instCount} premier domestic & global funds.";
            }
            if (promoter >= 40m)
            {
                return $"Balanced ownership structure with {promoter:N1}% promoter stake and {institutional:N1}% institutional backing ({instCount} funds).";
            }
            return $"Institutional ownership of {institutional:N1}% across {instCount} institutional funds providing long-term governance support.";
        }

        private static string GenerateDynamicRiskInsight(string symbol, decimal de, decimal beta, decimal cr)
        {
            if (de <= 0.15m && beta <= 0.30m)
            {
                return $"Pristine low-risk balance sheet with minimal debt ({de:N2}x D/E) and low market volatility (Beta {beta:N2}).";
            }
            if (de <= 0.30m && beta <= 0.50m)
            {
                return $"Conservative capital structure with {de:N2}x D/E and stable liquidity buffer (Current Ratio {cr:N2}x).";
            }
            if (beta >= 1.0m)
            {
                return $"Cyclical sector sensitivity indicated by Beta {beta:N2}, balanced by prudent leverage control ({de:N2}x D/E).";
            }
            if (de >= 0.80m)
            {
                return $"Working capital leverage at {de:N2}x D/E monitored alongside healthy liquidity coverage (Current Ratio {cr:N2}x).";
            }
            return $"Moderate leverage profile with {de:N2}x D/E and steady market responsiveness (Beta {beta:N2}).";
        }

        private static string GenerateDynamicGrowthInsight(string symbol, decimal sales, decimal profit, decimal return1y)
        {
            var sStr = $"{(sales >= 0 ? "+" : "")}{sales:N1}%";
            var pStr = profit == 0m ? "0.0%" : $"{(profit > 0 ? "+" : "")}{profit:N1}%";

            // 1. Massive top-line surge (>= 50%)
            if (sales >= 50m)
            {
                if (profit > 0m)
                {
                    return $"Exceptional top-line surge of {sStr} YoY with {pStr} profit growth strengthening market dominance.";
                }
                return $"Rapid top-line delivery of {sStr} YoY sales, while near-term profit contracted by {pStr} amid expansion.";
            }

            // 2. High top-line with negative profit (Margin compression / reinvestment cycle)
            if (sales >= 15m && profit < 0m)
            {
                return $"Robust top-line expansion of {sStr} YoY, while margins contracted by {pStr} reflecting near-term operational cycle.";
            }

            // 3. Significant profit turnaround / operating leverage (Profit >= 30%)
            if (profit >= 30m)
            {
                return $"Significant operating leverage lifting net profit growth by {pStr} YoY alongside {sStr} revenue expansion.";
            }

            // 4. Solid double-digit momentum in both Sales & Profit (Sales >= 10% and Profit >= 10%)
            if (sales >= 10m && profit >= 10m)
            {
                return $"Solid double-digit momentum with {sStr} YoY sales and {pStr} profit expansion driving fundamental strength.";
            }

            // 5. Flat / Consolidating earnings (Profit around 0)
            if (Math.Abs(profit) <= 1.0m)
            {
                return $"Consolidation phase with steady revenue ({sStr} YoY) and flat profit margins ({pStr}) as client demand stabilizes.";
            }

            // 6. Moderate positive growth
            if (sales > 0m && profit > 0m)
            {
                return $"Balanced financial execution delivering {sStr} YoY sales growth and {pStr} profit expansion.";
            }

            // 7. General fallback
            return $"Tracking {sStr} YoY revenue and {pStr} net profit growth across key operating segments.";
        }

        private static string GenerateSvgSparklinePath(List<decimal> prices, int width, int height, int padding)
        {
            if (prices == null || prices.Count < 2)
            {
                return $"M 10 {height / 2} L {width - 10} {height / 2}";
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

            // Build smooth SVG path
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
