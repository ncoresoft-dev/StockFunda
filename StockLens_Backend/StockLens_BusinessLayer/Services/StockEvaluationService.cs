using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.Models;
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

        public StockEvaluationService(
            IStockCashflowService cashflowService,
            IStockQuarterlyResultsService quartersService,
            IStockShareholdingService shareholdingService,
            IStockBalanceSheetService balanceSheetService,
            IStockPriceHistoryService priceHistoryService,
            IFinancialProvider financialProvider)
        {
            _cashflowService = cashflowService;
            _quartersService = quartersService;
            _shareholdingService = shareholdingService;
            _balanceSheetService = balanceSheetService;
            _priceHistoryService = priceHistoryService;
            _financialProvider = financialProvider;
        }

        public async Task<StockHealthScoreDto> EvaluateStockAsync(
            string symbol,
            string? exchange = "NSE",
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            var cleanSymbol = (symbol ?? string.Empty).Trim().ToUpper();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpper();

            // Run tasks in parallel to fetch data safely
            var cashflowTask = FetchSafeAsync(() => _cashflowService.GetCashflowBySymbolAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var quartersTask = FetchSafeAsync(() => _quartersService.GetQuarterlyResultsBySymbolAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var shareholdingTask = FetchSafeAsync(() => _shareholdingService.GetShareholdingBySymbolAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var balanceSheetTask = FetchSafeAsync(() => _balanceSheetService.GetBalanceSheetAsync(cleanSymbol, cleanExchange, refresh, cancellationToken));
            var priceHistoryTask = FetchSafeAsync(() => _priceHistoryService.GetPriceHistoryBySymbolAsync(cleanSymbol, cleanExchange, "1yr", refresh, "all", cancellationToken));

            await Task.WhenAll(cashflowTask, quartersTask, shareholdingTask, balanceSheetTask, priceHistoryTask);

            var cashflowData = cashflowTask.Result;
            var quartersData = quartersTask.Result;
            var shareholdingData = shareholdingTask.Result;
            var balanceSheetData = balanceSheetTask.Result;
            var priceHistoryData = priceHistoryTask.Result;

            var companyName = quartersData?.CompanyName ?? cashflowData?.CompanyName ?? shareholdingData?.CompanyName ?? cleanSymbol;

            var result = new StockHealthScoreDto
            {
                Symbol = cleanSymbol,
                Exchange = cleanExchange,
                CompanyName = companyName,
                EvaluatedAt = DateTime.UtcNow
            };

            var pros = new List<string>();
            var cons = new List<string>();
            var redFlags = new List<string>();

            // 1. Profitability & Cash Flow (Max: 25 Points)
            var pScore = EvaluateProfitability(cashflowData, pros, cons);
            result.ProfitabilityScore = pScore;

            // 2. Valuation & Fair Value (Max: 20 Points)
            var vScore = EvaluateValuation(cashflowData, pros, cons);
            result.ValuationScore = vScore;

            // 3. Financial Safety & Debt (Max: 15 Points)
            var sScore = EvaluateDebtAndSolvency(cashflowData, quartersData, balanceSheetData, pros, cons, redFlags);
            result.SolvencyScore = sScore;

            // 4. Growth & Technical Momentum (Max: 15 Points)
            var gScore = EvaluateGrowthAndMomentum(quartersData, priceHistoryData, pros, cons, redFlags);
            result.GrowthScore = gScore;

            // 5. Smart Money & Ownership (Max: 15 Points)
            var smScore = EvaluateOwnershipAndCapex(shareholdingData, balanceSheetData, pros, cons, redFlags);
            result.SmartMoneyScore = smScore;

            // 6. Operating Efficiency & Working Capital (Max: 10 Points)
            var eScore = EvaluateWorkingCapitalEfficiency(cashflowData, pros, cons);
            result.EfficiencyScore = eScore;

            // Aggregate Total Score (0 - 100)
            var totalScore = pScore.EarnedPoints + vScore.EarnedPoints + sScore.EarnedPoints +
                             gScore.EarnedPoints + smScore.EarnedPoints + eScore.EarnedPoints;

            // Check for Critical Red Flag overrides
            if (redFlags.Count > 0)
            {
                totalScore = Math.Min(totalScore, 44); // Cap at Risky / Avoid
            }

            result.TotalScore = Math.Max(0, Math.Min(100, totalScore));
            result.Pros = pros.Distinct().ToList();
            result.Cons = cons.Distinct().ToList();
            result.RedFlags = redFlags.Distinct().ToList();

            // Determine Signal & Badge
            if (result.TotalScore >= 75)
            {
                result.Signal = "Strong Buy";
                result.SignalClass = "badge-success";
                result.SummaryText = "Exceptional fundamentals with robust cash generation, attractive valuation, and institutional backing.";
            }
            else if (result.TotalScore >= 60)
            {
                result.Signal = "Good for Buy";
                result.SignalClass = "badge-good";
                result.SummaryText = "Solid financials, healthy operating performance, and favorable risk-reward profile.";
            }
            else if (result.TotalScore >= 45)
            {
                result.Signal = "Neutral / Hold";
                result.SignalClass = "badge-warning";
                result.SummaryText = "Mixed signals or fair valuation. Recommend monitoring upcoming quarters or waiting for better entry dips.";
            }
            else
            {
                result.Signal = "Risky / Avoid";
                result.SignalClass = "badge-danger";
                result.SummaryText = "Elevated risk profile due to weak cash flow, excessive leverage, or negative earnings momentum.";
            }



            return result;
        }

        #region Evaluation Categories

        private CategoryScoreDto EvaluateProfitability(StockCashflowResponseDto? cashflow, List<string> pros, List<string> cons)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Profitability & Cash Flow",
                MaxPoints = 25
            };

            int points = 0;
            var ratios = cashflow?.Ratios;
            var summary = cashflow?.Summary;

            // 1. ROCE & ROE (8 pts)
            if (ratios != null && (ratios.Roe.HasValue || ratios.Roce.HasValue))
            {
                var roe = ratios.Roe ?? 0;
                var roce = ratios.Roce ?? 0;

                if (roce >= 15 && roe >= 15)
                {
                    points += 8;
                    pros.Add($"High Capital Compounding: ROE ({roe:F1}%) & ROCE ({roce:F1}%) > 15%");
                    category.Highlights.Add("High ROE & ROCE (>15%)");
                }
                else if ((roce >= 10 && roe >= 10) || ((roce + roe) / 2m >= 11m))
                {
                    points += 6;
                    category.Highlights.Add("Healthy ROE & ROCE (10-15%)");
                }
                else if (roce >= 7 || roe >= 7)
                {
                    points += 4;
                    category.Highlights.Add("Stable Capital Return (7-10%)");
                }
                else
                {
                    cons.Add($"Moderate/Low ROE ({roe:F1}%) / ROCE ({roce:F1}%) underperforming capital efficiency benchmark");
                }
            }
            else
            {
                points += 4; // Neutral points for missing data
                category.Highlights.Add("ROE/ROCE Baseline");
            }

            // 2. Free Cash Flow & Operating Cash Flow (7 pts)
            var fcf = summary?.FreeCashFlow;
            var cfo = summary?.OperatingCashFlow;

            if (fcf.HasValue && fcf.Value > 0)
            {
                points += 7;
                pros.Add($"Positive Free Cash Flow (₹{fcf.Value:N0} Cr) indicates self-funded organic expansion");
                category.Highlights.Add("Positive Free Cash Flow");
            }
            else if (cfo.HasValue && cfo.Value > 0)
            {
                // Positive Operating Cash Flow with high Capex reinvestment for future capacity/growth
                points += 4;
                pros.Add($"Strong Operating Cash Flow (₹{cfo.Value:N0} Cr) reinvested into capacity expansion");
                category.Highlights.Add("Robust Operating Cash Generation");
            }
            else if (fcf.HasValue && fcf.Value < 0)
            {
                cons.Add($"Negative Free Cash Flow (₹{fcf.Value:N0} Cr) due to heavy capital expenditure or operational lag");
            }
            else
            {
                points += 3;
            }

            // 3. CFO / Operating Profit Conversion Quality (5 pts)
            var cfoRatio = summary?.CfoToOperatingProfitRatio;
            if (cfoRatio.HasValue && cfoRatio.Value >= 0.8m)
            {
                points += 5;
                pros.Add($"High Quality of Earnings: CFO/OP conversion is {cfoRatio.Value * 100:F0}%");
                category.Highlights.Add("High CFO/OP cash conversion (>80%)");
            }
            else if (cfoRatio.HasValue && cfoRatio.Value >= 0.5m)
            {
                points += 3;
                category.Highlights.Add("Healthy CFO/OP conversion (50-80%)");
            }
            else if (cfoRatio.HasValue && cfoRatio.Value >= 0.2m)
            {
                points += 1;
            }
            else if (cfoRatio.HasValue && cfoRatio.Value < 0.2m && cfoRatio.Value > 0)
            {
                cons.Add("Low CFO/OP ratio (< 50%): Accounting profits lagging actual cash realization");
            }
            else
            {
                points += 3; // Baseline if ratio not calculable
            }

            // 4. Net Cash Flow (5 pts)
            var netCashFlow = summary?.NetCashFlow ?? 0;
            if (netCashFlow > 0)
            {
                points += 5;
                category.Highlights.Add("Positive Net Cash Flow");
            }
            else if (summary?.NetCashFlow.HasValue == true)
            {
                points += 2;
            }
            else
            {
                points += 3; // Neutral baseline
            }

            category.EarnedPoints = points;
            category.Status = points >= 20 ? "Excellent" : points >= 13 ? "Good" : points >= 8 ? "Average" : "Poor";
            return category;
        }

        private CategoryScoreDto EvaluateValuation(StockCashflowResponseDto? cashflow, List<string> pros, List<string> cons)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Valuation & Fair Value",
                MaxPoints = 20
            };

            int points = 0;
            var ratios = cashflow?.Ratios;
            if (ratios == null)
            {
                category.EarnedPoints = 10;
                category.Status = "Average";
                return category;
            }

            // 1. PEG Ratio (10 pts)
            var peg = ratios.PegRatio;
            if (peg.HasValue && peg.Value > 0 && peg.Value < 1.0m)
            {
                points += 10;
                pros.Add($"Undervalued on PEG basis ({peg.Value:F2}x < 1.0)");
                category.Highlights.Add("Undervalued PEG (< 1.0)");
            }
            else if (peg.HasValue && peg.Value >= 1.0m && peg.Value <= 1.5m)
            {
                points += 7;
                category.Highlights.Add("Fairly Valued PEG (1.0 - 1.5x)");
            }
            else if (peg.HasValue && peg.Value > 1.5m && peg.Value <= 2.5m)
            {
                points += 4;
                category.Highlights.Add("Reasonable PEG (1.5 - 2.5x)");
            }
            else if (peg.HasValue && peg.Value > 2.5m)
            {
                points += 1;
                cons.Add($"High PEG valuation ({peg.Value:F2}x > 2.5)");
            }
            else
            {
                points += 5; // Neutral baseline when PEG is not available
                category.Highlights.Add("Fair Value Baseline");
            }

            // 2. P/E vs Sector P/E (6 pts)
            var pe = ratios.PeRatio;
            var sectorPe = ratios.SectorPe;
            if (pe.HasValue && sectorPe.HasValue && sectorPe.Value > 0)
            {
                if (pe.Value > 0 && pe.Value <= sectorPe.Value)
                {
                    points += 6;
                    pros.Add($"Trading at discount to Industry: P/E ({pe.Value:F1}x) <= Sector P/E ({sectorPe.Value:F1}x)");
                    category.Highlights.Add("Discount to Sector P/E");
                }
                else if (pe.Value <= sectorPe.Value * 1.3m)
                {
                    points += 4;
                    category.Highlights.Add("Fair Sector P/E Alignment");
                }
                else if (pe.Value <= sectorPe.Value * 1.8m)
                {
                    points += 2;
                }
                else
                {
                    cons.Add($"Premium Valuation: P/E ({pe.Value:F1}x) significantly above Sector ({sectorPe.Value:F1}x)");
                }
            }
            else if (pe.HasValue && pe.Value > 0)
            {
                if (pe.Value <= 20) points += 5;
                else if (pe.Value <= 35) points += 3;
                else points += 1;
            }
            else
            {
                points += 3;
            }

            // 3. 52W High / Low Position (4 pts)
            var price = ratios.CurrentPrice;
            var high = ratios.Week52High;
            if (price.HasValue && high.HasValue && high.Value > 0)
            {
                var discountFromHigh = ((high.Value - price.Value) / high.Value) * 100;
                if (discountFromHigh >= 10 && discountFromHigh <= 35)
                {
                    points += 4;
                    pros.Add($"Attractive entry point: Trading {discountFromHigh:F0}% off 52W High");
                    category.Highlights.Add("Healthy pullback off 52W High");
                }
                else if (discountFromHigh < 10 && discountFromHigh >= 0)
                {
                    points += 2;
                }
                else
                {
                    points += 1;
                }
            }
            else
            {
                points += 2;
            }

            category.EarnedPoints = points;
            category.Status = points >= 15 ? "Excellent" : points >= 10 ? "Good" : points >= 6 ? "Average" : "Poor";
            return category;
        }

        private CategoryScoreDto EvaluateDebtAndSolvency(
            StockCashflowResponseDto? cashflow,
            StockQuarterlyResultsResponseDto? quarters,
            BalanceSheetResponseDto? balanceSheet,
            List<string> pros,
            List<string> cons,
            List<string> redFlags)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Financial Safety & Debt",
                MaxPoints = 15
            };

            int points = 0;

            // Harmonize Operating Profit and Interest to Annual figures
            decimal op = 0m;
            decimal interest = 0m;

            if (cashflow?.Summary?.OperatingProfit.HasValue == true && cashflow.Summary.OperatingProfit.Value > 0)
            {
                op = cashflow.Summary.OperatingProfit.Value;
            }
            else if (quarters?.History != null && quarters.History.Count >= 4)
            {
                op = quarters.History.Take(4).Sum(q => q.OperatingProfit ?? 0);
            }
            else if (quarters?.Summary?.OperatingProfit.HasValue == true)
            {
                op = quarters.Summary.OperatingProfit.Value * 4m; // Annualize single quarter
            }

            if (cashflow?.Summary?.Interest.HasValue == true && cashflow.Summary.Interest.Value > 0)
            {
                interest = cashflow.Summary.Interest.Value;
            }
            else if (quarters?.History != null && quarters.History.Count >= 4)
            {
                interest = quarters.History.Take(4).Sum(q => q.Interest ?? 0);
            }
            else if (quarters?.Summary?.Interest.HasValue == true)
            {
                interest = quarters.Summary.Interest.Value * 4m; // Annualize single quarter
            }

            // Borrowings check from Balance Sheet
            var borrowingsRow = balanceSheet?.LineItems?.FirstOrDefault(l =>
                l.Name.IndexOf("Borrowing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                l.Name.IndexOf("Debt", StringComparison.OrdinalIgnoreCase) >= 0);
            
            var latestBorrowings = 0m;
            if (borrowingsRow != null && borrowingsRow.Values.Count > 0)
            {
                var nonNullVals = borrowingsRow.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (nonNullVals.Count > 0)
                {
                    latestBorrowings = nonNullVals[^1];
                }
            }

            // 1. Interest Coverage Ratio (8 pts)
            if (interest <= 0 && latestBorrowings <= 0)
            {
                points += 8; // True zero debt
                pros.Add("Virtually debt-free (Zero debt on balance sheet)");
                category.Highlights.Add("Negligible Interest Expense");
            }
            else if (interest <= 0 && latestBorrowings > 0)
            {
                points += 4;
                category.Highlights.Add("Interest Data Baseline");
            }
            else if (interest > 0 && op > 0)
            {
                var coverage = op / interest;
                if (coverage >= 4.0m)
                {
                    points += 8;
                    pros.Add($"Strong Debt Servicing: Interest coverage is {coverage:F1}x");
                    category.Highlights.Add("High Interest Coverage (> 4x)");
                }
                else if (coverage >= 2.0m)
                {
                    points += 5;
                    category.Highlights.Add("Moderate Interest Coverage (2-4x)");
                }
                else if (coverage >= 1.2m)
                {
                    points += 2;
                }
                else
                {
                    cons.Add($"Critically Low Interest Coverage ({coverage:F1}x) - debt service pressure");
                    redFlags.Add("Interest coverage below 1.2x (High solvency risk)");
                }
            }
            else
            {
                points += 4;
            }

            // 2. Borrowings trend in Balance Sheet (7 pts)
            if (borrowingsRow != null && borrowingsRow.Values.Count >= 2)
            {
                var nonNullVals = borrowingsRow.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (nonNullVals.Count >= 2)
                {
                    var latest = nonNullVals[^1];
                    var prev = nonNullVals[^2];
                    if (prev > 0)
                    {
                        var changePct = ((latest - prev) / prev) * 100;
                        if (changePct <= 5)
                        {
                            points += 7;
                            pros.Add(changePct <= 0 ? $"Borrowings reduced by {Math.Abs(changePct):F1}% YoY" : "Borrowings stable YoY");
                            category.Highlights.Add("Deleveraging / Stable Debt Profile");
                        }
                        else if (changePct <= 15)
                        {
                            points += 4;
                            category.Highlights.Add("Controlled Debt Growth");
                        }
                        else if (changePct > 25)
                        {
                            cons.Add($"Total Borrowings increased rapidly by {changePct:F1}% YoY");
                            points += 1;
                        }
                        else
                        {
                            points += 3;
                        }
                    }
                    else if (latest == 0)
                    {
                        points += 7;
                        pros.Add("Zero Debt company");
                    }
                }
            }
            else
            {
                points += 5; // Default neutral points if balance sheet debt item is non-applicable
            }

            category.EarnedPoints = points;
            category.Status = points >= 12 ? "Excellent" : points >= 8 ? "Good" : points >= 5 ? "Average" : "Poor";
            return category;
        }

        private CategoryScoreDto EvaluateGrowthAndMomentum(
            StockQuarterlyResultsResponseDto? quarters,
            PriceHistoryResponseDto? priceHistory,
            List<string> pros,
            List<string> cons,
            List<string> redFlags)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Growth & Technicals",
                MaxPoints = 15
            };

            int points = 0;

            // 1. Quarterly YoY Growth (8 pts)
            var salesGrowth = quarters?.YoYGrowth?.SalesGrowthPercent;
            var profitGrowth = quarters?.YoYGrowth?.NetProfitGrowthPercent;

            if (salesGrowth.HasValue && profitGrowth.HasValue)
            {
                if (salesGrowth.Value >= 8 && profitGrowth.Value >= 10)
                {
                    points += 8;
                    pros.Add($"Robust Growth: Latest Qtr Sales +{salesGrowth.Value:F1}% YoY, PAT +{profitGrowth.Value:F1}% YoY");
                    category.Highlights.Add("Solid Topline & Bottomline Growth");
                }
                else if (salesGrowth.Value >= 0 && profitGrowth.Value >= 0)
                {
                    points += 5;
                    category.Highlights.Add("Positive Sales & Profit Expansion");
                }
                else if (salesGrowth.Value >= 0 || profitGrowth.Value >= 0)
                {
                    points += 3;
                }
                else if (profitGrowth.Value < -15)
                {
                    cons.Add($"Quarterly Profit contraction: Net profit fell {profitGrowth.Value:F1}% YoY");
                }
                else
                {
                    points += 2;
                }
                
                // Revenue-Profit Mismatch Red Flag (Severe margin collapse)
                if (salesGrowth.Value > 25 && profitGrowth.Value < -25)
                {
                    redFlags.Add($"Revenue-Profit Mismatch: Sales grew {salesGrowth.Value:F1}% but Profits crashed {profitGrowth.Value:F1}% (Severe margin crush)");
                }
            }
            else
            {
                points += 4;
            }

            // 2. 50 DMA & 200 DMA Trend (7 pts)
            if (priceHistory != null && priceHistory.ClosePrices.Count > 0)
            {
                var latestPrice = priceHistory.ClosePrices[^1];
                var dma50 = priceHistory.Dma50.LastOrDefault(v => v.HasValue);
                var dma200 = priceHistory.Dma200.LastOrDefault(v => v.HasValue);

                if (dma50.HasValue && dma200.HasValue)
                {
                    if (latestPrice > dma50.Value && dma50.Value > dma200.Value)
                    {
                        points += 7;
                        pros.Add("Bullish Moving Average Alignment: Price > 50 DMA > 200 DMA (Golden Trend)");
                        category.Highlights.Add("Golden Trend (Price > 50 > 200 DMA)");
                    }
                    else if (latestPrice > dma200.Value)
                    {
                        points += 4;
                        category.Highlights.Add("Trading above 200 DMA");
                    }
                    else if (latestPrice > dma50.Value)
                    {
                        points += 3;
                    }
                    else
                    {
                        cons.Add("Technical Downtrend: Price is currently below 200 DMA");
                        points += 1;
                    }
                }
                else
                {
                    points += 4;
                }
            }
            else
            {
                points += 4;
            }

            category.EarnedPoints = points;
            category.Status = points >= 12 ? "Excellent" : points >= 8 ? "Good" : points >= 5 ? "Average" : "Poor";
            return category;
        }

        private CategoryScoreDto EvaluateOwnershipAndCapex(
            StockShareholdingResponseDto? shareholding,
            BalanceSheetResponseDto? balanceSheet,
            List<string> pros,
            List<string> cons,
            List<string> redFlags)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Smart Money & Ownership",
                MaxPoints = 15
            };

            int points = 0;
            var cur = shareholding?.CurrentPeriod;
            var change = shareholding?.Change;

            // 1. Promoter Holding (6 pts)
            if (cur?.Promoter.HasValue == true)
            {
                var prom = cur.Promoter.Value;
                var promChange = change?.Promoter ?? 0;

                if (prom >= 50 && promChange >= -0.5m)
                {
                    points += 6;
                    pros.Add($"High & Committed Promoter Stake ({prom:F1}%) with stable ownership");
                    category.Highlights.Add("Strong Promoter Ownership (>50%)");
                }
                else if (prom >= 40)
                {
                    points += 4;
                    category.Highlights.Add("Moderate Promoter Ownership (40-50%)");
                }
                else if (prom >= 30)
                {
                    points += 2;
                }
                else
                {
                    points += 1;
                }

                if (promChange <= -4.0m)
                {
                    cons.Add($"Promoters trimmed stake by {Math.Abs(promChange):F2}% in recent quarter");
                    redFlags.Add("Significant promoter stake sale (>4% in single quarter)");
                }
            }
            else
            {
                points += 3;
            }

            // 2. Institutional (FII + DII) Trend (6 pts)
            if (change != null)
            {
                var instChange = (change.Fii ?? 0) + (change.Dii ?? 0);
                if (instChange > 0.3m)
                {
                    points += 6;
                    pros.Add($"Institutional Accumulation: FII + DII increased holding by +{instChange:F2}%");
                    category.Highlights.Add("FII / DII Accumulation");
                }
                else if (instChange >= -0.5m)
                {
                    points += 4;
                    category.Highlights.Add("Stable Institutional Stake");
                }
                else if (instChange < -2.0m)
                {
                    cons.Add($"Institutional Selling: FII + DII reduced stake by {Math.Abs(instChange):F2}%");
                    points += 1;
                }
                else
                {
                    points += 2;
                }
            }
            else
            {
                points += 3;
            }

            // 3. Fixed Assets / Capex Expansion (3 pts)
            var assetGrowth = balanceSheet?.AssetGrowthPercentage ?? 0;
            if (assetGrowth >= 4.0m && assetGrowth <= 35.0m)
            {
                points += 3;
                pros.Add($"Healthy Capacity Expansion: Fixed assets grew +{assetGrowth:F1}%");
                category.Highlights.Add("Healthy Capex Expansion");
            }
            else if (assetGrowth > 0)
            {
                points += 2;
            }
            else
            {
                points += 1;
            }

            category.EarnedPoints = points;
            category.Status = points >= 12 ? "Excellent" : points >= 8 ? "Good" : points >= 5 ? "Average" : "Poor";
            return category;
        }

        private CategoryScoreDto EvaluateWorkingCapitalEfficiency(StockCashflowResponseDto? cashflow, List<string> pros, List<string> cons)
        {
            var category = new CategoryScoreDto
            {
                CategoryName = "Operating Efficiency",
                MaxPoints = 10
            };

            int points = 0;
            var ratios = cashflow?.Ratios;
            if (ratios == null)
            {
                category.EarnedPoints = 5;
                category.Status = "Average";
                return category;
            }

            // 1. Debtor Days (4 pts)
            if (ratios.DebtorDays.HasValue)
            {
                var dd = ratios.DebtorDays.Value;
                var ddChange = ratios.DebtorDaysYoY ?? 0;

                if (dd <= 60 || ddChange <= 0)
                {
                    points += 4;
                    pros.Add($"Disciplined Collections: Debtor Days ({dd:N0} days) fast or improving");
                    category.Highlights.Add("Lean Debtor Days (<60 days)");
                }
                else if (dd <= 90)
                {
                    points += 2;
                }
                else if (ddChange > 20)
                {
                    cons.Add($"Debtor Days lengthened by +{ddChange:F1}% (slower collections from clients)");
                    points += 1;
                }
                else
                {
                    points += 2;
                }
            }
            else
            {
                points += 2;
            }

            // 2. Inventory Days (3 pts)
            if (ratios.InventoryDays.HasValue)
            {
                var invChange = ratios.InventoryDaysYoY ?? 0;
                if (invChange <= 10 || ratios.InventoryDays.Value <= 60)
                {
                    points += 3;
                    category.Highlights.Add("Optimal Inventory Turnover");
                }
                else
                {
                    points += 1;
                }
            }
            else
            {
                points += 2;
            }

            // 3. Payable Days (3 pts)
            if (ratios.PayableDays.HasValue && ratios.PayableDays.Value > 0)
            {
                points += 3;
            }
            else
            {
                points += 2;
            }

            category.EarnedPoints = points;
            category.Status = points >= 8 ? "Excellent" : points >= 5 ? "Good" : "Average";
            return category;
        }

        public async Task<BreakoutEvaluationDto> GetBreakoutAnalysisAsync(string symbol, string? exchange = "NSE", bool refresh = false, CancellationToken cancellationToken = default)
        {
            var result = new BreakoutEvaluationDto();
            var priceHistory = await _priceHistoryService.GetPriceHistoryBySymbolAsync(symbol, exchange, "3yr", refresh, "price", cancellationToken);

            if (priceHistory == null || priceHistory.ClosePrices.Count == 0)
                return result;

            var closes = priceHistory.ClosePrices;
            var vols = priceHistory.Volumes;
            int daysAvailable = closes.Count;

            result.CurrentPrice = closes.LastOrDefault();
            result.TodayVolume = vols.LastOrDefault();

            var year1Days = Math.Min(252, daysAvailable);
            var year2Days = Math.Min(504, daysAvailable);
            var year3Days = Math.Min(756, daysAvailable);

            result.Levels.Year1High = closes.TakeLast(year1Days).Max();
            result.Levels.Year1Low = closes.TakeLast(year1Days).Min();

            if (daysAvailable >= 253)
            {
                result.Levels.Year2High = closes.TakeLast(year2Days).Max();
                result.Levels.Year2Low = closes.TakeLast(year2Days).Min();
            }
            if (daysAvailable >= 505)
            {
                result.Levels.Year3High = closes.TakeLast(year3Days).Max();
                result.Levels.Year3Low = closes.TakeLast(year3Days).Min();
            }

            var avgVol1MonthDays = Math.Min(21, daysAvailable);
            result.AvgVolume1Month = (decimal)vols.TakeLast(avgVol1MonthDays).Average();

            bool isHighVol = result.TodayVolume > result.AvgVolume1Month;
            bool isMassiveVol = result.TodayVolume > (result.AvgVolume1Month * 5m);

            if (result.Levels.Year3High.HasValue && result.CurrentPrice >= result.Levels.Year3High.Value * 0.98m)
            {
                result.OverallSignal = "Bullish";
                result.SignalBadgeText = isMassiveVol ? "🚀 3Y-BO (>5x Vol)" : "🚀 3Y Breakout";
                result.BullishCount = 3; result.BearishCount = 0;
            }
            else if (result.Levels.Year2High.HasValue && result.CurrentPrice >= result.Levels.Year2High.Value * 0.98m)
            {
                result.OverallSignal = "Bullish";
                result.SignalBadgeText = isMassiveVol ? "🚀 2Y-BO (>5x Vol)" : "🚀 2Y Breakout";
                result.BullishCount = 2; result.BearishCount = 0;
            }
            else if (result.Levels.Year1High.HasValue && result.CurrentPrice >= result.Levels.Year1High.Value * 0.98m)
            {
                result.OverallSignal = "Bullish";
                result.SignalBadgeText = isMassiveVol ? "🔥 52W High (>5x Vol)" : "⬆️ 52W High";
                result.BullishCount = 1; result.BearishCount = 0;
            }
            else if (result.Levels.Year3Low.HasValue && result.CurrentPrice <= result.Levels.Year3Low.Value * 1.02m)
            {
                result.OverallSignal = "Bearish";
                result.SignalBadgeText = isMassiveVol ? "🩸 3Y Low (>5x Vol)" : "🩸 3Y Low";
                result.BearishCount = 3; result.BullishCount = 0;
            }
            else if (result.Levels.Year2Low.HasValue && result.CurrentPrice <= result.Levels.Year2Low.Value * 1.02m)
            {
                result.OverallSignal = "Bearish";
                result.SignalBadgeText = isMassiveVol ? "🩸 2Y Low (>5x Vol)" : "🩸 2Y Low";
                result.BearishCount = 2; result.BullishCount = 0;
            }
            else if (result.Levels.Year1Low.HasValue && result.CurrentPrice <= result.Levels.Year1Low.Value * 1.02m)
            {
                result.OverallSignal = "Bearish";
                result.SignalBadgeText = isMassiveVol ? "⚠️ 52W Low (>5x Vol)" : "📉 52W Low";
                result.BearishCount = 1; result.BullishCount = 0;
            }
            else
            {
                result.OverallSignal = "Neutral";
                result.SignalBadgeText = "No Breakout";
                result.BullishCount = 0; result.BearishCount = 0;
            }

            if (result.OverallSignal == "Bullish")
                result.SummaryText = $"{symbol} is showing a strong Bullish trend, hitting key highs with {(isMassiveVol ? "massive 5x" : isHighVol ? "high" : "normal")} volume.";
            else if (result.OverallSignal == "Bearish")
                result.SummaryText = $"{symbol} is showing a Bearish trend, breaking its support levels with {(isMassiveVol ? "massive 5x" : isHighVol ? "high" : "normal")} volume.";
            else
                result.SummaryText = $"{symbol} is trading in a normal range between its 52-Week High and Low.";

            return result;
        }

        #endregion

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
