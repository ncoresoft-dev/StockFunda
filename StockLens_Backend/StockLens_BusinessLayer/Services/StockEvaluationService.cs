using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Enums;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.Models;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.BharatStock;

namespace StockLens_BusinessLayer.Services
{
    public class StockEvaluationService : IStockEvaluationService
    {
        private readonly IStockCashflowService _cashflowService;
        private readonly IStockQuarterlyResultsService _quartersService;
        private readonly IStockShareholdingService _shareholdingService;
        private readonly IStockBalanceSheetService _balanceSheetService;
        private readonly IStockPriceHistoryService _priceHistoryService;
        private readonly IFinancialProvider _financialProvider;
        private readonly ICompanyRepository _companyRepository;

        public StockEvaluationService(
            IStockCashflowService cashflowService,
            IStockQuarterlyResultsService quartersService,
            IStockShareholdingService shareholdingService,
            IStockBalanceSheetService balanceSheetService,
            IStockPriceHistoryService priceHistoryService,
            IFinancialProvider financialProvider,
            ICompanyRepository companyRepository)
        {
            _cashflowService = cashflowService;
            _quartersService = quartersService;
            _shareholdingService = shareholdingService;
            _balanceSheetService = balanceSheetService;
            _priceHistoryService = priceHistoryService;
            _financialProvider = financialProvider;
            _companyRepository = companyRepository;
        }

        private static class MetricNames
        {
            public const string CapitalCompounding = "Capital Compounding (ROE/ROCE)";
            public const string CashFlowGeneration = "Cash Flow Generation (FCF/CFO)";
            public const string CashConversionQuality = "Cash Conversion Quality (CFO/OP)";
            public const string NetCashFlow = "Net Cash Flow";
            public const string ValuationMultiple = "Asset & Growth Valuation Multiples (PEG/PB/PE)";
            public const string RelativeValuation = "Relative Valuation Multiple (PE vs Sector PE)";
            public const string BookValueSolvency = "Net Worth Solvency (Book Value)";
            public const string DebtCoverage = "Debt Burden & Interest Coverage";
            public const string BorrowingsTrend = "Borrowings Trend (Balance Sheet YoY)";
            public const string SalesGrowthYoY = "Quarterly Sales Growth YoY";
            public const string ProfitGrowthYoY = "Quarterly Net Profit Growth YoY";
            public const string Dma50 = "50 DMA Technical Trend";
            public const string Dma200 = "200 DMA Technical Trend";
            public const string Price52WeekRange = "52-Week Price Range Proximity";
            public const string PromoterHolding = "Promoter Stake & Board Governance";
            public const string InstitutionalTrend = "Institutional Ownership Trend (FII+DII)";
            public const string CapexExpansion = "Fixed Asset & Capex Growth";
            public const string DebtorDays = "Debtor Days (Receivables Turnover)";
            public const string InventoryDays = "Inventory Days (Holding Period)";
            public const string PayableDaysMoat = "Payable Days Float Moat";
        }

        private class MetricsTracker
        {
            public HashSet<string> VerifiedMetrics { get; } = new(StringComparer.OrdinalIgnoreCase);

            public void Record(string metricName)
            {
                VerifiedMetrics.Add(metricName);
            }

            public bool IsVerified(string metricName) => VerifiedMetrics.Contains(metricName);

            public int ValidCount => VerifiedMetrics.Count;
        }

        private static (List<string> Applicable, List<string> Inapplicable) GetArchetypeMetricRules(SectorArchetype archetype, bool hasInventoryData = true)
        {
            var all20 = new List<string>
            {
                MetricNames.CapitalCompounding,
                MetricNames.CashFlowGeneration,
                MetricNames.CashConversionQuality,
                MetricNames.NetCashFlow,
                MetricNames.ValuationMultiple,
                MetricNames.RelativeValuation,
                MetricNames.BookValueSolvency,
                MetricNames.DebtCoverage,
                MetricNames.BorrowingsTrend,
                MetricNames.SalesGrowthYoY,
                MetricNames.ProfitGrowthYoY,
                MetricNames.Dma50,
                MetricNames.Dma200,
                MetricNames.Price52WeekRange,
                MetricNames.PromoterHolding,
                MetricNames.InstitutionalTrend,
                MetricNames.CapexExpansion,
                MetricNames.DebtorDays,
                MetricNames.InventoryDays,
                MetricNames.PayableDaysMoat
            };

            if (archetype == SectorArchetype.FinancialServices)
            {
                // Financial Services (17 applicable): Debtor days, Inventory days, Payable float moat are structurally inapplicable
                var inapplicable = new List<string>
                {
                    MetricNames.DebtorDays,
                    MetricNames.InventoryDays,
                    MetricNames.PayableDaysMoat
                };
                var applicable = all20.Except(inapplicable).ToList(); // Count = 17
                return (applicable, inapplicable);
            }

            if (archetype == SectorArchetype.InformationTechnology ||
                archetype == SectorArchetype.TelecomAndMedia ||
                !hasInventoryData)
            {
                // Asset-light / services: Inventory days and Payable float moat are structurally inapplicable
                var inapplicable = new List<string>
                {
                    MetricNames.InventoryDays,
                    MetricNames.PayableDaysMoat
                };
                var applicable = all20.Except(inapplicable).ToList(); // Count = 18
                return (applicable, inapplicable);
            }

            // Asset-heavy archetypes: Manufacturing, Consumer, Pharma, Utilities, RealEstate (20 applicable)
            return (all20, new List<string>());
        }

        /// <summary>
        /// Evaluates a stock's health across 6 fundamental and technical categories.
        /// 
        /// Data Provider Limitations (Documented per audit):
        /// 1. Promoter Pledge %: Neither IndianAPI nor BharatStock feeds provide promoter pledge / encumbered shares.
        ///    To maintain integrity, this metric is omitted from scoring rather than fabricated with artificial defaults.
        /// 2. Historical 5-Year P/E Percentile Bands: Trailing 5-year valuation percentiles are unavailable from upstream feeds
        ///    (only scalar PeRatio and SectorPe are supplied). Relative valuation relies strictly on verified P/E vs Sector P/E.
        /// </summary>
        public async Task<StockHealthScoreDto> EvaluateStockAsync(
            string symbol,
            string? exchange = "NSE",
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            var cleanSymbol = (symbol ?? string.Empty).Trim().ToUpper();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpper();

            if (string.IsNullOrEmpty(cleanSymbol))
            {
                return new StockHealthScoreDto
                {
                    Symbol = string.Empty,
                    Exchange = cleanExchange,
                    EvaluatedAt = DateTime.UtcNow,
                    Signal = "Inconclusive",
                    SignalClass = "badge-warning",
                    SummaryText = "Invalid or empty symbol provided.",
                    DataConfidence = "Inconclusive"
                };
            }

            var companyData = await FetchSafeAsync(() => _companyRepository.GetCompanyBySymbolAsync(cleanSymbol));
            var cashflowData = await FetchSafeAsync(() => _cashflowService.GetCashflowBySymbolAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var quartersData = await FetchSafeAsync(() => _quartersService.GetQuarterlyResultsBySymbolAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var shareholdingData = await FetchSafeAsync(() => _shareholdingService.GetShareholdingBySymbolAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var balanceSheetData = await FetchSafeAsync(() => _balanceSheetService.GetBalanceSheetAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var priceHistoryData = await FetchSafeAsync(() => _priceHistoryService.GetPriceHistoryBySymbolAsync(cleanSymbol, cleanExchange, "5y", refresh, "all", cancellationToken));

            var companyName = companyData?.CompanyName ?? quartersData?.CompanyName ?? cashflowData?.CompanyName ?? shareholdingData?.CompanyName ?? cleanSymbol;

            var archetype = ClassifySectorArchetype(
                companyData?.Industry,
                companyData?.Sector,
                cashflowData?.Ratios?.SectorPeSector,
                companyName);

            var tracker = new MetricsTracker();
            var triggeredFatalRisks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var triggeredWarningRisks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var pros = new List<string>();
            var cons = new List<string>();
            var redFlags = new List<string>();

            // 1. Profitability & Cash Flow (Max: 25 Points)
            var pScore = EvaluateProfitability(cashflowData, archetype, tracker, pros, cons);

            // 2. Valuation & Fair Value (Max: 20 Points)
            var vScore = EvaluateValuation(cashflowData, archetype, tracker, pros, cons);

            // 3. Financial Safety & Debt (Max: 15 Points)
            var sScore = EvaluateDebtAndSolvency(cashflowData, quartersData, balanceSheetData, archetype, tracker, pros, cons, redFlags, triggeredFatalRisks);

            // 4. Growth & Technical Momentum (Max: 15 Points)
            var gScore = EvaluateGrowthAndMomentum(quartersData, priceHistoryData, cashflowData, tracker, pros, cons, redFlags, triggeredWarningRisks);

            // 5. Smart Money & Ownership (Max: 15 Points)
            var smScore = EvaluateOwnershipAndCapex(shareholdingData, balanceSheetData, tracker, pros, cons, redFlags, triggeredWarningRisks);

            // 6. Operating Efficiency & Working Capital (Max: 10 Points)
            var eScore = EvaluateWorkingCapitalEfficiency(cashflowData, archetype, tracker, pros, cons);

            // Aggregate Raw Score (0 - 100)
            int rawTotalScore = pScore.EarnedPoints + vScore.EarnedPoints + sScore.EarnedPoints +
                                gScore.EarnedPoints + smScore.EarnedPoints + eScore.EarnedPoints;
            int runningScore = rawTotalScore;
            var adjustments = new List<ScoreAdjustmentDto>();

            // 1. Operational Turnaround Growth Catalyst (Capped strictly at +3 pts)
            // Strict operational guards:
            // 1. PAT growth >= 100%
            // 2. Sales growth >= 10%
            // 3. Core Operating Profit > 0
            // 4. Operating Cash Flow > 0
            // 5. Operating Profit Growth MUST be verified (non-null) and >= 15% (missing data is never assumed as evidence)
            // Bonus is capped at +3 points to prevent double-counting quarterly momentum & low-base distortions.
            var salesGrowth = quartersData?.YoYGrowth?.SalesGrowthPercent ?? 0m;
            var profitGrowth = quartersData?.YoYGrowth?.NetProfitGrowthPercent ?? 0m;
            var opGrowth = quartersData?.YoYGrowth?.OperatingProfitGrowthPercent;
            var cfo = cashflowData?.Summary?.OperatingCashFlow;
            var op = quartersData?.Summary?.OperatingProfit ?? cashflowData?.Summary?.OperatingProfit ?? 0m;

            bool isOperationalTurnaround = profitGrowth >= 100m &&
                                           salesGrowth >= 10m &&
                                           op > 0m &&
                                           (cfo.HasValue && cfo.Value > 0m) &&
                                           (opGrowth.HasValue && opGrowth.Value >= 15m);

            if (isOperationalTurnaround)
            {
                const int turnaroundBonus = 3;
                runningScore += turnaroundBonus;
                adjustments.Add(new ScoreAdjustmentDto
                {
                    Name = "Operational Turnaround Catalyst",
                    Type = "Bonus",
                    PointsDelta = turnaroundBonus,
                    Reason = $"PAT +{profitGrowth:F1}% YoY backed by Topline (+{salesGrowth:F1}%), OP expansion (+{opGrowth!.Value:F1}%), and positive Operating Cash Flow"
                });
                pros.Add($"Operational Turnaround Catalyst: PAT +{profitGrowth:F1}% YoY backed by Topline (+{salesGrowth:F1}%), OP expansion (+{opGrowth!.Value:F1}%), and positive Operating Cash Flow");
            }

            // 2. Deduct for distinct warning risks (-8 per warning, e.g. promoter dump, margin crash)
            int warningDeductions = triggeredWarningRisks.Count * 8;
            if (warningDeductions > 0)
            {
                int effectiveWarningDeduction = Math.Min(runningScore, warningDeductions);
                runningScore = Math.Max(0, runningScore - warningDeductions);
                adjustments.Add(new ScoreAdjustmentDto
                {
                    Name = "Warning Risk Deductions",
                    Type = "Penalty",
                    PointsDelta = -effectiveWarningDeduction,
                    Reason = $"Deductions for {triggeredWarningRisks.Count} warning risk(s): {string.Join(", ", triggeredWarningRisks)}"
                });
            }

            // 3. Dynamic Circuit Breaker Cap for fatal risks (Scaled dynamically between 25 and 30 pts)
            bool hasFatalRisk = triggeredFatalRisks.Count > 0;
            if (hasFatalRisk)
            {
                // Ceiling starts at 30 for 1 fatal risk, drops by 2 pts for each additional fatal risk (floor: 25)
                int fatalRiskCeiling = Math.Max(25, 30 - ((triggeredFatalRisks.Count - 1) * 2));

                // Scale proportionally between 24 and fatalRiskCeiling based on pre-cap performance
                decimal normalizedStrength = Math.Clamp((decimal)runningScore / 100m, 0m, 1m);
                int dynamicCap = (int)Math.Round(24m + normalizedStrength * (fatalRiskCeiling - 24m));
                dynamicCap = Math.Clamp(dynamicCap, 25, fatalRiskCeiling);

                if (runningScore > dynamicCap)
                {
                    int fatalCapDelta = runningScore - dynamicCap;
                    runningScore = dynamicCap;
                    adjustments.Add(new ScoreAdjustmentDto
                    {
                        Name = "Fatal Risk Circuit Breaker Cap",
                        Type = "CircuitBreakerCap",
                        PointsDelta = -fatalCapDelta,
                        Reason = $"Score capped dynamically at {dynamicCap} points (range 25-30) due to critical solvency/risk trigger(s): {string.Join(", ", triggeredFatalRisks)}"
                    });
                }
            }

            // Calculate Data Coverage & Confidence based on sector-applicable metrics
            bool hasInventoryData = cashflowData?.Ratios?.InventoryDays.HasValue == true && cashflowData.Ratios.InventoryDays.Value > 0;
            var (applicableRules, inapplicableRules) = GetArchetypeMetricRules(archetype, hasInventoryData);
            var verifiedApplicable = applicableRules.Where(m => tracker.IsVerified(m)).ToList();
            var missingMetrics = applicableRules.Where(m => !tracker.IsVerified(m)).ToList();

            int totalExpectedMetrics = applicableRules.Count;
            int availableMetricsCount = verifiedApplicable.Count;

            var coveragePercent = totalExpectedMetrics > 0
                ? Math.Min(100m, Math.Round((decimal)availableMetricsCount / totalExpectedMetrics * 100, 1))
                : 0m;

            string dataConfidence;
            if (coveragePercent >= 80m)
                dataConfidence = "High";
            else if (coveragePercent >= 55m)
                dataConfidence = "Moderate";
            else if (coveragePercent >= 35m)
                dataConfidence = "Low";
            else
                dataConfidence = "Inconclusive";

            var result = new StockHealthScoreDto
            {
                Symbol = cleanSymbol,
                Exchange = cleanExchange,
                CompanyName = companyName,
                EvaluatedAt = DateTime.UtcNow,
                RawTotalScore = Math.Max(0, Math.Min(100, rawTotalScore)),
                TotalScore = Math.Max(0, Math.Min(100, runningScore)),
                ScoreAdjustments = adjustments,
                TotalApplicableMetrics = totalExpectedMetrics,
                AvailableMetricsCount = availableMetricsCount,
                MissingMetrics = missingMetrics,
                InapplicableMetrics = inapplicableRules,
                DataCoveragePercent = coveragePercent,
                DataConfidence = dataConfidence,
                ProfitabilityScore = pScore,
                ValuationScore = vScore,
                SolvencyScore = sScore,
                GrowthScore = gScore,
                SmartMoneyScore = smScore,
                EfficiencyScore = eScore,
                Pros = pros.Distinct().ToList(),
                Cons = cons.Distinct().ToList(),
                RedFlags = redFlags.Distinct().ToList()
            };

            // Signal Determination with Data Quality qualification
            if (dataConfidence == "Low" || dataConfidence == "Inconclusive")
            {
                if (hasFatalRisk)
                {
                    result.Signal = "Risky / Avoid";
                    result.SignalClass = "badge-danger";
                    result.SummaryText = "Critical solvency/governance risks identified with limited verified data coverage.";
                }
                else
                {
                    result.Signal = "Inconclusive";
                    result.SignalClass = "badge-warning";
                    result.SummaryText = $"Limited data coverage ({coveragePercent:F0}%). Fundamentals cannot be conclusively verified.";
                }
            }
            else
            {
                bool isTechnicalDowntrend = priceHistoryData != null &&
                                            priceHistoryData.ClosePrices != null &&
                                            priceHistoryData.ClosePrices.Count > 0 &&
                                            priceHistoryData.Dma200 != null &&
                                            priceHistoryData.Dma200.LastOrDefault(v => v.HasValue) is decimal d200 &&
                                            priceHistoryData.ClosePrices[^1] < d200;

                if (isTechnicalDowntrend && result.TotalScore > 70)
                {
                    // Gating Rule: A stock in confirmed technical downtrend (Price < 200 DMA) cannot exceed 70 pts or be "Strong Buy".
                    int dmaCapDelta = result.TotalScore - 70;
                    result.TotalScore = 70;
                    result.ScoreAdjustments.Add(new ScoreAdjustmentDto
                    {
                        Name = "200 DMA Downtrend Gating Cap",
                        Type = "CircuitBreakerCap",
                        PointsDelta = -dmaCapDelta,
                        Reason = "Score capped at 70 points: Technical downtrend (Price < 200 DMA) gates 'Strong Buy' status to 'Good for Buy'."
                    });
                }

                if (result.TotalScore >= 75)
                {
                    string valDesc = vScore.EarnedPoints >= 14 ? "attractive valuation" :
                                     vScore.EarnedPoints >= 10 ? "reasonable valuation" :
                                     "growth-led premium valuation";

                    result.Signal = "Strong Buy";
                    result.SignalClass = "badge-success";
                    result.SummaryText = $"Exceptional fundamentals with robust cash generation, {valDesc}, and strong governance.";
                }
                else if (result.TotalScore >= 60)
                {
                    result.Signal = "Good for Buy";
                    result.SignalClass = "badge-good";
                    result.SummaryText = isTechnicalDowntrend
                        ? "Robust core business & balance sheet, but intermediate technical downtrend (Price < 200 DMA) advises cautious phased accumulation."
                        : "Solid financials, healthy operating performance, and favorable risk-reward profile.";
                }
                else if (result.TotalScore >= 45)
                {
                    string neutralValDesc = vScore.EarnedPoints >= 12 ? "Fair valuation with moderate fundamental momentum" :
                                            vScore.EarnedPoints <= 7 ? "Mixed fundamentals with stretched valuation multiples" :
                                            "Mixed signals across fundamentals and valuation";

                    result.Signal = "Neutral / Hold";
                    result.SignalClass = "badge-warning";
                    result.SummaryText = $"{neutralValDesc}. Recommend monitoring upcoming quarters or waiting for better entry dips.";
                }
                else
                {
                    result.Signal = "Risky / Avoid";
                    result.SignalClass = "badge-danger";
                    result.SummaryText = "Elevated risk profile due to weak cash flow, excessive leverage, or negative earnings momentum.";
                }
            }

            return result;
        }

        #region Sector Classification

        private static SectorArchetype ClassifySectorArchetype(
            string? industry,
            string? sector,
            string? sectorPeSector,
            string? companyName)
        {
            // 1. Primary: Official NSE Industry
            if (!string.IsNullOrWhiteSpace(industry))
            {
                var ind = industry.Trim();
                if (ind.Equals("Financial Services", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.FinancialServices;
                if (ind.Equals("Information Technology", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.InformationTechnology;
                if (ind.Equals("Telecommunication", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Media Entertainment & Publication", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.TelecomAndMedia;
                if (ind.Equals("Healthcare", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.HealthcareAndPharma;
                if (ind.Equals("Fast Moving Consumer Goods", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Consumer Services", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Consumer Durables", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.ConsumerAndFmcg;
                if (ind.Equals("Power", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Oil Gas & Consumable Fuels", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.PowerAndUtilities;
                if (ind.Equals("Realty", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Construction", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Construction Materials", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.RealEstateAndConstruction;
                if (ind.Equals("Capital Goods", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Automobile and Auto Components", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Chemicals", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Metals & Mining", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Textiles", StringComparison.OrdinalIgnoreCase) ||
                    ind.Equals("Services", StringComparison.OrdinalIgnoreCase))
                    return SectorArchetype.ManufacturingAndIndustrials;
            }

            // 2. Secondary: Sector or SectorPeSector keyword detection
            var combinedSector = $"{sector} {sectorPeSector}".ToLowerInvariant();
            if (combinedSector.Contains("bank") || combinedSector.Contains("finance") || combinedSector.Contains("nbfc") ||
                combinedSector.Contains("housing") || combinedSector.Contains("amc") || combinedSector.Contains("broking"))
                return SectorArchetype.FinancialServices;

            if (combinedSector.Contains("software") || combinedSector.Contains("information tech") || combinedSector.Contains("it services") || combinedSector.Contains("computers"))
                return SectorArchetype.InformationTechnology;

            if (combinedSector.Contains("telecom") || combinedSector.Contains("media") || combinedSector.Contains("entertainment"))
                return SectorArchetype.TelecomAndMedia;

            if (combinedSector.Contains("pharma") || combinedSector.Contains("health") || combinedSector.Contains("hospital") || combinedSector.Contains("biotech"))
                return SectorArchetype.HealthcareAndPharma;

            if (combinedSector.Contains("fmcg") || combinedSector.Contains("consumer") || combinedSector.Contains("food") || combinedSector.Contains("retail"))
                return SectorArchetype.ConsumerAndFmcg;

            if (combinedSector.Contains("power") || combinedSector.Contains("energy") || combinedSector.Contains("utility") || combinedSector.Contains("gas") || combinedSector.Contains("petroleum"))
                return SectorArchetype.PowerAndUtilities;

            if (combinedSector.Contains("realt") || combinedSector.Contains("construct") || combinedSector.Contains("cement") || combinedSector.Contains("infra"))
                return SectorArchetype.RealEstateAndConstruction;

            if (combinedSector.Contains("auto") || combinedSector.Contains("chemical") || combinedSector.Contains("metal") || combinedSector.Contains("steel") || combinedSector.Contains("textile") || combinedSector.Contains("capital goods"))
                return SectorArchetype.ManufacturingAndIndustrials;

            // 3. Fallback: CompanyName indicators
            var name = (companyName ?? string.Empty).ToLowerInvariant();
            if (name.Contains("bank") || name.Contains("finance") || name.Contains("finserve") || name.Contains("capital"))
                return SectorArchetype.FinancialServices;
            if (name.Contains("infotech") || name.Contains("technologies") || name.Contains("software"))
                return SectorArchetype.InformationTechnology;
            if (name.Contains("pharma") || name.Contains("laboratories") || name.Contains("life sciences"))
                return SectorArchetype.HealthcareAndPharma;

            // 4. Default: GenericCorporate
            return SectorArchetype.GenericCorporate;
        }

        private static int CalculateNormalizedScore(
            int earnedPoints,
            int availableMaxPoints,
            int categoryMaxPoints,
            decimal minRequiredCoverage = 0.6m)
        {
            if (availableMaxPoints <= 0)
                return 0;

            // If available metrics cover less than 60% of the category,
            // avoid aggressive extrapolation from a single metric.
            if ((decimal)availableMaxPoints / categoryMaxPoints < minRequiredCoverage)
            {
                return Math.Min(categoryMaxPoints, earnedPoints);
            }

            decimal scaled = ((decimal)earnedPoints / availableMaxPoints) * categoryMaxPoints;
            return Math.Min(categoryMaxPoints, (int)Math.Round(scaled));
        }

        #endregion

        #region Evaluation Categories

        private static CategoryScoreDto EvaluateProfitability(
            StockCashflowResponseDto? cashflow,
            SectorArchetype archetype,
            MetricsTracker tracker,
            List<string> pros,
            List<string> cons)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Profitability & Cash Flow",
                MaxPoints = 25
            };

            int earned = 0;
            int availableMax = 0;
            var ratios = cashflow?.Ratios;
            var summary = cashflow?.Summary;

            // 1. Capital Compounding (Max: 8 pts)
            var roe = ratios?.Roe;
            var roce = ratios?.Roce;
            if (roe.HasValue || roce.HasValue)
            {
                tracker.Record(MetricNames.CapitalCompounding);
                availableMax += 8;
                decimal effectiveReturn = Math.Max(roe ?? 0m, roce ?? 0m);

                if (archetype == SectorArchetype.FinancialServices)
                {
                    var bankRoe = roe ?? effectiveReturn;
                    if (bankRoe >= 15m)
                    {
                        earned += 8;
                        pros.Add($"High Capital Compounding: Banking ROE ({bankRoe:F1}%) >= 15%");
                        category.Highlights.Add("High Banking Compounding (ROE >= 15%)");
                    }
                    else if (bankRoe >= 11m)
                    {
                        earned += 6;
                        category.Highlights.Add("Healthy Banking ROE (11-15%)");
                    }
                    else if (bankRoe >= 7m)
                    {
                        earned += 3;
                    }
                    else
                    {
                        earned += 1;
                        cons.Add($"Sub-par Banking ROE ({bankRoe:F1}%) lagging compounding benchmark");
                    }
                }
                else if (archetype == SectorArchetype.InformationTechnology ||
                         archetype == SectorArchetype.HealthcareAndPharma ||
                         archetype == SectorArchetype.ConsumerAndFmcg)
                {
                    if (effectiveReturn >= 18m)
                    {
                        earned += 8;
                        pros.Add($"Superior Capital Compounding: ROCE/ROE ({effectiveReturn:F1}%) >= 18%");
                        category.Highlights.Add("Elite Capital Compounding (>=18%)");
                    }
                    else if (effectiveReturn >= 13m)
                    {
                        earned += 6;
                        category.Highlights.Add("Healthy Capital Return (13-18%)");
                    }
                    else if (effectiveReturn >= 8m)
                    {
                        earned += 3;
                    }
                    else
                    {
                        earned += 1;
                        cons.Add($"Lagging Capital Productivity ({effectiveReturn:F1}%)");
                    }
                }
                else if (archetype == SectorArchetype.PowerAndUtilities ||
                         archetype == SectorArchetype.RealEstateAndConstruction)
                {
                    if (effectiveReturn >= 13m)
                    {
                        earned += 8;
                        pros.Add($"Strong Asset Return: ROCE ({effectiveReturn:F1}%) >= 13% on heavy asset base");
                        category.Highlights.Add("Strong Asset Return (ROCE >= 13%)");
                    }
                    else if (effectiveReturn >= 9m)
                    {
                        earned += 6;
                        category.Highlights.Add("Fair Regulated Asset Return (9-13%)");
                    }
                    else if (effectiveReturn >= 5m)
                    {
                        earned += 3;
                    }
                    else
                    {
                        earned += 1;
                    }
                }
                else
                {
                    if (effectiveReturn >= 15m)
                    {
                        earned += 8;
                        pros.Add($"High Capital Compounding: Return on Capital/Equity ({effectiveReturn:F1}%) >= 15%");
                        category.Highlights.Add("High Capital Compounding (>=15%)");
                    }
                    else if (effectiveReturn >= 10m)
                    {
                        earned += 6;
                        category.Highlights.Add("Healthy Capital Return (10-15%)");
                    }
                    else if (effectiveReturn >= 6m)
                    {
                        earned += 3;
                    }
                    else
                    {
                        earned += 1;
                        cons.Add($"Sub-par Capital Return ({effectiveReturn:F1}%) lagging benchmark");
                    }
                }
            }

            // 2. Free Cash Flow & Operating Cash Generation (Max: 7 pts)
            var fcf = summary?.FreeCashFlow;
            var cfo = summary?.OperatingCashFlow;
            if (fcf.HasValue || cfo.HasValue)
            {
                tracker.Record(MetricNames.CashFlowGeneration);
                availableMax += 7;

                if (archetype == SectorArchetype.PowerAndUtilities ||
                    archetype == SectorArchetype.RealEstateAndConstruction ||
                    archetype == SectorArchetype.TelecomAndMedia)
                {
                    if (fcf.HasValue && fcf.Value > 0)
                    {
                        earned += 7;
                        pros.Add($"Positive Free Cash Flow (₹{fcf.Value:N0} Cr) post continuous infrastructure capex");
                        category.Highlights.Add("Positive Free Cash Flow");
                    }
                    else if (cfo.HasValue && cfo.Value > 0)
                    {
                        earned += 6;
                        pros.Add($"Robust Operating Cash Flow (₹{cfo.Value:N0} Cr) funding long-term project expansion");
                        category.Highlights.Add("Strong Operating Cash Generation");
                    }
                    else if (cfo.HasValue && cfo.Value <= 0)
                    {
                        earned += 1;
                        cons.Add($"Negative Operating Cash Flow (₹{cfo.Value:N0} Cr) - operational cash deficit");
                    }
                    else if (fcf.HasValue && fcf.Value <= 0)
                    {
                        earned += 1;
                        cons.Add($"Negative Free Cash Flow (₹{fcf.Value:N0} Cr) post capex");
                    }
                    else
                    {
                        earned += 2;
                    }
                }
                else
                {
                    if (fcf.HasValue && fcf.Value > 0)
                    {
                        earned += 7;
                        pros.Add($"Positive Free Cash Flow (₹{fcf.Value:N0} Cr) demonstrates self-funding ability");
                        category.Highlights.Add("Positive Free Cash Flow");
                    }
                    else if (cfo.HasValue && cfo.Value > 0)
                    {
                        earned += 5;
                        pros.Add($"Strong Operating Cash Flow (₹{cfo.Value:N0} Cr) supporting operational reinvestment");
                        category.Highlights.Add("Robust Operating Cash Flow");
                    }
                    else if ((cfo.HasValue && cfo.Value <= 0) || (fcf.HasValue && fcf.Value <= 0))
                    {
                        earned += 1;
                        if (cfo.HasValue && cfo.Value <= 0 && fcf.HasValue && fcf.Value <= 0)
                        {
                            cons.Add($"Negative Operating & Free Cash Flow (₹{cfo.Value:N0} Cr)");
                        }
                        else if (cfo.HasValue && cfo.Value <= 0)
                        {
                            cons.Add($"Negative Operating Cash Flow (₹{cfo.Value:N0} Cr) - operational cash deficit");
                        }
                        else
                        {
                            cons.Add($"Negative Free Cash Flow (₹{fcf!.Value:N0} Cr) demonstrates operational cash burn");
                        }
                    }
                }
            }

            // 3. CFO / Operating Profit Conversion Quality (Max: 5 pts)
            var cfoRatio = summary?.CfoToOperatingProfitRatio;
            if (cfoRatio.HasValue)
            {
                tracker.Record(MetricNames.CashConversionQuality);
                availableMax += 5;
                if (cfoRatio.Value >= 0.75m)
                {
                    earned += 5;
                    pros.Add($"High Earnings Quality: CFO/Operating Profit conversion is {cfoRatio.Value * 100:F0}%");
                    category.Highlights.Add("High Cash Conversion Quality (>=75%)");
                }
                else if (cfoRatio.Value >= 0.45m)
                {
                    earned += 3;
                    category.Highlights.Add("Moderate Cash Conversion (45-75%)");
                }
                else if (cfoRatio.Value >= 0.15m)
                {
                    earned += 2;
                }
                else
                {
                    earned += 1;
                    cons.Add("Low CFO/OP ratio (< 45%): Accounting profits lagging actual cash realization");
                }
            }

            // 4. Net Cash Flow (Max: 5 pts)
            var netCashFlow = summary?.NetCashFlow;
            if (netCashFlow.HasValue)
            {
                tracker.Record(MetricNames.NetCashFlow);
                availableMax += 5;
                if (netCashFlow.Value > 0)
                {
                    earned += 5;
                    category.Highlights.Add("Positive Net Cash Flow");
                }
                else
                {
                    earned += 2;
                }
            }

            category.EarnedPoints = CalculateNormalizedScore(earned, availableMax, 25);
            category.Status = category.EarnedPoints >= 20 ? "Excellent" : category.EarnedPoints >= 13 ? "Good" : category.EarnedPoints >= 8 ? "Average" : "Poor";
            return category;
        }

        private static CategoryScoreDto EvaluateValuation(
            StockCashflowResponseDto? cashflow,
            SectorArchetype archetype,
            MetricsTracker tracker,
            List<string> pros,
            List<string> cons)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Valuation & Fair Value",
                MaxPoints = 20
            };

            var ratios = cashflow?.Ratios;
            if (ratios == null)
            {
                category.EarnedPoints = 0;
                category.Status = "N/A";
                return category;
            }

            int earned = 0;
            int availableMax = 0;

            var pe = ratios.PeRatio;
            var peg = ratios.PegRatio;
            var pb = ratios.PbRatio;
            var sectorPe = ratios.SectorPe;

            bool isLossMaking = pe.HasValue && pe.Value < 0;

            // 1. Asset & Growth Multiples (Max: 10 pts)
            if (isLossMaking)
            {
                tracker.Record(MetricNames.ValuationMultiple);
                availableMax += 10;
                if (pb.HasValue && pb.Value > 0 && pb.Value <= 2.0m)
                {
                    earned += 4;
                    cons.Add($"Loss-making operations (P/E is {pe!.Value:F1}x) - valuation partially supported by tangible book value (P/B {pb.Value:F2}x)");
                    category.Highlights.Add("Asset-Backed Valuation (Loss-making)");
                }
                else
                {
                    earned += 0;
                    cons.Add($"Loss-making company (Negative P/E of {pe!.Value:F1}x) with no attractive valuation floor");
                    category.Highlights.Add("Loss-Making (Negative P/E)");
                }
            }
            else if (archetype == SectorArchetype.FinancialServices)
            {
                if (pb.HasValue && pb.Value > 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    if (pb.Value <= 1.8m)
                    {
                        earned += 10;
                        pros.Add($"Attractive Financial Valuation: P/B ({pb.Value:F2}x) <= 1.8x Book Value");
                        category.Highlights.Add("Undervalued P/B (<= 1.8x)");
                    }
                    else if (pb.Value <= 2.8m)
                    {
                        earned += 7;
                        category.Highlights.Add("Fair Financial P/B (1.8 - 2.8x)");
                    }
                    else if (pb.Value <= 4.0m)
                    {
                        earned += 4;
                        category.Highlights.Add("Moderate P/B (2.8 - 4.0x)");
                    }
                    else
                    {
                        earned += 2;
                        cons.Add($"Elevated Price-to-Book multiple for financial sector ({pb.Value:F2}x > 4.0x)");
                    }
                }
            }
            else if (archetype == SectorArchetype.ConsumerAndFmcg)
            {
                if (peg.HasValue && peg.Value > 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    if (peg.Value < 1.2m)
                    {
                        earned += 10;
                        pros.Add($"Undervalued on Growth basis: PEG ratio ({peg.Value:F2}x) < 1.2x");
                        category.Highlights.Add("Attractive PEG (< 1.2x)");
                    }
                    else if (peg.Value <= 1.8m)
                    {
                        earned += 7;
                        category.Highlights.Add("Fair Consumer PEG (1.2 - 1.8x)");
                    }
                    else if (peg.Value <= 2.5m)
                    {
                        earned += 4;
                    }
                    else
                    {
                        earned += 2;
                        cons.Add($"Elevated Consumer PEG valuation ({peg.Value:F2}x > 2.5x)");
                    }
                }
                else if (peg.HasValue && peg.Value <= 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    earned += 1;
                    cons.Add($"Negative or Zero PEG ratio ({peg.Value:F1}x): Valuation is unbacked by earnings growth (Earnings contracting or stagnant)");
                    category.Highlights.Add("Unbacked PEG (Zero / Contracting Earnings)");
                }
                else if (pe.HasValue && pe.Value > 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    if (pe.Value <= 32m)
                    {
                        earned += 9;
                        pros.Add($"Reasonable Consumer P/E multiple ({pe.Value:F1}x <= 32x)");
                        category.Highlights.Add("Reasonable Consumer P/E (<= 32x)");
                    }
                    else if (pe.Value <= 50m)
                    {
                        earned += 6;
                        category.Highlights.Add("Standard FMCG Multiple (32 - 50x)");
                    }
                    else
                    {
                        earned += 3;
                        cons.Add($"Premium Consumer multiple (P/E is {pe.Value:F1}x > 50x)");
                    }
                }
            }
            else if (archetype == SectorArchetype.InformationTechnology ||
                     archetype == SectorArchetype.HealthcareAndPharma)
            {
                if (peg.HasValue && peg.Value > 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    if (peg.Value < 1.2m)
                    {
                        earned += 10;
                        pros.Add($"Undervalued on PEG basis: {peg.Value:F2}x < 1.2x");
                        category.Highlights.Add("Attractive PEG (< 1.2x)");
                    }
                    else if (peg.Value <= 1.8m)
                    {
                        earned += 7;
                        category.Highlights.Add("Fair PEG (1.2 - 1.8x)");
                    }
                    else if (peg.Value <= 2.5m)
                    {
                        earned += 4;
                    }
                    else
                    {
                        earned += 2;
                        cons.Add($"Elevated PEG multiple ({peg.Value:F2}x > 2.5x)");
                    }
                }
                else if (peg.HasValue && peg.Value <= 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    earned += 1;
                    cons.Add($"Negative or Zero PEG ratio ({peg.Value:F1}x): Valuation is unbacked by earnings growth (Earnings contracting or stagnant)");
                    category.Highlights.Add("Unbacked PEG (Zero / Contracting Earnings)");
                }
                else if (pb.HasValue && pb.Value > 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    if (pb.Value <= 3.0m)
                    {
                        earned += 8;
                        pros.Add($"Healthy P/B multiple ({pb.Value:F2}x <= 3.0x)");
                    }
                    else if (pb.Value <= 5.5m)
                    {
                        earned += 5;
                    }
                    else
                    {
                        earned += 2;
                    }
                }
            }
            else
            {
                if (peg.HasValue && peg.Value > 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    if (peg.Value < 1.0m)
                    {
                        earned += 10;
                        pros.Add($"Undervalued on PEG basis ({peg.Value:F2}x < 1.0)");
                        category.Highlights.Add("Undervalued PEG (< 1.0)");
                    }
                    else if (peg.Value <= 1.5m)
                    {
                        earned += 7;
                        category.Highlights.Add("Fairly Valued PEG (1.0 - 1.5x)");
                    }
                    else if (peg.Value <= 2.2m)
                    {
                        earned += 4;
                    }
                    else
                    {
                        earned += 2;
                        cons.Add($"High PEG valuation ({peg.Value:F2}x > 2.2x)");
                    }
                }
                else if (peg.HasValue && peg.Value <= 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    earned += 1;
                    cons.Add($"Negative or Zero PEG ratio ({peg.Value:F1}x): Valuation is unbacked by earnings growth (Earnings contracting or stagnant)");
                    category.Highlights.Add("Unbacked PEG (Zero / Contracting Earnings)");
                }
                else if (pb.HasValue && pb.Value > 0)
                {
                    tracker.Record(MetricNames.ValuationMultiple);
                    availableMax += 10;
                    if (pb.Value <= 1.6m)
                    {
                        earned += 10;
                        pros.Add($"Attractive Valuation: P/B ({pb.Value:F2}x) <= 1.6x book value");
                        category.Highlights.Add("Undervalued P/B (<= 1.6x)");
                    }
                    else if (pb.Value <= 2.6m)
                    {
                        earned += 7;
                        category.Highlights.Add("Fair P/B (1.6 - 2.6x)");
                    }
                    else if (pb.Value <= 3.8m)
                    {
                        earned += 4;
                    }
                    else
                    {
                        earned += 2;
                        cons.Add($"Elevated P/B multiple ({pb.Value:F2}x > 3.8x)");
                    }
                }
            }

            // 2. Relative Multiple: P/E vs Sector P/E (Max: 10 pts)
            if (!isLossMaking && pe.HasValue && pe.Value > 0)
            {
                if (sectorPe.HasValue && sectorPe.Value > 0)
                {
                    tracker.Record(MetricNames.RelativeValuation);
                    availableMax += 10;
                    if (pe.Value <= sectorPe.Value)
                    {
                        earned += 10;
                        pros.Add($"Trading at discount to Industry: P/E ({pe.Value:F1}x) <= Sector P/E ({sectorPe.Value:F1}x)");
                        category.Highlights.Add("Discount to Sector P/E");
                    }
                    else if (pe.Value <= sectorPe.Value * 1.25m)
                    {
                        earned += 7;
                        category.Highlights.Add("Fair Sector P/E Alignment");
                    }
                    else if (pe.Value <= sectorPe.Value * 1.6m)
                    {
                        earned += 4;
                    }
                    else
                    {
                        earned += 2;
                        cons.Add($"Premium Valuation: P/E ({pe.Value:F1}x) significantly above Sector ({sectorPe.Value:F1}x)");
                    }
                }
                else
                {
                    tracker.Record(MetricNames.RelativeValuation);
                    availableMax += 10;
                    if (pe.Value <= 20m) earned += 8;
                    else if (pe.Value <= 32m) earned += 6;
                    else if (pe.Value <= 45m) earned += 3;
                    else earned += 1;
                }
            }

            category.EarnedPoints = CalculateNormalizedScore(earned, availableMax, 20);
            category.Status = category.EarnedPoints >= 15 ? "Excellent" : category.EarnedPoints >= 10 ? "Good" : category.EarnedPoints >= 6 ? "Average" : "Poor";
            return category;
        }

        private static CategoryScoreDto EvaluateDebtAndSolvency(
            StockCashflowResponseDto? cashflow,
            StockQuarterlyResultsResponseDto? quarters,
            BalanceSheetResponseDto? balanceSheet,
            SectorArchetype archetype,
            MetricsTracker tracker,
            List<string> pros,
            List<string> cons,
            List<string> redFlags,
            HashSet<string> triggeredFatalRisks)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Financial Safety & Debt",
                MaxPoints = 15
            };

            int earned = 0;
            int availableMax = 0;

            // 1. Accounting-verified Insolvent Balance Sheet check
            var bookValue = cashflow?.Ratios?.BookValue;
            if (bookValue.HasValue)
            {
                tracker.Record(MetricNames.BookValueSolvency);
                if (bookValue.Value < 0)
                {
                    // Balance sheet line items in StockBalanceSheetService are named "Equity Capital" and "Reserves"
                    var equityRow = balanceSheet?.LineItems?.FirstOrDefault(l =>
                        l.Name.IndexOf("Total Equity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        l.Name.IndexOf("Net Worth", StringComparison.OrdinalIgnoreCase) >= 0);

                    var equityCapitalRow = balanceSheet?.LineItems?.FirstOrDefault(l =>
                        l.Name.IndexOf("Equity Capital", StringComparison.OrdinalIgnoreCase) >= 0);
                    var reservesRow = balanceSheet?.LineItems?.FirstOrDefault(l =>
                        l.Name.IndexOf("Reserves", StringComparison.OrdinalIgnoreCase) >= 0);

                    bool hasBsEquityData = false;
                    decimal bsNetWorth = 0m;

                    if (equityRow?.Values?.Count > 0 && equityRow.Values.LastOrDefault(v => v.HasValue) is decimal eq)
                    {
                        bsNetWorth = eq;
                        hasBsEquityData = true;
                    }
                    else if (equityCapitalRow?.Values?.Count > 0 || reservesRow?.Values?.Count > 0)
                    {
                        var eqCap = equityCapitalRow?.Values?.LastOrDefault(v => v.HasValue) ?? 0m;
                        var res = reservesRow?.Values?.LastOrDefault(v => v.HasValue) ?? 0m;
                        bsNetWorth = eqCap + res;
                        hasBsEquityData = true;
                    }

                    if (hasBsEquityData && bsNetWorth <= 0)
                    {
                        cons.Add("Negative Net Worth: Company's total liabilities exceed its assets");
                        redFlags.Add("Insolvent Balance Sheet: Net Worth is negative on Balance Sheet");
                        triggeredFatalRisks.Add("INSOLVENT_NET_WORTH");
                        category.EarnedPoints = 0;
                        category.Status = "Poor";
                        return category;
                    }
                    else if (hasBsEquityData && bsNetWorth > 0)
                    {
                        cons.Add($"Negative Book Value ratio reported ({bookValue.Value:F1}), but balance sheet confirms positive net worth (₹{bsNetWorth:N0} Cr)");
                    }
                    else
                    {
                        cons.Add($"Negative Book Value reported ({bookValue.Value:F1}) - unconfirmed by balance sheet");
                    }
                }
            }

            // Harmonize Operating Profit and Interest to Annual figures
            decimal op = 0m;
            bool hasOpData = false;
            decimal interest = 0m;
            bool hasInterestData = false;

            if (cashflow?.Summary?.OperatingProfit.HasValue == true)
            {
                op = cashflow.Summary.OperatingProfit.Value;
                hasOpData = true;
            }
            else if (quarters?.History != null && quarters.History.Count >= 4)
            {
                op = quarters.History.Take(4).Sum(q => q.OperatingProfit ?? 0);
                hasOpData = true;
            }
            else if (quarters?.Summary?.OperatingProfit.HasValue == true)
            {
                op = quarters.Summary.OperatingProfit.Value * 4m;
                hasOpData = true;
            }

            if (cashflow?.Summary?.Interest.HasValue == true)
            {
                interest = cashflow.Summary.Interest.Value;
                hasInterestData = true;
            }
            else if (quarters?.History != null && quarters.History.Count >= 4)
            {
                interest = quarters.History.Take(4).Sum(q => q.Interest ?? 0);
                hasInterestData = true;
            }
            else if (quarters?.Summary?.Interest.HasValue == true)
            {
                interest = quarters.Summary.Interest.Value * 4m;
                hasInterestData = true;
            }

            // Borrowings check from Balance Sheet (exclude 'Debtor' to avoid matching 'Trade Debtors')
            var borrowingsRow = balanceSheet?.LineItems?.FirstOrDefault(l =>
                l.Name.IndexOf("Borrowing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (l.Name.IndexOf("Debt", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 !l.Name.Contains("Debtor", StringComparison.OrdinalIgnoreCase)));

            var latestBorrowings = 0m;
            bool hasBorrowingsData = false;
            List<decimal> nonNullBorrowings = new();
            if (borrowingsRow != null && borrowingsRow.Values.Count > 0)
            {
                nonNullBorrowings = borrowingsRow.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (nonNullBorrowings.Count > 0)
                {
                    latestBorrowings = nonNullBorrowings[^1];
                    hasBorrowingsData = true;
                }
            }

            // 2. Debt Burden & Coverage Evaluation (Max: 8 pts)
            if (archetype == SectorArchetype.FinancialServices)
            {
                if (hasOpData || quarters?.Summary?.NetProfit.HasValue == true || cashflow?.Ratios?.PeRatio.HasValue == true)
                {
                    tracker.Record(MetricNames.DebtCoverage);
                    availableMax += 8;

                    category.Highlights.Add("Financial sector proxy scoring (NPA/CAR outside feed)");
                    var pe = cashflow?.Ratios?.PeRatio;
                    var netProfit = quarters?.Summary?.NetProfit;
                    bool isProfitable = (op > 0) || (pe.HasValue && pe.Value > 0) || (netProfit.HasValue && netProfit.Value > 0);
                    bool isLossMaking = (pe.HasValue && pe.Value < 0) || (netProfit.HasValue && netProfit.Value < 0 && op <= 0);

                    if (isProfitable && (bookValue == null || bookValue.Value > 0))
                    {
                        earned += 8;
                        pros.Add("Solvent Financial Operations: Positive net worth and profitable banking performance");
                    }
                    else if (isLossMaking)
                    {
                        earned += 2;
                        cons.Add("Operating losses in financial operations");
                    }
                    else
                    {
                        earned += 5;
                    }
                }
            }
            else
            {
                // Non-financials require confirmed debt evidence (interest or borrowings data).
                // Missing debt data must NOT be assumed as zero debt.
                if (hasInterestData || hasBorrowingsData)
                {
                    tracker.Record(MetricNames.DebtCoverage);
                    availableMax += 8;

                    // Debt-free is only confirmed when confirmed data shows zero debt:
                    // 1. Borrowings data exists and is <= 0, and interest is <= 0
                    // 2. Or interest data exists and is <= 0, and borrowings (if present) are <= 0
                    bool isZeroDebt = (hasBorrowingsData && latestBorrowings <= 0 && (!hasInterestData || interest <= 0)) ||
                                      (hasInterestData && interest <= 0 && (!hasBorrowingsData || latestBorrowings <= 0));

                    if (isZeroDebt)
                    {
                        earned += 8;
                        pros.Add("Virtually debt-free: Zero borrowings and zero interest expense");
                        category.Highlights.Add("Zero Debt & Surplus Cash");
                    }
                    else if (hasInterestData && interest > 0 && hasOpData)
                    {
                        if (op <= 0)
                        {
                            earned += 0;
                            cons.Add("Operating Loss with Debt: Company is making operating losses while servicing debt interest");
                            redFlags.Add("Severe Solvency Risk: Operating Loss with active interest servicing obligations");
                            triggeredFatalRisks.Add("OP_LOSS_WITH_DEBT");
                        }
                        else
                        {
                            var coverage = op / interest;
                            decimal eliteThreshold = (archetype == SectorArchetype.InformationTechnology) ? 5.0m :
                                                     (archetype == SectorArchetype.PowerAndUtilities || archetype == SectorArchetype.TelecomAndMedia) ? 2.2m :
                                                     (archetype == SectorArchetype.RealEstateAndConstruction) ? 2.5m : 4.0m;

                            decimal fairThreshold = (archetype == SectorArchetype.PowerAndUtilities || archetype == SectorArchetype.TelecomAndMedia) ? 1.4m :
                                                    (archetype == SectorArchetype.RealEstateAndConstruction) ? 1.5m : 2.0m;

                            if (coverage >= eliteThreshold)
                            {
                                earned += 8;
                                pros.Add($"Strong Debt Servicing: Interest coverage is {coverage:F1}x");
                                category.Highlights.Add($"High Interest Coverage ({coverage:F1}x)");
                            }
                            else if (coverage >= fairThreshold)
                            {
                                earned += 5;
                                category.Highlights.Add($"Adequate Interest Coverage ({coverage:F1}x)");
                            }
                            else if (coverage >= 1.0m)
                            {
                                earned += 2;
                                cons.Add($"Tight Interest Coverage ({coverage:F1}x) - debt servicing consumes significant operating profit");
                            }
                            else
                            {
                                earned += 0;
                                cons.Add($"Critically Low Interest Coverage ({coverage:F1}x) - operating profit insufficient for interest");
                                redFlags.Add("Interest coverage below 1.0x (Operating profit insufficient to service debt)");
                                triggeredFatalRisks.Add("INTEREST_COVERAGE_COLLAPSE");
                            }
                        }
                    }
                    else
                    {
                        earned += 4;
                    }
                }
            }

            // 3. Borrowings Trend in Balance Sheet (Max: 7 pts)
            if (nonNullBorrowings.Count >= 2)
            {
                tracker.Record(MetricNames.BorrowingsTrend);
                availableMax += 7;
                var latest = nonNullBorrowings[^1];
                var prev = nonNullBorrowings[^2];

                if (prev > 0)
                {
                    var changePct = ((latest - prev) / prev) * 100;
                    if (changePct <= 5)
                    {
                        earned += 7;
                        pros.Add(changePct <= 0 ? $"Borrowings reduced by {Math.Abs(changePct):F1}% YoY" : "Borrowings stable YoY");
                        category.Highlights.Add("Deleveraging / Stable Borrowings");
                    }
                    else if (changePct <= 15)
                    {
                        earned += 4;
                        category.Highlights.Add("Controlled Borrowings Growth");
                    }
                    else if (changePct > 25)
                    {
                        earned += 1;
                        cons.Add($"Total Borrowings increased rapidly by +{changePct:F1}% YoY");
                    }
                    else
                    {
                        earned += 3;
                    }
                }
                else if (latest <= 0)
                {
                    earned += 7;
                    pros.Add("Zero Debt company");
                }
                else if (prev <= 0 && latest > 0)
                {
                    earned += 2;
                    cons.Add($"Company newly took on borrowings (₹{latest:N0} Cr) from a zero-debt base");
                }
            }

            category.EarnedPoints = CalculateNormalizedScore(earned, availableMax, 15);
            category.Status = category.EarnedPoints >= 12 ? "Excellent" : category.EarnedPoints >= 8 ? "Good" : category.EarnedPoints >= 5 ? "Average" : "Poor";
            return category;
        }

        private static CategoryScoreDto EvaluateGrowthAndMomentum(
            StockQuarterlyResultsResponseDto? quarters,
            PriceHistoryResponseDto? priceHistory,
            StockCashflowResponseDto? cashflow,
            MetricsTracker tracker,
            List<string> pros,
            List<string> cons,
            List<string> redFlags,
            HashSet<string> triggeredWarningRisks)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Growth & Technicals",
                MaxPoints = 15
            };

            int earned = 0;
            int availableMax = 0;

            // 1. Quarterly YoY Growth (Max: 8 pts: Sales 4 pts, Profit 4 pts)
            var salesGrowth = quarters?.YoYGrowth?.SalesGrowthPercent;
            var profitGrowth = quarters?.YoYGrowth?.NetProfitGrowthPercent;

            if (salesGrowth.HasValue)
            {
                tracker.Record(MetricNames.SalesGrowthYoY);
                availableMax += 4;
                if (salesGrowth.Value >= 12m)
                {
                    earned += 4;
                    pros.Add($"Robust Topline Expansion: Sales +{salesGrowth.Value:F1}% YoY");
                }
                else if (salesGrowth.Value >= 5m)
                {
                    earned += 3;
                }
                else if (salesGrowth.Value >= 0m)
                {
                    earned += 2;
                }
                else
                {
                    earned += 1;
                    cons.Add($"Topline contraction: Quarterly Sales fell {salesGrowth.Value:F1}% YoY");
                }
            }

            if (profitGrowth.HasValue)
            {
                tracker.Record(MetricNames.ProfitGrowthYoY);
                availableMax += 4;
                if (profitGrowth.Value >= 15m)
                {
                    earned += 4;
                    pros.Add($"Strong Profit Growth: Net Profit +{profitGrowth.Value:F1}% YoY");
                }
                else if (profitGrowth.Value >= 8m)
                {
                    earned += 3;
                }
                else if (profitGrowth.Value >= 0m)
                {
                    earned += 2;
                }
                else if (profitGrowth.Value < -15m)
                {
                    earned += 0;
                    cons.Add($"Quarterly Profit contraction: Net profit fell {profitGrowth.Value:F1}% YoY");
                }
                else
                {
                    earned += 1;
                }
            }

            if (salesGrowth.HasValue && profitGrowth.HasValue)
            {
                if (salesGrowth.Value >= 8m && profitGrowth.Value >= 10m)
                {
                    category.Highlights.Add("Solid Sales & Profit Expansion");
                }

                // Severe Profit Contraction Warning (Deduplicated)
                if (salesGrowth.Value > 25 && profitGrowth.Value < -25)
                {
                    if (triggeredWarningRisks.Add("SEVERE_MARGIN_COLLAPSE"))
                    {
                        redFlags.Add($"Severe Margin Contraction: Sales grew +{salesGrowth.Value:F1}% YoY but Net Profit dropped {profitGrowth.Value:F1}% YoY");
                    }
                }
            }

            // 2. 50 DMA & 200 DMA Trend (Max: 4 pts: 50 DMA 2 pts, 200 DMA 2 pts)
            bool isBelow200Dma = false;
            if (priceHistory != null && priceHistory.ClosePrices != null && priceHistory.ClosePrices.Count > 0)
            {
                var latestPrice = priceHistory.ClosePrices[^1];
                var dma50 = priceHistory.Dma50?.LastOrDefault(v => v.HasValue);
                var dma200 = priceHistory.Dma200?.LastOrDefault(v => v.HasValue);

                if (dma50.HasValue)
                {
                    tracker.Record(MetricNames.Dma50);
                    availableMax += 2;
                    if (latestPrice > dma50.Value)
                    {
                        earned += 2;
                    }
                    else
                    {
                        earned += 1;
                    }
                }

                if (dma200.HasValue)
                {
                    tracker.Record(MetricNames.Dma200);
                    availableMax += 2;
                    if (latestPrice > dma200.Value)
                    {
                        earned += 2;
                        category.Highlights.Add("Trading above 200 DMA");
                    }
                    else
                    {
                        isBelow200Dma = true;
                        earned += 0;
                        cons.Add("Technical Downtrend: Price is currently below 200 DMA");
                    }
                }

                if (dma50.HasValue && dma200.HasValue && latestPrice > dma50.Value && dma50.Value > dma200.Value)
                {
                    pros.Add("Bullish Moving Average Alignment: Price > 50 DMA > 200 DMA (Golden Trend)");
                    category.Highlights.Add("Golden Trend (Price > 50 > 200 DMA)");
                }
            }

            // 3. Technical Consolidation & 52W Range (Max: 3 pts)
            bool dma200Unknown = priceHistory == null || priceHistory.Dma200 == null ||
                                 !priceHistory.Dma200.Any(v => v.HasValue);
            var price = cashflow?.Ratios?.CurrentPrice;
            var high = cashflow?.Ratios?.Week52High;
            var low = cashflow?.Ratios?.Week52Low;
            if (price.HasValue && high.HasValue && high.Value > 0)
            {
                tracker.Record(MetricNames.Price52WeekRange);
                availableMax += 3;
                var discountFromHigh = ((high.Value - price.Value) / high.Value) * 100;
                bool isNear52WLow = low.HasValue && low.Value > 0 &&
                                   (((price.Value - low.Value) / low.Value) * 100 <= 8m);

                if (isNear52WLow)
                {
                    earned += 0;
                    cons.Add("Near 52-Week Low: Stock is under persistent selling pressure near its 52-week low");
                    category.Highlights.Add("Near 52W Low (Selling Pressure)");
                }
                else if (dma200Unknown && discountFromHigh > 20)
                {
                    earned += 1;
                    cons.Add($"Stock is {discountFromHigh:F0}% below 52W High with unknown DMA200 trend");
                }
                else if (isBelow200Dma)
                {
                    earned += 1;
                    cons.Add($"Price Drawdown in Downtrend: Stock is {discountFromHigh:F0}% below 52W High while below 200 DMA");
                }
                else if (discountFromHigh >= 10 && discountFromHigh <= 35)
                {
                    earned += 3;
                    pros.Add($"Healthy technical consolidation: Trading {discountFromHigh:F0}% off 52W High");
                    category.Highlights.Add("Healthy pullback off 52W High");
                }
                else if (discountFromHigh < 10)
                {
                    earned += 2;
                    category.Highlights.Add("Near 52W High Momentum");
                }
                else if (discountFromHigh > 45)
                {
                    earned += 1;
                    cons.Add($"Deep Drawdown: Stock is trading {discountFromHigh:F0}% below 52-week peak");
                }
                else
                {
                    earned += 2;
                }
            }

            category.EarnedPoints = CalculateNormalizedScore(earned, availableMax, 15);
            category.Status = category.EarnedPoints >= 12 ? "Excellent" : category.EarnedPoints >= 8 ? "Good" : category.EarnedPoints >= 5 ? "Average" : "Poor";
            return category;
        }

        private static CategoryScoreDto EvaluateOwnershipAndCapex(
            StockShareholdingResponseDto? shareholding,
            BalanceSheetResponseDto? balanceSheet,
            MetricsTracker tracker,
            List<string> pros,
            List<string> cons,
            List<string> redFlags,
            HashSet<string> triggeredWarningRisks)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Smart Money & Ownership",
                MaxPoints = 15
            };

            int earned = 0;
            int availableMax = 0;
            var cur = shareholding?.CurrentPeriod;
            var change = shareholding?.Change;

            // 1. Promoter Holding & Board Governance (Max: 6 pts)
            if (cur?.Promoter.HasValue == true)
            {
                tracker.Record(MetricNames.PromoterHolding);
                availableMax += 6;
                var prom = cur.Promoter.Value;
                var promChange = change?.Promoter ?? 0;
                var totalInst = (cur.Fii ?? 0) + (cur.Dii ?? 0);

                if (prom < 15 && totalInst >= 50)
                {
                    earned += 6;
                    pros.Add($"Professionally Managed: High Institutional Ownership ({totalInst:F1}%) across FII & DII");
                    category.Highlights.Add("Institution-Backed Board Governance");
                }
                else if (prom >= 50 && promChange >= -0.5m)
                {
                    earned += 6;
                    pros.Add($"Strong & Stable Promoter Stake ({prom:F1}%)");
                    category.Highlights.Add("Strong Promoter Ownership (>=50%)");
                }
                else if (prom >= 40)
                {
                    earned += 4;
                    category.Highlights.Add("Moderate Promoter Ownership (40-50%)");
                }
                else if (prom >= 30)
                {
                    earned += 2;
                }
                else
                {
                    earned += 1;
                }

                if (promChange <= -4.0m)
                {
                    cons.Add($"Promoters trimmed stake by {Math.Abs(promChange):F2}% in recent quarter");
                    if (triggeredWarningRisks.Add("PROMOTER_STAKE_SALE"))
                    {
                        redFlags.Add($"Significant promoter stake sale ({Math.Abs(promChange):F1}% in single quarter)");
                    }
                }
            }

            // 2. Institutional (FII + DII) Trend (Max: 6 pts)
            if (change != null && (change.Fii.HasValue || change.Dii.HasValue))
            {
                tracker.Record(MetricNames.InstitutionalTrend);
                availableMax += 6;
                var instChange = (change.Fii ?? 0) + (change.Dii ?? 0);
                if (instChange > 0.3m)
                {
                    earned += 6;
                    pros.Add($"Institutional Accumulation: FII + DII increased holding by +{instChange:F2}%");
                    category.Highlights.Add("FII / DII Accumulation");
                }
                else if (instChange >= -0.5m)
                {
                    earned += 4;
                    category.Highlights.Add("Stable Institutional Stake");
                }
                else if (instChange <= -2.0m)
                {
                    earned += 1;
                    cons.Add($"Institutional Selling: FII + DII reduced stake by {Math.Abs(instChange):F2}%");
                }
                else
                {
                    earned += 2;
                }
            }

            // 3. Fixed Assets / Capex Expansion (Max: 3 pts)
            var assetGrowth = balanceSheet?.AssetGrowthPercentage;
            if (assetGrowth.HasValue)
            {
                tracker.Record(MetricNames.CapexExpansion);
                availableMax += 3;
                if (assetGrowth.Value >= 4.0m && assetGrowth.Value <= 35.0m)
                {
                    earned += 3;
                    pros.Add($"Healthy Capacity Expansion: Fixed assets grew +{assetGrowth.Value:F1}%");
                    category.Highlights.Add("Healthy Capex Expansion");
                }
                else if (assetGrowth.Value > 0)
                {
                    earned += 2;
                }
                else
                {
                    earned += 1;
                }
            }

            category.EarnedPoints = CalculateNormalizedScore(earned, availableMax, 15);
            category.Status = category.EarnedPoints >= 12 ? "Excellent" : category.EarnedPoints >= 8 ? "Good" : category.EarnedPoints >= 5 ? "Average" : "Poor";
            return category;
        }

        private static CategoryScoreDto EvaluateWorkingCapitalEfficiency(
            StockCashflowResponseDto? cashflow,
            SectorArchetype archetype,
            MetricsTracker tracker,
            List<string> pros,
            List<string> cons)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Operating Efficiency",
                MaxPoints = 10
            };

            var ratios = cashflow?.Ratios;
            if (ratios == null)
            {
                category.EarnedPoints = 0;
                category.Status = "N/A";
                return category;
            }

            int earned = 0;
            int availableMax = 0;

            var debtorDays = ratios.DebtorDays;
            var debtorDaysYoY = ratios.DebtorDaysYoY;
            var inventoryDays = ratios.InventoryDays;
            var inventoryDaysYoY = ratios.InventoryDaysYoY;
            var payableDays = ratios.PayableDays;
            var roce = ratios.Roce ?? ratios.Roe;

            if (archetype == SectorArchetype.FinancialServices)
            {
                var bankReturn = ratios.Roe ?? ratios.Roce;
                if (bankReturn.HasValue)
                {
                    // Note: Capital Compounding (ROE/ROCE) is already recorded under Profitability.
                    // We do not re-increment tracker here to ensure zero double-counting of metrics.
                    availableMax += 10;
                    if (bankReturn.Value >= 14m)
                    {
                        earned += 10;
                        pros.Add($"Strong Financial Productivity: Banking ROE ({bankReturn.Value:F1}%) >= 14%");
                        category.Highlights.Add("High Financial Asset Productivity");
                    }
                    else if (bankReturn.Value >= 10m)
                    {
                        earned += 7;
                        category.Highlights.Add("Healthy Financial Return");
                    }
                    else
                    {
                        earned += 4;
                    }
                }
                category.EarnedPoints = CalculateNormalizedScore(earned, availableMax, 10);
                category.Status = category.EarnedPoints >= 8 ? "Excellent" : category.EarnedPoints >= 5 ? "Good" : "Average";
                return category;
            }

            bool hasInventoryData = inventoryDays.HasValue && inventoryDays.Value > 0;
            bool isAssetLight = archetype == SectorArchetype.InformationTechnology || !hasInventoryData;

            if (isAssetLight)
            {
                if (debtorDays.HasValue)
                {
                    tracker.Record(MetricNames.DebtorDays);
                    availableMax += 6;
                    var dd = debtorDays.Value;
                    bool isImproving = debtorDaysYoY.HasValue && debtorDaysYoY.Value <= 0;

                    if (dd <= 75 || isImproving)
                    {
                        earned += 6;
                        pros.Add(isImproving && debtorDaysYoY.HasValue
                            ? $"Disciplined Billing Cycle: Debtor Days ({dd:N0} days) improved YoY ({debtorDaysYoY.Value:F1}%)"
                            : $"Disciplined Billing Cycle: Debtor Days ({dd:N0} days) <= 75 days");
                        category.Highlights.Add("Lean Debtor Cycle (<=75 days)");
                    }
                    else if (dd <= 95)
                    {
                        earned += 4;
                        category.Highlights.Add("Acceptable Debtor Cycle");
                    }
                    else
                    {
                        earned += 2;
                        if (debtorDaysYoY.HasValue && debtorDaysYoY.Value > 20)
                        {
                            cons.Add($"Collections stretched: Debtor Days lengthened by +{debtorDaysYoY.Value:F1}% YoY");
                        }
                    }
                }

                if (roce.HasValue)
                {
                    // Note: Capital Compounding (ROE/ROCE) is already recorded under Profitability.
                    // We do not re-increment tracker here to ensure zero double-counting of metrics.
                    availableMax += 4;
                    if (roce.Value >= 18m)
                    {
                        earned += 4;
                        pros.Add($"High Capital Productivity: Return on capital is {roce.Value:F1}%");
                        category.Highlights.Add("High Capital Productivity (>=18%)");
                    }
                    else if (roce.Value >= 12m)
                    {
                        earned += 3;
                    }
                    else
                    {
                        earned += 1;
                    }
                }
            }
            else
            {
                // 1. Debtor Days (Max: 4 pts)
                if (debtorDays.HasValue)
                {
                    tracker.Record(MetricNames.DebtorDays);
                    availableMax += 4;
                    var dd = debtorDays.Value;
                    bool isImproving = debtorDaysYoY.HasValue && debtorDaysYoY.Value <= 0;

                    if (dd <= 65 || isImproving)
                    {
                        earned += 4;
                        pros.Add(isImproving && debtorDaysYoY.HasValue
                            ? $"Disciplined Collections: Debtor Days ({dd:N0} days) improved YoY"
                            : $"Disciplined Collections: Debtor Days ({dd:N0} days) healthy");
                        category.Highlights.Add("Disciplined Debtor Days");
                    }
                    else if (dd <= 90)
                    {
                        earned += 3;
                        category.Highlights.Add("Manageable Debtor Days");
                    }
                    else
                    {
                        earned += 1;
                        if (debtorDaysYoY.HasValue && debtorDaysYoY.Value > 20)
                        {
                            cons.Add($"Debtor Days stretched by +{debtorDaysYoY.Value:F1}% YoY");
                        }
                    }
                }

                // 2. Inventory Days (Max: 3 pts)
                if (inventoryDays.HasValue)
                {
                    tracker.Record(MetricNames.InventoryDays);
                    availableMax += 3;
                    var inv = inventoryDays.Value;
                    bool isInvImproving = inventoryDaysYoY.HasValue && inventoryDaysYoY.Value <= 5;

                    decimal maxHealthyInv = archetype == SectorArchetype.HealthcareAndPharma ? 150m :
                                           archetype == SectorArchetype.RealEstateAndConstruction ? 400m :
                                           archetype == SectorArchetype.ConsumerAndFmcg ? 75m : 120m;

                    if (inv <= maxHealthyInv || isInvImproving)
                    {
                        earned += 3;
                        pros.Add(archetype == SectorArchetype.HealthcareAndPharma
                            ? $"Optimal Regulatory Inventory Cycle: Holding period ({inv:N0} days) within compliance buffer"
                            : $"Optimal Inventory Turnover: Holding cycle ({inv:N0} days) healthy or stable");
                        category.Highlights.Add("Optimal Inventory Cycle");
                    }
                    else
                    {
                        earned += 1;
                        cons.Add($"Extended Inventory Holding: Working capital tied up for {inv:N0} days");
                    }
                }

                // 3. Working Capital Float / Moat (Max: 3 pts)
                if (payableDays.HasValue && debtorDays.HasValue)
                {
                    tracker.Record(MetricNames.PayableDaysMoat);
                    availableMax += 3;
                    if (payableDays.Value >= debtorDays.Value)
                    {
                        earned += 3;
                        pros.Add(archetype == SectorArchetype.ConsumerAndFmcg
                            ? $"Negative Working Capital Moat: Suppliers fund operations (Payables {payableDays.Value:N0}d >= Debtors {debtorDays.Value:N0}d)"
                            : $"Working Capital Float: Payable Days ({payableDays.Value:N0}) >= Debtor Days ({debtorDays.Value:N0})");
                        category.Highlights.Add("Working Capital Advantage");
                    }
                    else if (payableDays.Value > 0)
                    {
                        earned += 2;
                    }
                    else
                    {
                        earned += 1;
                    }
                }
            }

            category.EarnedPoints = CalculateNormalizedScore(earned, availableMax, 10);
            category.Status = category.EarnedPoints >= 8 ? "Excellent" : category.EarnedPoints >= 5 ? "Good" : "Average";
            return category;
        }

        #endregion     

        public async Task<BreakoutEvaluationDto> GetBreakoutAnalysisAsync(string symbol, string? exchange = "NSE", bool refresh = false, CancellationToken cancellationToken = default)
        {
            var result = new BreakoutEvaluationDto();
            var cleanSymbol = (symbol ?? string.Empty).Trim().ToUpper();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpper();
            if (string.IsNullOrEmpty(cleanSymbol)) return result;
            var priceHistory = await _priceHistoryService.GetPriceHistoryBySymbolAsync(cleanSymbol, cleanExchange, "5y", refresh, "price", cancellationToken);

            if (priceHistory == null ||
                priceHistory.ClosePrices == null ||
                priceHistory.Highs == null ||
                priceHistory.Lows == null ||
                priceHistory.Volumes == null ||
                priceHistory.ClosePrices.Count == 0)
                return result;

            var closes = priceHistory.ClosePrices;
            var highs = priceHistory.Highs;
            var lows = priceHistory.Lows;
            var vols = priceHistory.Volumes;
            int daysAvailable = closes.Count;

            // Ensure equal series lengths to avoid IndexOutOfRangeException
            if (highs.Count != daysAvailable || lows.Count != daysAvailable || vols.Count != daysAvailable)
            {
                int minLen = Math.Min(daysAvailable, Math.Min(highs.Count, Math.Min(lows.Count, vols.Count)));
                if (minLen == 0) return result;
                closes = closes.TakeLast(minLen).ToList();
                highs = highs.TakeLast(minLen).ToList();
                lows = lows.TakeLast(minLen).ToList();
                vols = vols.TakeLast(minLen).ToList();
                daysAvailable = minLen;
            }

            result.CurrentPrice = closes.LastOrDefault();
            result.TodayVolume = vols.LastOrDefault();

            var historicalCloses = closes.Take(Math.Max(0, daysAvailable - 1)).ToList();
            var historicalHighs = highs.Take(Math.Max(0, daysAvailable - 1)).ToList();
            var historicalLows = lows.Take(Math.Max(0, daysAvailable - 1)).ToList();
            int historicalDays = historicalCloses.Count;

            if (historicalDays >= 252)
            {
                result.Levels.Year1High = historicalHighs.TakeLast(252).Max();
                result.Levels.Year1Low = historicalLows.TakeLast(252).Min();
            }

            if (historicalDays >= 504)
            {
                result.Levels.Year2High = historicalHighs.TakeLast(504).Max();
                result.Levels.Year2Low = historicalLows.TakeLast(504).Min();
            }

            if (historicalDays >= 756)
            {
                result.Levels.Year3High = historicalHighs.TakeLast(756).Max();
                result.Levels.Year3Low = historicalLows.TakeLast(756).Min();
            }

            var historicalVols = vols.Take(Math.Max(0, daysAvailable - 1)).ToList();
            var lookback = Math.Min(21, historicalVols.Count);
            if (lookback > 0)
            {
                result.AvgVolume1Month = (decimal)historicalVols.TakeLast(lookback).Average();
            }
            else
            {
                result.AvgVolume1Month = 0;
            }

            bool hasAvgVolume = result.AvgVolume1Month > 0;
            bool isAboveAvgVol = hasAvgVolume && result.TodayVolume >= (result.AvgVolume1Month * 1.25m);
            bool volumeConfirmed = isAboveAvgVol;
            bool isHighVol = hasAvgVolume && result.TodayVolume >= (result.AvgVolume1Month * 2m);
            bool isMassiveVol = hasAvgVolume && result.TodayVolume >= (result.AvgVolume1Month * 5m);
            
            var currentHigh = highs.LastOrDefault();
            var currentLow = lows.LastOrDefault();

            string signalBadgeText = "";
            if (result.Levels.Year3High.HasValue && currentHigh > result.Levels.Year3High.Value)
            {
                result.OverallSignal = "Bullish";
                result.BullishCount = 3; result.BearishCount = 0;
                signalBadgeText = isMassiveVol ? "3Y Breakout (5x Vol)" : (isAboveAvgVol ? "3Y Breakout (Above Avg Vol)" : "3Y Breakout (Low Vol)");
            }
            else if (result.Levels.Year2High.HasValue && currentHigh > result.Levels.Year2High.Value)
            {
                result.OverallSignal = "Bullish";
                result.BullishCount = 2; result.BearishCount = 0;
                signalBadgeText = isMassiveVol ? "2Y Breakout (5x Vol)" : (isAboveAvgVol ? "2Y Breakout (Above Avg Vol)" : "2Y Breakout (Low Vol)");
            }
            else if (result.Levels.Year1High.HasValue && currentHigh > result.Levels.Year1High.Value)
            {
                result.OverallSignal = "Bullish";
                result.BullishCount = 1; result.BearishCount = 0;
                signalBadgeText = isMassiveVol ? "52W High (5x Vol)" : (isAboveAvgVol ? "52W High (Above Avg Vol)" : "52W High (Low Vol)");
            }
            else if (result.Levels.Year3Low.HasValue && currentLow < result.Levels.Year3Low.Value)
            {
                result.OverallSignal = "Bearish";
                result.BearishCount = 3; result.BullishCount = 0;
                signalBadgeText = isMassiveVol ? "3Y Breakdown (5x Vol)" : (isAboveAvgVol ? "3Y Breakdown (Above Avg Vol)" : "3Y Breakdown (Low Vol)");
            }
            else if (result.Levels.Year2Low.HasValue && currentLow < result.Levels.Year2Low.Value)
            {
                result.OverallSignal = "Bearish";
                result.BearishCount = 2; result.BullishCount = 0;
                signalBadgeText = isMassiveVol ? "2Y Breakdown (5x Vol)" : (isAboveAvgVol ? "2Y Breakdown (Above Avg Vol)" : "2Y Breakdown (Low Vol)");
            }
            else if (result.Levels.Year1Low.HasValue && currentLow < result.Levels.Year1Low.Value)
            {
                result.OverallSignal = "Bearish";
                result.BearishCount = 1; result.BullishCount = 0;
                signalBadgeText = isMassiveVol ? "52W Low (5x Vol)" : (isAboveAvgVol ? "52W Low (Above Avg Vol)" : "52W Low (Low Vol)");
            }
            else
            {
                result.OverallSignal = "Neutral";
                signalBadgeText = "No Breakout";
                result.BullishCount = 0; result.BearishCount = 0;
            }

            // Signal Strength calculation
            if (result.OverallSignal == "Bullish")
            {
                if (isMassiveVol && result.BullishCount >= 2) result.SignalStrength = "Strong";
                else if (isHighVol || result.BullishCount >= 2) result.SignalStrength = "Medium";
                else result.SignalStrength = "Weak";
            }
            else if (result.OverallSignal == "Bearish")
            {
                if (isMassiveVol && result.BearishCount >= 2) result.SignalStrength = "Strong";
                else if (isHighVol || result.BearishCount >= 2) result.SignalStrength = "Medium";
                else result.SignalStrength = "Weak";
            }
            else
            {
                result.SignalStrength = "Neutral";
            }

            if (result.OverallSignal == "Bullish")
                result.SummaryText = $"{symbol} is showing a {(volumeConfirmed ? "confirmed" : "unconfirmed")} Bullish trend, hitting key highs with {(isMassiveVol ? "massive 5x" : isHighVol ? "high" : volumeConfirmed ? "above-average" : "low")} volume.";
            else if (result.OverallSignal == "Bearish")
                result.SummaryText = $"{symbol} is showing a {(volumeConfirmed ? "confirmed" : "unconfirmed")} Bearish trend, breaking its support levels with {(isMassiveVol ? "massive 5x" : isHighVol ? "high" : volumeConfirmed ? "above-average" : "low")} volume.";
            else
                result.SummaryText = $"{symbol} is trading in a normal range between its 52-Week High and Low.";

            // Volume Logic
            result.VolumeRatio = result.AvgVolume1Month > 0 ? (result.TodayVolume / result.AvgVolume1Month) : 0;

            // Year 1
            if (result.Levels.Year1High.HasValue && result.Levels.Year1Low.HasValue)
            {
                decimal range1 = result.Levels.Year1High.Value - result.Levels.Year1Low.Value;
                decimal progress1 = range1 > 0 ? Math.Max(0, Math.Min(100, ((result.CurrentPrice - result.Levels.Year1Low.Value) / range1) * 100)) : 0;
                bool isBrkHigh = currentHigh > result.Levels.Year1High.Value;
                bool isBrkLow = currentLow < result.Levels.Year1Low.Value;
                
                string tfTrend = isBrkHigh ? "BULLISH" : (isBrkLow ? "BEARISH" : "NEUTRAL");
                result.Timeframes.Add(new BreakoutTimeframeDto
                {
                    Period = "1 YEAR (52W)",
                    Trend = tfTrend,
                    High = result.Levels.Year1High.Value,
                    Low = result.Levels.Year1Low.Value,
                    ProgressPercent = progress1
                });
            }

            // Year 2
            if (result.Levels.Year2High.HasValue && result.Levels.Year2Low.HasValue)
            {
                decimal range2 = result.Levels.Year2High.Value - result.Levels.Year2Low.Value;
                decimal progress2 = range2 > 0 ? Math.Max(0, Math.Min(100, ((result.CurrentPrice - result.Levels.Year2Low.Value) / range2) * 100)) : 0;
                bool isBrkHigh = currentHigh > result.Levels.Year2High.Value;
                bool isBrkLow = currentLow < result.Levels.Year2Low.Value;
                
                string tfTrend = isBrkHigh ? "BULLISH" : (isBrkLow ? "BEARISH" : "NEUTRAL");
                result.Timeframes.Add(new BreakoutTimeframeDto
                {
                    Period = "2 YEARS",
                    Trend = tfTrend,
                    High = result.Levels.Year2High.Value,
                    Low = result.Levels.Year2Low.Value,
                    ProgressPercent = progress2
                });
            }

            // Year 3
            if (result.Levels.Year3High.HasValue && result.Levels.Year3Low.HasValue)
            {
                decimal range3 = result.Levels.Year3High.Value - result.Levels.Year3Low.Value;
                decimal progress3 = range3 > 0 ? Math.Max(0, Math.Min(100, ((result.CurrentPrice - result.Levels.Year3Low.Value) / range3) * 100)) : 0;
                bool isBrkHigh = currentHigh > result.Levels.Year3High.Value;
                bool isBrkLow = currentLow < result.Levels.Year3Low.Value;
                
                string tfTrend = isBrkHigh ? "BULLISH" : (isBrkLow ? "BEARISH" : "NEUTRAL");
                result.Timeframes.Add(new BreakoutTimeframeDto
                {
                    Period = "3 YEARS",
                    Trend = tfTrend,
                    High = result.Levels.Year3High.Value,
                    Low = result.Levels.Year3Low.Value,
                    ProgressPercent = progress3
                });
            }

            // Populate Priority Signals Dynamically
            int rank = 1;
            
            if (result.OverallSignal == "Bullish")
            {
                // Signal 1: Volume > 5x
                if (isMassiveVol)
                {
                    result.PrioritySignals.Add(new PrioritySignalDto { 
                        Rank = rank++, Level = "Highest", Description = "Volume > 5x Month Avg", 
                        SubDescription = "Massive institutional buying", Type = "BULLISH" 
                    });
                }
                
                // Signal 2: Breakout level
                result.PrioritySignals.Add(new PrioritySignalDto { 
                    Rank = rank++, Level = "High", Description = signalBadgeText, 
                    SubDescription = "Stock is breaking key resistance", Type = "BULLISH" 
                });

                // Signal 3: Volume > 1x (only if not massive)
                if (isAboveAvgVol && !isMassiveVol)
                {
                    result.PrioritySignals.Add(new PrioritySignalDto { 
                        Rank = rank++, Level = "Medium", Description = "Volume > 1 Month Avg", 
                        SubDescription = "Strong buying interest", Type = "BULLISH" 
                    });
                }
            }
            else if (result.OverallSignal == "Bearish")
            {
                // Signal 1: Volume > 5x
                if (isMassiveVol)
                {
                    result.PrioritySignals.Add(new PrioritySignalDto { 
                        Rank = rank++, Level = "Highest", Description = "Volume > 5x Month Avg", 
                        SubDescription = "Massive institutional selling", Type = "BEARISH" 
                    });
                }
                
                // Signal 2: Breakdown level
                result.PrioritySignals.Add(new PrioritySignalDto { 
                    Rank = rank++, Level = "High", Description = signalBadgeText, 
                    SubDescription = "Stock is breaking key support", Type = "BEARISH" 
                });

                // Signal 3: Volume > 1x
                if (isAboveAvgVol && !isMassiveVol)
                {
                    result.PrioritySignals.Add(new PrioritySignalDto { 
                        Rank = rank++, Level = "Medium", Description = "Volume > 1 Month Avg", 
                        SubDescription = "Strong selling pressure", Type = "BEARISH" 
                    });
                }
            }
            else
            {
                result.PrioritySignals.Add(new PrioritySignalDto { 
                    Rank = rank++, Level = "Low", Description = "Trading in Normal Range", 
                    SubDescription = "No clear breakout direction", Type = "NEUTRAL" 
                });
            }

            return result;
        }

        #region Helpers

        private static async Task<T?> FetchSafeAsync<T>(Func<Task<T>> taskFunc) where T : class?
        {
            try
            {
                return await taskFunc();
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
