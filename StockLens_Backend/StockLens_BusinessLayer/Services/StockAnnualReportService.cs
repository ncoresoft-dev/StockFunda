using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.AnnualReportInsights;
using StockLens_Infrastructure.ExternalServices.Screener;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockAnnualReportService : IStockAnnualReportService
    {
        private readonly IScreenerClient _screenerClient;
        private readonly IStockRepository _stockRepository;
        private readonly IStockAnnualReportRepository _annualReportRepository;
        private readonly IAnnualReportInsightExtractor _insightExtractor;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<StockAnnualReportService> _logger;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(7);

        public StockAnnualReportService(
            IScreenerClient screenerClient,
            IStockRepository stockRepository,
            IStockAnnualReportRepository annualReportRepository,
            IAnnualReportInsightExtractor insightExtractor,
            IMemoryCache memoryCache,
            ILogger<StockAnnualReportService> logger)
        {
            _screenerClient = screenerClient;
            _stockRepository = stockRepository;
            _annualReportRepository = annualReportRepository;
            _insightExtractor = insightExtractor;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task<StockAnnualReportsResponseDto> GetAnnualReportsBySymbolAsync(
            string symbol,
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cacheKey = $"screener_current_annual_report_{cleanSymbol}";

            // 1. Check in-memory cache first
            if (!refresh && _memoryCache.TryGetValue(cacheKey, out StockAnnualReportsResponseDto? cachedResponse) 
                && cachedResponse != null)
            {
                _logger.LogInformation("Returning cached annual report for symbol: {Symbol}", cleanSymbol);
                return cachedResponse;
            }

            // Look up stock in database
            var stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, "NSE", null, null, cancellationToken);
            var companyName = stock?.Company?.CompanyName;

            // 2. Check Database (No automatic background scraping or automatic extraction!)
            if (stock != null)
            {
                var dbReport = await _annualReportRepository.GetLatestByStockIdAsync(stock.Id, cancellationToken);
                if (dbReport != null)
                {
                    // Case A: Insights already extracted and completed in DB
                    if (!string.IsNullOrWhiteSpace(dbReport.ExtractedInsightsJson) 
                        && dbReport.ExtractionStatus == "Completed"
                        && !IsFailedOrFallbackInsights(dbReport.ExtractedInsightsJson))
                    {
                        _logger.LogInformation("Returning pre-extracted annual report insights from Database for symbol: {Symbol}", cleanSymbol);
                        var responseFromDb = BuildResponseDto(stock.Id, cleanSymbol, companyName, dbReport);
                        _memoryCache.Set(cacheKey, responseFromDb, CacheDuration);
                        return responseFromDb;
                    }

                    // Case B: Record exists in DB (e.g. Requested, Pending, or NotAvailable) - return status without calling Gemini!
                    var pendingResponse = BuildResponseDto(stock.Id, cleanSymbol, companyName, dbReport);
                    return pendingResponse;
                }
            }

            // 3. If not in DB: Return NotAvailable. (Do NOT auto-scrape Screener and do NOT auto-save to DB!)
            _logger.LogInformation("No annual report record in DB for symbol: {Symbol}. Awaiting user request or admin entry.", cleanSymbol);
            return new StockAnnualReportsResponseDto
            {
                StockId = stock?.Id,
                Symbol = cleanSymbol,
                CompanyName = companyName,
                TotalCount = 0,
                Reports = Array.Empty<AnnualReportDto>(),
                ExtractionStatus = "NotAvailable",
                Message = "Annual report insights have not been generated yet for this company."
            };
        }

        public async Task<StockAnnualReportsResponseDto> RequestAnnualReportAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, "NSE", null, null, cancellationToken);
            if (stock == null)
            {
                throw new InvalidOperationException($"Unable to resolve stock for symbol: {cleanSymbol}");
            }
            var companyName = stock.Company?.CompanyName;

            var dbReport = await _annualReportRepository.GetLatestByStockIdAsync(stock.Id, cancellationToken);
            if (dbReport != null)
            {
                if (dbReport.ExtractionStatus != "Completed")
                {
                    dbReport.ExtractionStatus = "Requested";
                    dbReport.UpdatedAt = DateTime.UtcNow;
                    await _annualReportRepository.UpdateAsync(dbReport, cancellationToken);
                }
            }
            else
            {
                dbReport = new StockAnnualReport
                {
                    StockId = stock.Id,
                    Title = $"{cleanSymbol} Annual Report",
                    ExtractionStatus = "Requested",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow
                };
                await _annualReportRepository.AddAsync(dbReport, cancellationToken);
            }

            var cacheKey = $"screener_current_annual_report_{cleanSymbol}";
            _memoryCache.Remove(cacheKey);

            var response = BuildResponseDto(stock.Id, cleanSymbol, companyName, dbReport);
            response.Message = "Annual report requested successfully. Admin has been notified.";
            return response;
        }

        public async Task<string?> SuggestScreenerPdfUrlAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            try
            {
                var docs = await _screenerClient.GetAnnualReportsAsync(cleanSymbol, cancellationToken);
                var latest = docs.OrderByDescending(d => d.Year).FirstOrDefault();
                return latest?.Url;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch Screener PDF URL suggestion for {Symbol}", cleanSymbol);
                return null;
            }
        }

        public async Task<StockAnnualReportsResponseDto> AdminSaveAndExtractAnnualReportAsync(
            AdminSaveAndExtractReportRequestDto request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request?.Symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(request.Symbol));
            }

            if (string.IsNullOrWhiteSpace(request.PdfUrl))
            {
                throw new ArgumentException("PDF URL is required for extraction.", nameof(request.PdfUrl));
            }

            var cleanSymbol = request.Symbol.Trim().ToUpperInvariant();
            var cacheKey = $"screener_current_annual_report_{cleanSymbol}";

            var stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, "NSE", null, null, cancellationToken);
            if (stock == null)
            {
                throw new InvalidOperationException($"Unable to resolve stock for symbol: {cleanSymbol}");
            }
            var companyName = stock.Company?.CompanyName;

            var dbReport = await _annualReportRepository.GetLatestByStockIdAsync(stock.Id, cancellationToken);
            if (dbReport == null)
            {
                dbReport = new StockAnnualReport
                {
                    StockId = stock.Id,
                    Title = string.IsNullOrWhiteSpace(request.Title) ? $"{cleanSymbol} Annual Report {request.Year}".Trim() : request.Title.Trim(),
                    Year = request.Year?.Trim(),
                    PdfUrl = request.PdfUrl.Trim(),
                    Source = string.IsNullOrWhiteSpace(request.Source) ? "BSE" : request.Source.Trim(),
                    ExtractionStatus = "Processing",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow
                };
                await _annualReportRepository.AddAsync(dbReport, cancellationToken);
            }
            else
            {
                dbReport.Title = string.IsNullOrWhiteSpace(request.Title) ? dbReport.Title : request.Title.Trim();
                dbReport.Year = string.IsNullOrWhiteSpace(request.Year) ? dbReport.Year : request.Year.Trim();
                dbReport.PdfUrl = request.PdfUrl.Trim();
                dbReport.Source = string.IsNullOrWhiteSpace(request.Source) ? (dbReport.Source ?? "BSE") : request.Source.Trim();
                dbReport.ExtractionStatus = "Processing";
                dbReport.UpdatedAt = DateTime.UtcNow;
                await _annualReportRepository.UpdateAsync(dbReport, cancellationToken);
            }

            _logger.LogInformation("Admin triggered AI extraction for {Symbol} from URL: {PdfUrl}", cleanSymbol, request.PdfUrl);

            try
            {
                var insightsJson = await _insightExtractor.ExtractInsightsJsonAsync(cleanSymbol, dbReport.PdfUrl, dbReport.Year, cancellationToken);
                if (!string.IsNullOrWhiteSpace(insightsJson))
                {
                    var isFailed = IsFailedOrFallbackInsights(insightsJson);
                    dbReport.ExtractedInsightsJson = insightsJson;
                    dbReport.ExtractionStatus = isFailed ? "Failed" : "Completed";
                    dbReport.ProcessedAt = DateTime.UtcNow;
                    dbReport.UpdatedAt = DateTime.UtcNow;
                    await _annualReportRepository.UpdateAsync(dbReport, cancellationToken);
                }
                else
                {
                    dbReport.ExtractionStatus = "Failed";
                    dbReport.UpdatedAt = DateTime.UtcNow;
                    await _annualReportRepository.UpdateAsync(dbReport, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract insights via Gemini for symbol {Symbol}", cleanSymbol);
                dbReport.ExtractionStatus = "Failed";
                dbReport.UpdatedAt = DateTime.UtcNow;
                await _annualReportRepository.UpdateAsync(dbReport, cancellationToken);
                throw;
            }

            var response = BuildResponseDto(stock.Id, cleanSymbol, companyName, dbReport);
            if (dbReport.ExtractionStatus == "Completed")
            {
                _memoryCache.Set(cacheKey, response, CacheDuration);
            }
            else
            {
                _memoryCache.Remove(cacheKey);
            }
            return response;
        }

        public async Task<IReadOnlyList<AnnualReportDirectoryItemDto>> GetAnnualReportsDirectoryAsync(
            string? search = null,
            string? status = null,
            CancellationToken cancellationToken = default)
        {
            var allReports = await _annualReportRepository.GetAllReportsAsync(cancellationToken);
            var query = allReports.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var cleanSearch = search.Trim();
                query = query.Where(r => 
                    (r.Stock?.Symbol != null && r.Stock.Symbol.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase)) ||
                    (r.Stock?.Company?.CompanyName != null && r.Stock.Company.CompanyName.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => 
                {
                    var isFailed = IsFailedOrFallbackInsights(r.ExtractedInsightsJson);
                    var effective = (isFailed && r.ExtractionStatus == "Completed") ? "Failed" : r.ExtractionStatus;
                    return string.Equals(effective, status.Trim(), StringComparison.OrdinalIgnoreCase);
                });
            }

            return query.Select(r => 
            {
                var isFailed = IsFailedOrFallbackInsights(r.ExtractedInsightsJson);
                var effective = (isFailed && r.ExtractionStatus == "Completed") ? "Failed" : r.ExtractionStatus;

                return new AnnualReportDirectoryItemDto
                {
                    StockId = r.StockId,
                    Symbol = r.Stock?.Symbol ?? string.Empty,
                    CompanyName = r.Stock?.Company?.CompanyName ?? r.Stock?.Symbol ?? string.Empty,
                    LogoUrl = r.Stock?.Company?.LogoUrl,
                    Industry = r.Stock?.Company?.Industry,
                    Sector = r.Stock?.Company?.Sector,
                    Year = r.Year,
                    Title = r.Title,
                    PdfUrl = r.PdfUrl,
                    Source = r.Source,
                    ExtractionStatus = effective,
                    CreatedAt = r.CreatedAt,
                    ProcessedAt = r.ProcessedAt
                };
            })
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        }

        public async Task<IReadOnlyList<AnnualReportDirectoryItemDto>> GetRequestedReportsAsync(
            CancellationToken cancellationToken = default)
        {
            var requested = await _annualReportRepository.GetRequestedReportsAsync(cancellationToken);
            return requested.Select(r => new AnnualReportDirectoryItemDto
            {
                StockId = r.StockId,
                Symbol = r.Stock?.Symbol ?? string.Empty,
                CompanyName = r.Stock?.Company?.CompanyName ?? r.Stock?.Symbol ?? string.Empty,
                LogoUrl = r.Stock?.Company?.LogoUrl,
                Industry = r.Stock?.Company?.Industry,
                Sector = r.Stock?.Company?.Sector,
                Year = r.Year,
                Title = r.Title,
                PdfUrl = r.PdfUrl,
                Source = r.Source,
                ExtractionStatus = r.ExtractionStatus,
                CreatedAt = r.CreatedAt,
                ProcessedAt = r.ProcessedAt
            })
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        }

        private static StockAnnualReportsResponseDto BuildResponseDto(
            int? stockId,
            string symbol,
            string? companyName,
            StockAnnualReport dbReport)
        {
            var isFailedOrFallback = IsFailedOrFallbackInsights(dbReport.ExtractedInsightsJson);
            var parsedInsights = isFailedOrFallback ? null : DeserializeInsights(dbReport.ExtractedInsightsJson, dbReport.Year);
            var effectiveStatus = (isFailedOrFallback && dbReport.ExtractionStatus == "Completed") ? "Failed" : dbReport.ExtractionStatus;

            return new StockAnnualReportsResponseDto
            {
                StockId = stockId ?? dbReport.StockId,
                Symbol = symbol,
                CompanyName = companyName,
                TotalCount = 1,
                Reports = new List<AnnualReportDto>
                {
                    new AnnualReportDto
                    {
                        Title = dbReport.Title,
                        Year = dbReport.Year,
                        PdfUrl = dbReport.PdfUrl,
                        Source = dbReport.Source ?? "BSE"
                    }
                },
                ExtractionStatus = effectiveStatus,
                Insights = parsedInsights,
                ProcessedAt = dbReport.ProcessedAt,
                Message = effectiveStatus == "NotAvailable" ? "Annual report not available." : (isFailedOrFallback ? "Annual report filing retrieved. Insights extraction needs to be run." : null)
            };
        }

        private static AnnualReportInsightsDto? DeserializeInsights(string? json, string? reportYear = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var insights = JsonSerializer.Deserialize<AnnualReportInsightsDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (insights == null)
                    return null;

                ProcessAndPrioritizeInsights(insights, reportYear);
                return insights;
            }
            catch
            {
                return null;
            }
        }

        public static void ProcessAndPrioritizeInsights(AnnualReportInsightsDto insights, string? reportYear = null)
        {
            if (insights == null) return;

            // 1. Assign standard categories & display groups
            ConfigureCategoryItems(insights.CompanyExpansion, "Company Expansion", "Expansion & Capacity");
            ConfigureCategoryItems(insights.CapacityExpansion, "Capacity Expansion", "Expansion & Capacity");
            ConfigureCategoryItems(insights.MarketExpansion, "Market Expansion", "Expansion & Capacity");
            ConfigureCategoryItems(insights.MajorOrdersAndContracts, "Major Orders & Contracts", "Major Deals & Partnerships");
            ConfigureCategoryItems(insights.StrategicPartnerships, "Strategic Partnerships", "Major Deals & Partnerships");
            ConfigureCategoryItems(insights.GovernmentBenefits, "Government Benefits", "Government Benefits");
            ConfigureCategoryItems(insights.GovernmentProjectsAndPolicies, "Govt Projects & Policies", "Government Benefits");
            ConfigureCategoryItems(insights.NewBusinessAndProducts, "New Business & Products", "New Business & Acquisitions");
            ConfigureCategoryItems(insights.AcquisitionsAndInvestments, "Acquisitions & Investments", "New Business & Acquisitions");
            ConfigureCategoryItems(insights.FutureGrowthPlans, "Future Growth Plans", "Future Growth");
            ConfigureCategoryItems(insights.ManagementAndWorkforce, "Management & Workforce", "Management & Risks");
            ConfigureCategoryItems(insights.MajorBusinessRisks, "Major Business Risks", "Management & Risks");

            // 2. Validate, filter low-value boilerplate, and normalize status
            insights.CompanyExpansion = FilterAndCleanItems(insights.CompanyExpansion);
            insights.CapacityExpansion = FilterAndCleanItems(insights.CapacityExpansion);
            insights.MarketExpansion = FilterAndCleanItems(insights.MarketExpansion);
            insights.MajorOrdersAndContracts = FilterAndCleanItems(insights.MajorOrdersAndContracts);
            insights.StrategicPartnerships = FilterAndCleanItems(insights.StrategicPartnerships);
            insights.GovernmentBenefits = FilterAndCleanItems(insights.GovernmentBenefits);
            insights.GovernmentProjectsAndPolicies = FilterAndCleanItems(insights.GovernmentProjectsAndPolicies);
            insights.NewBusinessAndProducts = FilterAndCleanItems(insights.NewBusinessAndProducts);
            insights.AcquisitionsAndInvestments = FilterAndCleanItems(insights.AcquisitionsAndInvestments);
            insights.FutureGrowthPlans = FilterAndCleanItems(insights.FutureGrowthPlans);
            insights.ManagementAndWorkforce = FilterAndCleanItems(insights.ManagementAndWorkforce);
            insights.MajorBusinessRisks = FilterAndCleanItems(insights.MajorBusinessRisks);

            // 3. Cross-category deduplication & merging
            var allCategories = new List<List<AnnualReportInsightItemDto>>
            {
                insights.CompanyExpansion,
                insights.CapacityExpansion,
                insights.MajorOrdersAndContracts,
                insights.GovernmentBenefits,
                insights.StrategicPartnerships,
                insights.NewBusinessAndProducts,
                insights.AcquisitionsAndInvestments,
                insights.MarketExpansion,
                insights.ManagementAndWorkforce,
                insights.FutureGrowthPlans,
                insights.GovernmentProjectsAndPolicies,
                insights.MajorBusinessRisks
            };

            DeduplicateAcrossCategories(allCategories);

            var resolvedFy = AnnualReportInsightExtractor.ResolveFiscalYear(reportYear);

            // 4. Calculate Priority Score for all items based on material business significance and normalize reporting period
            foreach (var list in allCategories)
            {
                foreach (var item in list)
                {
                    if (!string.IsNullOrWhiteSpace(resolvedFy))
                    {
                        // If reporting period was hallucinated/copied as "FY24" from legacy prompt template while report is e.g. FY26, or if empty:
                        if (string.IsNullOrWhiteSpace(item.ReportingPeriod) ||
                            (item.ReportingPeriod.Equals("FY24", StringComparison.OrdinalIgnoreCase) && !resolvedFy.Equals("FY24", StringComparison.OrdinalIgnoreCase)))
                        {
                            item.ReportingPeriod = resolvedFy;
                        }
                    }

                    item.PriorityScore = ComputePriorityScore(item);
                }
                list.Sort((a, b) => b.PriorityScore.CompareTo(a.PriorityScore));
            }

            // 5. Select top 5-6 high-impact insights ensuring diversity across display groups
            var allItems = allCategories
                .SelectMany(l => l)
                .OrderByDescending(i => i.PriorityScore)
                .ToList();

            var topList = new List<AnnualReportInsightItemDto>();
            var groupCounts = new Dictionary<string, int>();

            foreach (var item in allItems)
            {
                if (topList.Count >= 6) break;

                var group = item.DisplayGroup ?? "Other";
                var currentCount = groupCounts.GetValueOrDefault(group, 0);

                // Allow max 2 from the same display group in top 6 to guarantee diverse overview
                if (currentCount < 2 || topList.Count + (allItems.Count - topList.Count) <= 6)
                {
                    topList.Add(item);
                    groupCounts[group] = currentCount + 1;
                }
            }

            // If we still have fewer than 6, fill remaining by highest priority score
            if (topList.Count < 6)
            {
                foreach (var item in allItems)
                {
                    if (topList.Count >= 6) break;
                    if (!topList.Contains(item))
                    {
                        topList.Add(item);
                    }
                }
            }

            insights.TopHighImpactInsights = topList;
        }

        private static void ConfigureCategoryItems(List<AnnualReportInsightItemDto> items, string defaultCategory, string defaultDisplayGroup)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Category))
                    item.Category = defaultCategory;
                if (string.IsNullOrWhiteSpace(item.DisplayGroup))
                    item.DisplayGroup = defaultDisplayGroup;
            }
        }

        private static List<AnnualReportInsightItemDto> FilterAndCleanItems(List<AnnualReportInsightItemDto> items)
        {
            if (items == null) return new List<AnnualReportInsightItemDto>();

            var cleaned = new List<AnnualReportInsightItemDto>();
            var genericTerms = new[]
            {
                "available in the official annual report",
                "details are disclosed in",
                "refer to annual report",
                "details available in full report",
                "expansion details available",
                "please review original document",
                "general industry commentary"
            };

            foreach (var item in items)
            {
                if (item == null) continue;

                var text = (item.Summary ?? string.Empty).Trim();
                if (text.Length < 15) continue;

                var isGeneric = false;
                foreach (var gt in genericTerms)
                {
                    if (text.Contains(gt, StringComparison.OrdinalIgnoreCase))
                    {
                        isGeneric = true;
                        break;
                    }
                }
                if (isGeneric) continue;

                // Normalize Status
                item.Status = NormalizeStatus(item.Status);

                // Ensure clean Title
                if (string.IsNullOrWhiteSpace(item.Title))
                {
                    item.Title = text.Length > 70 ? text[..70].Trim() + "..." : text;
                }

                cleaned.Add(item);
            }

            return cleaned;
        }

        private static string? NormalizeStatus(string? rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus)) return null;

            var lower = rawStatus.Trim().ToLowerInvariant();
            if (lower.Contains("completed") || lower.Contains("commissioned") || lower.Contains("executed"))
                return "Completed";
            if (lower.Contains("ongoing") || lower.Contains("in progress") || lower.Contains("underway") || lower.Contains("operational"))
                return "Ongoing";
            if (lower.Contains("approved") || lower.Contains("awarded") || lower.Contains("granted") || lower.Contains("eligible"))
                return "Approved";
            if (lower.Contains("planned") || lower.Contains("proposed") || lower.Contains("targeted") || lower.Contains("target") || lower.Contains("upcoming"))
                return "Planned";

            return char.ToUpperInvariant(rawStatus[0]) + rawStatus[1..].ToLowerInvariant();
        }

        private static void DeduplicateAcrossCategories(List<List<AnnualReportInsightItemDto>> categories)
        {
            var seenSignatures = new List<(string Key, AnnualReportInsightItemDto Item)>();

            foreach (var list in categories)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var item = list[i];
                    var sig = GenerateSignature(item);

                    var existing = seenSignatures.FirstOrDefault(s => AreSignaturesSimilar(s.Key, sig));
                    if (existing.Item != null)
                    {
                        // Merge missing details into existing item if available
                        MergeInsightDetails(existing.Item, item);
                        list.RemoveAt(i);
                    }
                    else
                    {
                        seenSignatures.Add((sig, item));
                    }
                }
            }
        }

        private static string GenerateSignature(AnnualReportInsightItemDto item)
        {
            var combined = $"{item.Title} {item.Summary}".ToLowerInvariant();
            var words = System.Text.RegularExpressions.Regex.Split(combined, @"\W+")
                .Where(w => w.Length > 3)
                .Distinct()
                .OrderBy(w => w);
            return string.Join(" ", words);
        }

        private static bool AreSignaturesSimilar(string sig1, string sig2)
        {
            if (sig1 == sig2) return true;
            var words1 = new HashSet<string>(sig1.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var words2 = new HashSet<string>(sig2.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (words1.Count == 0 || words2.Count == 0) return false;

            var common = words1.Intersect(words2).Count();
            var minCount = Math.Min(words1.Count, words2.Count);

            return (double)common / minCount >= 0.75;
        }

        private static void MergeInsightDetails(AnnualReportInsightItemDto target, AnnualReportInsightItemDto source)
        {
            if (!target.FinancialValue.HasValue && source.FinancialValue.HasValue)
            {
                target.FinancialValue = source.FinancialValue;
                target.FinancialValueUnit = source.FinancialValueUnit;
            }
            if (string.IsNullOrWhiteSpace(target.ExpectedDate) && !string.IsNullOrWhiteSpace(source.ExpectedDate))
                target.ExpectedDate = source.ExpectedDate;
            if (string.IsNullOrWhiteSpace(target.ExpectedBenefit) && !string.IsNullOrWhiteSpace(source.ExpectedBenefit))
                target.ExpectedBenefit = source.ExpectedBenefit;
            if (!target.SourcePage.HasValue && source.SourcePage.HasValue)
                target.SourcePage = source.SourcePage;
            if (string.IsNullOrWhiteSpace(target.SourceText) && !string.IsNullOrWhiteSpace(source.SourceText))
                target.SourceText = source.SourceText;
        }

        private static int ComputePriorityScore(AnnualReportInsightItemDto item)
        {
            // Base priority score aligned with Section 6 Prioritization Rules:
            // 1. Large material expansion projects (base 80)
            // 2. Significant contracts & confirmed orders (base 78)
            // 3. Approved government benefits & subsidies (base 75)
            // 4. Strategic partnerships with defined business objectives (base 72)
            // 5. Acquisitions & new market entry (base 70)
            // 6. Major future growth targets and investment plans (base 68)
            // 7. Significant risks materially affecting growth (base 65)

            int score = item.Category switch
            {
                "Company Expansion" => 80,
                "Capacity Expansion" => 80,
                "Major Orders & Contracts" => 78,
                "Government Benefits" => 75,
                "Govt Projects & Policies" => 73,
                "Strategic Partnerships" => 72,
                "Acquisitions & Investments" => 71,
                "New Business & Products" => 69,
                "Future Growth Plans" => 68,
                "Market Expansion" => 67,
                "Major Business Risks" => 65,
                "Management & Workforce" => 52, // Routine workforce changes deprioritized
                _ => 60
            };

            // Material financial impact boost
            if (item.FinancialValue.HasValue && item.FinancialValue.Value > 0)
            {
                if (item.FinancialValue.Value >= 1000) score += 15;
                else if (item.FinancialValue.Value >= 100) score += 10;
                else score += 5;
            }

            // Status boost: Confirmed / Approved / Ongoing projects rank above speculative plans
            if (item.Status == "Approved") score += 8;
            else if (item.Status == "Ongoing") score += 6;
            else if (item.Status == "Completed") score += 5;

            // Concrete timeline boost
            if (!string.IsNullOrWhiteSpace(item.ExpectedDate)) score += 5;

            // Source traceability boost (verified backing in PDF)
            if (item.SourcePage.HasValue) score += 3;

            // Special keyword boosts:
            var text = (item.Title + " " + item.Summary).ToLowerInvariant();
            if (text.Contains("gigafactory") || text.Contains("green hydrogen") || text.Contains("5g") || text.Contains("pli scheme"))
                score += 5;

            return Math.Min(100, Math.Max(10, score));
        }

        public static bool IsFailedOrFallbackInsights(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;
            return json.Contains("encountered a temporary issue", StringComparison.OrdinalIgnoreCase)
                || json.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                || json.Contains("Could not download the filing", StringComparison.OrdinalIgnoreCase)
                || json.Contains("Configure 'Gemini:ApiKey'", StringComparison.OrdinalIgnoreCase)
                || json.Contains("AI insights generation returned status", StringComparison.OrdinalIgnoreCase);
        }
    }
}
