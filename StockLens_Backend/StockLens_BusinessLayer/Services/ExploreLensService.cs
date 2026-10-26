using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Helpers;
using StockLens_BusinessLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            decimal FallbackPrice,
            decimal FallbackChangePercent,
            decimal FallbackSalesGrowth,
            decimal FallbackProfitGrowth,
            decimal FallbackOneYearReturn,
            string FallbackFCF,
            decimal FallbackMargin,
            string FallbackOCF,
            decimal FallbackPromoter,
            decimal FallbackInstitutional,
            int FallbackInstCount,
            decimal FallbackDebtToEquity,
            decimal FallbackBeta,
            decimal FallbackCurrentRatio,
            string? Isin = null
        );

        private static readonly List<CompanyMeta> TrackedCompanies = new()
        {
            new("RELIANCE", "RELIANCE", "Reliance Industries Limited", "Energy & Conglomerate", 270, "#d4f77c", "#aee838", 1190.0m, 0.46m, 29.7m, -22.4m, -19.8m, "₹1.4L Cr", 12.3m, "₹1.8L Cr", 51.8m, 28.1m, 379, 0.46m, 0.19m, 1.10m, "INE002A01018"),
            new("TCS", "TCS", "Tata Consultancy Services", "Technology", 315, "#bee6fd", "#83cefc", 4120.0m, -0.6m, 11.2m, 15.0m, -18.0m, "₹394.6B", 24.0m, "₹529.5B", 71.8m, 17.6m, 306, 0.10m, 0.18m, 2.48m, "INE467B01029"),
            new("HDFCBANK", "HDFC", "HDFC Bank Limited", "Banking & Financials", 15, "#fed7aa", "#fca565", 1675.0m, 0.9m, 80.8m, 21.3m, -30.9m, "₹24,000 Cr", 38.5m, "₹45,000 Cr", 0.2m, 61.1m, 454, 0.80m, 0.41m, 1.28m, "INE040A01034"),
            new("INFY", "INFY", "Infosys Limited", "Technology", 55, "#d0cefd", "#9b97fb", 1153.8m, 0.04m, 1.4m, 0.0m, -23.3m, "$3.06B", 21.0m, "$3.95B", 16.0m, 51.3m, 345, 0.09m, 0.12m, 1.97m, "INE009A01021"),
            new("SUNPHARMA", "SUN", "Sun Pharmaceutical", "Healthcare & Pharma", 95, "#99f6e4", "#5eead4", 1740.0m, 2.4m, 10.5m, 27.4m, 3.2m, "₹18,500 Cr", 23.3m, "₹22,000 Cr", 55.6m, 29.1m, 300, 0.06m, 0.14m, 2.60m, "INE044A01036"),
            new("TATAMOTORS", "TATA", "Tata Motors Limited", "Automobile", 135, "#fecaca", "#fca5a5", 985.0m, 3.1m, 35.1m, 18.5m, 20.2m, "£2.5B", 7.3m, "₹179.1B", 45.2m, 25.8m, 206, 0.25m, 1.15m, 0.82m, "INE155A01022"),
            new("NUVOCO", "NUVOCO", "Nuvoco Vistas Corp", "Cement & Materials", 180, "#fed7aa", "#fca565", 342.0m, 0.4m, 13.5m, 77.5m, -22.2m, "₹15.6B", 6.4m, "₹14.3B", 72.0m, 19.8m, 46, 0.53m, 0.31m, 0.49m, "INE118D01016"),
            new("SENCO", "SENCO", "Senco Gold Limited", "Consumer Discretionary", 225, "#e9d5ff", "#d8b4fe", 1050.0m, -2.1m, 67.3m, -3.3m, -4.0m, "₹1,250 Cr", 6.3m, "₹1,800 Cr", 64.5m, 14.7m, 26, 1.07m, 0.38m, 1.80m, "INE602W01027")
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

            // 1. Fetch DB company logos if DbContext is available
            Dictionary<string, string?> dbLogos = new(StringComparer.OrdinalIgnoreCase);
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
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not query CompanyMaster table for ExploreLens logos");
                }
            }

            var companyDtos = new List<ExploreLensCompanyDto>();
            foreach (var (meta, quote, growth) in fetchedResults)
            {
                var price = quote != null && quote.Price > 0 ? quote.Price : meta.FallbackPrice;
                var change = quote?.Change ?? (price * (meta.FallbackChangePercent / 100m));
                var changePercent = quote?.ChangePercent ?? meta.FallbackChangePercent;
                var isPos = changePercent >= 0;

                var prices = (quote?.ClosingPrices != null && quote.ClosingPrices.Count >= 2)
                    ? quote.ClosingPrices
                    : GenerateFallbackPriceTrend(price, isPos);

                var trendPath = GenerateSvgSparklinePath(prices, 280, 70, 10);
                var firstPrice = prices.First();
                var lastPrice = prices.Last();
                var trendPos = lastPrice >= firstPrice;
                decimal sevenDayChangePercent = firstPrice > 0
                    ? Math.Round(((lastPrice - firstPrice) / firstPrice) * 100m, 1)
                    : 0m;
                string formattedSevenDayChange = $"{(sevenDayChangePercent >= 0 ? "+" : "")}{sevenDayChangePercent:N1}%";

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
                    FormattedPrice = $"₹{price:N0}",
                    Change = change,
                    ChangePercent = changePercent,
                    FormattedChange = $"{(changePercent >= 0 ? "+" : "")}{changePercent:N1}%",
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
                    var meta = TrackedCompanies.First(m => m.Symbol == c.Symbol);
                    var marginStr = c.Metrics.FirstOrDefault(m => m.Label == "Operating Margin")?.Value.Replace("%", "").Trim();
                    return decimal.TryParse(marginStr, out var mv) ? mv : meta.FallbackMargin;
                }).ToList();
            }
            else if (l.Contains("ownership"))
            {
                // Ownership: Rank by Strong Hands holding (Promoter + 0.9 * Institutional + 0.02 * InstCount)
                rankedCompanies = companyDtos.OrderByDescending(c =>
                {
                    var meta = TrackedCompanies.First(m => m.Symbol == c.Symbol);
                    var pStr = c.Metrics.FirstOrDefault(m => m.Label == "Promoter Holding")?.Value.Replace("%", "").Trim();
                    var iStr = c.Metrics.FirstOrDefault(m => m.Label == "Institutional Holding")?.Value.Replace("%", "").Trim();
                    var cStr = c.Metrics.FirstOrDefault(m => m.Label == "Institutions Count")?.Value.Replace("Funds", "").Trim();

                    var prom = decimal.TryParse(pStr, out var pv) ? pv : meta.FallbackPromoter;
                    var inst = decimal.TryParse(iStr, out var iv) ? iv : meta.FallbackInstitutional;
                    var cnt = int.TryParse(cStr, out var cv) ? cv : meta.FallbackInstCount;

                    return prom + (inst * 0.9m) + (cnt * 0.02m);
                }).ToList();
            }
            else if (l.Contains("risk"))
            {
                // Risk: LOWER RISK = BETTER / CLOSER TO CENTER (Score 5, Radius 95px)
                // Low Debt to Equity + Low Beta + Healthy Current Ratio
                rankedCompanies = companyDtos.OrderBy(c =>
                {
                    var meta = TrackedCompanies.First(m => m.Symbol == c.Symbol);
                    var deStr = c.Metrics.FirstOrDefault(m => m.Label == "Debt to Equity")?.Value.Replace("x", "").Trim();
                    var betaStr = c.Metrics.FirstOrDefault(m => m.Label.StartsWith("Beta"))?.Value.Trim();
                    var crStr = c.Metrics.FirstOrDefault(m => m.Label == "Current Ratio")?.Value.Replace("x", "").Trim();

                    var de = decimal.TryParse(deStr, out var dev) ? dev : meta.FallbackDebtToEquity;
                    var beta = decimal.TryParse(betaStr, out var bv) ? bv : meta.FallbackBeta;
                    var cr = decimal.TryParse(crStr, out var crv) ? crv : meta.FallbackCurrentRatio;

                    return (de * 50m) + (beta * 50m) - (cr * 10m);
                }).ToList();
            }
            else
            {
                // Growth: 40% Sales + 40% Profit + 20% 1-Year Return
                rankedCompanies = companyDtos.OrderByDescending(c =>
                {
                    var meta = TrackedCompanies.First(m => m.Symbol == c.Symbol);
                    var s = decimal.TryParse(c.Metrics.FirstOrDefault(m => m.Label == "Sales Growth YoY")?.Value.Replace("%", "").Replace("+", "").Trim(), out var sv) ? sv : meta.FallbackSalesGrowth;
                    var p = decimal.TryParse(c.Metrics.FirstOrDefault(m => m.Label == "Profit Growth YoY")?.Value.Replace("%", "").Replace("+", "").Trim(), out var pv) ? pv : meta.FallbackProfitGrowth;
                    var r = decimal.TryParse(c.Metrics.FirstOrDefault(m => m.Label == "1-Year Return")?.Value.Replace("%", "").Replace("+", "").Trim(), out var rv) ? rv : meta.FallbackOneYearReturn;

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
                var margin = growth?.OperatingMargin ?? meta.FallbackMargin;
                var fcf = !string.IsNullOrWhiteSpace(growth?.FreeCashFlowFormatted) ? growth.FreeCashFlowFormatted : meta.FallbackFCF;
                var ocf = !string.IsNullOrWhiteSpace(growth?.OperatingCashFlowFormatted) ? growth.OperatingCashFlowFormatted : meta.FallbackOCF;

                var metrics = new List<LensMetricDto>
                {
                    new() { Label = "Operating Margin", Value = $"{margin:N1}%", IsPositive = margin >= 15m },
                    new() { Label = "Free Cash Flow", Value = fcf, IsPositive = true },
                    new() { Label = "Operating Cash Flow", Value = ocf, IsPositive = true }
                };

                var insight = GenerateDynamicCashFlowInsight(meta.Symbol, margin, fcf, ocf);
                return (metrics, insight);
            }

            if (l.Contains("ownership"))
            {
                var promoter = (growth?.PromoterHolding > 0 ? growth.PromoterHolding : null) ?? meta.FallbackPromoter;
                var institutional = (growth?.InstitutionalHolding > 0 ? growth.InstitutionalHolding : null) ?? meta.FallbackInstitutional;
                var instCount = (growth?.InstitutionsCount > 0 ? growth.InstitutionsCount : null) ?? meta.FallbackInstCount;

                var metrics = new List<LensMetricDto>
                {
                    new() { Label = "Promoter Holding", Value = $"{promoter:N1}%", IsPositive = promoter >= 50m },
                    new() { Label = "Institutional Holding", Value = $"{institutional:N1}%", IsPositive = institutional >= 20m },
                    new() { Label = "Institutions Count", Value = $"{instCount} Funds", IsPositive = instCount >= 100 }
                };

                var insight = GenerateDynamicOwnershipInsight(meta.Symbol, promoter, institutional, instCount);
                return (metrics, insight);
            }

            if (l.Contains("risk"))
            {
                var de = (growth?.DebtToEquity > 0 ? growth.DebtToEquity : null) ?? meta.FallbackDebtToEquity;
                var beta = (growth?.Beta > 0 ? growth.Beta : null) ?? meta.FallbackBeta;
                var cr = (growth?.CurrentRatio > 0 ? growth.CurrentRatio : null) ?? meta.FallbackCurrentRatio;

                var metrics = new List<LensMetricDto>
                {
                    new() { Label = "Debt to Equity", Value = $"{de:N2}x", IsPositive = de <= 0.5m },
                    new() { Label = "Beta (Volatility)", Value = $"{beta:N2}", IsPositive = beta <= 0.5m },
                    new() { Label = "Current Ratio", Value = $"{cr:N2}x", IsPositive = cr >= 1.2m }
                };

                var insight = GenerateDynamicRiskInsight(meta.Symbol, de, beta, cr);
                return (metrics, insight);
            }

            // Default: Growth Lens
            var sales = growth?.SalesGrowthYoY ?? meta.FallbackSalesGrowth;
            var profit = growth?.ProfitGrowthYoY ?? meta.FallbackProfitGrowth;
            var ret1y = growth?.OneYearReturn ?? meta.FallbackOneYearReturn;

            static string FormatMetricValue(decimal v) => v == 0m ? "0.0%" : $"{(v > 0 ? "+" : "")}{v:N1}%";
            static bool? GetMetricIsPositive(decimal v) => v == 0m ? (bool?)null : v > 0;

            var growthMetrics = new List<LensMetricDto>
            {
                new() { Label = "Sales Growth YoY", Value = FormatMetricValue(sales), IsPositive = GetMetricIsPositive(sales) },
                new() { Label = "Profit Growth YoY", Value = FormatMetricValue(profit), IsPositive = GetMetricIsPositive(profit) },
                new() { Label = "1-Year Return", Value = FormatMetricValue(ret1y), IsPositive = GetMetricIsPositive(ret1y) }
            };

            var growthInsight = GenerateDynamicGrowthInsight(meta.Symbol, sales, profit, ret1y);
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

        private static List<decimal> GenerateFallbackPriceTrend(decimal basePrice, bool isPositive)
        {
            var delta = isPositive ? 1.015m : 0.985m;
            return new List<decimal>
            {
                Math.Round(basePrice * 0.98m, 2),
                Math.Round(basePrice * 0.99m, 2),
                Math.Round(basePrice * 0.985m, 2),
                Math.Round(basePrice * 1.005m, 2),
                Math.Round(basePrice * 1.01m, 2),
                Math.Round(basePrice * 1.002m, 2),
                basePrice
            };
        }
    }
}
