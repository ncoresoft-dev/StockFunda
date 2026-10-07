using AutoMapper;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions;
using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;

namespace StockLens_BusinessLayer.Services
{
    public class StockCashflowService : IStockCashflowService
    {
        private readonly IStockRepository _stockRepository;
        private readonly IStockFinancialRepository _financialRepository;
        private readonly ICompanyRepository _companyRepository;
        private readonly IStockBalanceSheetRepository _balanceSheetRepository;
        private readonly IFinancialProvider _financialProvider;
        private readonly ISectorValuationService _sectorValuationService;
        private readonly IMapper _mapper;
        private readonly ILogger<StockCashflowService> _logger;
        private readonly IIndianApiBalanceSheetClient? _indianApiClient;
        private readonly IIndianApiRatiosClient? _indianApiRatiosClient;
        private readonly IYahooFinanceClient? _yahooFinanceClient;

        private static readonly ConcurrentDictionary<int, SemaphoreSlim> StockLocks = new();

        public StockCashflowService(
            IStockRepository stockRepository,
            IStockFinancialRepository financialRepository,
            ICompanyRepository companyRepository,
            IStockBalanceSheetRepository balanceSheetRepository,
            IFinancialProvider financialProvider,
            ISectorValuationService sectorValuationService,
            IMapper mapper,
            ILogger<StockCashflowService> logger,
            IIndianApiBalanceSheetClient? indianApiClient = null,
            IIndianApiRatiosClient? indianApiRatiosClient = null,
            IYahooFinanceClient? yahooFinanceClient = null)
        {
            _stockRepository = stockRepository;
            _financialRepository = financialRepository;
            _companyRepository = companyRepository;
            _balanceSheetRepository = balanceSheetRepository;
            _financialProvider = financialProvider;
            _sectorValuationService = sectorValuationService;
            _mapper = mapper;
            _logger = logger;
            _indianApiClient = indianApiClient;
            _indianApiRatiosClient = indianApiRatiosClient;
            _yahooFinanceClient = yahooFinanceClient;
        }

        public async Task<StockCashflowResponseDto> GetCashflowByStockIdAsync(
            int stockId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var stock = await _stockRepository.GetByIdAsync(stockId);
            if (stock == null)
            {
                throw new KeyNotFoundException($"Stock with ID {stockId} was not found.");
            }

            return await ProcessCashflowAsync(stock, forceRefresh, cancellationToken);
        }

        public async Task<StockCashflowResponseDto> GetCashflowBySymbolAsync(
            string symbol,
            string? exchange = "NSE",
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();

            var stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, cleanExchange, cancellationToken: cancellationToken);

            return await ProcessCashflowAsync(stock, forceRefresh, cancellationToken);
        }

        public async Task<StockRatiosDto?> GetRatiosBySymbolAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();

            // 1. Check if DB has cached financial records with ratios
            var stock = await _stockRepository.GetBySymbolAsync(cleanSymbol);
            if (stock != null)
            {
                var dbFinancials = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "annual", 5);
                if (dbFinancials != null && dbFinancials.Count > 0)
                {
                    var cur = dbFinancials[0];
                    var dbBs = await _balanceSheetRepository.GetRecentByStockIdAsync(stock.Id, 1);
                    var latestBsDb = dbBs.FirstOrDefault();
                    var prevDb = dbFinancials.Count > 1 ? dbFinancials[1] : null;

                    // If cached entity is missing, sync from IndianAPI (Only check if data is older than 7 days)
                    var lastSync = cur.LastSyncedAt;
                    if (dbFinancials.Count < 2 || (DateTime.UtcNow - lastSync).TotalDays > 7)
                    {
                        if (_indianApiClient != null)
                        {
                            try
                            {
                                var overview = await _indianApiClient.GetStockFinancialsAndOverviewAsync(cleanSymbol, stock.Exchange, cancellationToken);
                                if (overview != null)
                                {
                                    var annualPeriods = overview.Financials
                                        .Where(p => string.Equals(p.PeriodType, "annual", StringComparison.OrdinalIgnoreCase))
                                        .OrderByDescending(p => p.PeriodEndDate ?? DateTime.MinValue)
                                        .ToList();
                                    var prevAnn = annualPeriods.Count > 1 ? annualPeriods[1] : null;
                                    ApplyIndianApiRatiosToEntity(cur, overview, latestBsDb, prevAnn);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to refresh ratios from IndianAPI for {Symbol}", cleanSymbol);
                            }
                        }
                    }

                    // Always fetch and apply real-time live quote (Price, 52W High, 52W Low)
                    var liveQuote = await GetLiveQuoteInternalAsync(cleanSymbol, stock.Exchange, cancellationToken);
                    if (liveQuote?.Price.HasValue == true && liveQuote.Price.Value > 0)
                    {
                        cur.CurrentPrice = liveQuote.Price.Value;
                        if (liveQuote.YearHigh.HasValue) cur.Week52High = liveQuote.YearHigh;
                        if (liveQuote.YearLow.HasValue) cur.Week52Low = liveQuote.YearLow;
                        cur.LastSyncedAt = DateTime.UtcNow;
                        cur.RatiosAsOfDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                        if (cur.TtmEps.HasValue && cur.TtmEps.Value > 0)
                            cur.PeRatio = Math.Round(cur.CurrentPrice.Value / cur.TtmEps.Value, 2);
                        if (cur.BookValue.HasValue && cur.BookValue.Value > 0)
                            cur.PbRatio = Math.Round(cur.CurrentPrice.Value / cur.BookValue.Value, 2);

                        var epsGrowth = CalculatePercentageGrowth(cur.Eps, prevDb?.Eps) ?? CalculatePercentageGrowth(cur.NetProfit, prevDb?.NetProfit);
                        if (cur.PeRatio.HasValue && epsGrowth.HasValue && epsGrowth.Value > 0)
                            cur.PegRatio = Math.Round(cur.PeRatio.Value / epsGrowth.Value, 2);

                        decimal? eqCapDb = latestBsDb?.EquityCapital ?? cur.EquityCapital;
                        var (refreshedMc, refreshedShares, refreshedEqCap, refreshedMcSrc) = CalculateMarketCap(eqCapDb, cur.FaceValue, cur.CurrentPrice, cur.MarketCap);
                        cur.MarketCap = refreshedMc;
                        cur.MarketCapSource = refreshedMcSrc;
                        cur.TotalShares = refreshedShares ?? cur.TotalShares;
                    }

                    // Dynamic formula fallbacks for any still-missing metrics on cur (ZERO static hardcoding)
                    decimal? eqCapCur = latestBsDb?.EquityCapital ?? cur.EquityCapital;
                    if (!cur.FaceValue.HasValue && eqCapCur.HasValue && eqCapCur.Value > 0)
                    {
                        if (cur.TotalShares.HasValue && cur.TotalShares.Value > 0)
                        {
                            var sh = cur.TotalShares.Value;
                            cur.FaceValue = sh > 10000000 ? Math.Round((eqCapCur.Value * 10000000m) / sh, 2) : Math.Round(eqCapCur.Value / sh, 2);
                        }
                        else if (cur.CurrentPrice.HasValue && cur.MarketCap.HasValue && cur.MarketCap.Value > 0)
                        {
                            cur.FaceValue = Math.Round((eqCapCur.Value * cur.CurrentPrice.Value) / cur.MarketCap.Value, 0);
                        }
                    }

                    var (totEq, eqSrc, eqPer) = DetermineTotalEquity(null, latestBsDb);
                    var (mCap, tShares, eqCapOut, mSource) = CalculateMarketCap(eqCapCur, cur.FaceValue, cur.CurrentPrice, cur.MarketCap);

                    if (!cur.BookValue.HasValue && (totEq ?? cur.TotalEquity).HasValue)
                    {
                        var te = (totEq ?? cur.TotalEquity)!.Value;
                        if (tShares.HasValue && tShares.Value > 0)
                        {
                            cur.BookValue = tShares.Value > 10000000 ? Math.Round((te * 10000000m) / tShares.Value, 2) : Math.Round(te / tShares.Value, 2);
                        }
                        else if (eqCapOut.HasValue && cur.FaceValue.HasValue && cur.FaceValue.Value > 0)
                        {
                            var shCr = eqCapOut.Value / cur.FaceValue.Value;
                            if (shCr > 0) cur.BookValue = Math.Round(te / shCr, 2);
                        }
                    }

                    if (!cur.Roe.HasValue && cur.NetProfit.HasValue && (totEq ?? cur.TotalEquity).HasValue && (totEq ?? cur.TotalEquity)!.Value > 0)
                    {
                        cur.Roe = Math.Round((cur.NetProfit.Value / (totEq ?? cur.TotalEquity)!.Value) * 100m, 2);
                    }

                    if (!cur.Roce.HasValue)
                    {
                        decimal? ebit = cur.ProfitBeforeTax.HasValue ? (cur.ProfitBeforeTax.Value + (cur.Interest ?? 0)) : (cur.OperatingProfit.HasValue ? cur.OperatingProfit.Value + (cur.OtherIncome ?? 0) : null);
                        decimal? capEmp = (totEq ?? cur.TotalEquity).HasValue ? ((totEq ?? cur.TotalEquity)!.Value + (latestBsDb?.Borrowings ?? 0)) : null;
                        if (ebit.HasValue && capEmp.HasValue && capEmp.Value > 0) cur.Roce = Math.Round((ebit.Value / capEmp.Value) * 100m, 2);
                    }

                    if (!cur.PeRatio.HasValue && cur.CurrentPrice.HasValue && cur.CurrentPrice.Value > 0)
                    {
                        var eps = cur.TtmEps ?? cur.Eps;
                        if (eps.HasValue && eps.Value > 0) cur.PeRatio = Math.Round(cur.CurrentPrice.Value / eps.Value, 2);
                        else if (mCap.HasValue && cur.NetProfit.HasValue && cur.NetProfit.Value > 0) cur.PeRatio = Math.Round(mCap.Value / cur.NetProfit.Value, 2);
                    }

                    if (!cur.PbRatio.HasValue && cur.CurrentPrice.HasValue && cur.CurrentPrice.Value > 0 && cur.BookValue.HasValue && cur.BookValue.Value > 0)
                    {
                        cur.PbRatio = Math.Round(cur.CurrentPrice.Value / cur.BookValue.Value, 2);
                    }

                    if (cur.PeRatio.HasValue && (!cur.PegRatio.HasValue || cur.PegRatio == 0))
                    {
                        var epsGrowth = CalculatePercentageGrowth(cur.Eps, prevDb?.Eps) ?? CalculatePercentageGrowth(cur.NetProfit, prevDb?.NetProfit);
                        if (epsGrowth.HasValue && epsGrowth.Value > 0)
                            cur.PegRatio = Math.Round(cur.PeRatio.Value / epsGrowth.Value, 2);
                    }

                    try
                    {
                        await _financialRepository.UpdateAsync(cur);
                        await _financialRepository.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist updated ratios to DB for {Symbol}", cleanSymbol);
                    }

                    decimal? prevRoe = prevDb?.Roe;
                    if (!prevRoe.HasValue && prevDb?.NetProfit.HasValue == true && (prevDb.TotalEquity.HasValue && prevDb.TotalEquity.Value > 0))
                    {
                        prevRoe = Math.Round((prevDb.NetProfit.Value / prevDb.TotalEquity.Value) * 100m, 2);
                    }

                    decimal? prevRoce = prevDb?.Roce;
                    if (!prevRoce.HasValue && prevDb != null)
                    {
                        decimal? prevEbit = prevDb.ProfitBeforeTax.HasValue ? (prevDb.ProfitBeforeTax.Value + (prevDb.Interest ?? 0)) : (prevDb.OperatingProfit.HasValue ? prevDb.OperatingProfit.Value + (prevDb.OtherIncome ?? 0) : null);
                        decimal? prevCapEmp = prevDb.TotalEquity.HasValue ? (prevDb.TotalEquity.Value + (latestBsDb?.Borrowings ?? 0)) : null;
                        if (prevEbit.HasValue && prevCapEmp.HasValue && prevCapEmp.Value > 0)
                            prevRoce = Math.Round((prevEbit.Value / prevCapEmp.Value) * 100m, 2);
                    }

                    return new StockRatiosDto
                    {
                        Roe = cur.Roe,
                        RoeYoY = CalculatePercentageGrowth(cur.Roe, prevRoe),
                        RoeChange = (cur.Roe.HasValue && prevRoe.HasValue) ? Math.Round(cur.Roe.Value - prevRoe.Value, 2) : null,
                        RoePrevYear = prevRoe,
                        Roce = cur.Roce,
                        RoceYoY = CalculatePercentageGrowth(cur.Roce, prevRoce),
                        RoceChange = (cur.Roce.HasValue && prevRoce.HasValue) ? Math.Round(cur.Roce.Value - prevRoce.Value, 2) : null,
                        RocePrevYear = prevRoce,
                        PeRatio = cur.PeRatio,
                        TtmEps = cur.TtmEps,
                        PbRatio = cur.PbRatio,
                        DividendYield = cur.DividendYield,
                        Week52High = cur.Week52High,
                        Week52Low = cur.Week52Low,
                        CurrentPrice = cur.CurrentPrice,
                        AsOfDate = cur.RatiosAsOfDate,
                        FinancialsFiscalYear = cur.FiscalYear,
                        FinancialsPeriodType = cur.PeriodType,
                        FaceValue = cur.FaceValue,
                        EquityCapital = eqCapOut,
                        TotalShares = tShares,
                        TotalEquity = totEq ?? cur.TotalEquity,
                        TotalEquityPeriod = eqPer ?? cur.FiscalYear,
                        TotalEquitySource = eqSrc ?? cur.TotalEquitySource,
                        BookValue = cur.BookValue,
                        MarketCap = mCap,
                        MarketCapSource = mSource,
                        SectorPeAsOfDate = cur.RatiosAsOfDate,
                        PegRatio = cur.PegRatio,
                        DebtorDays = cur.DebtorDays,
                        DebtorDaysYoY = CalculatePercentageGrowth(cur.DebtorDays, prevDb?.DebtorDays),
                        InventoryDays = cur.InventoryDays,
                        InventoryDaysYoY = CalculatePercentageGrowth(cur.InventoryDays, prevDb?.InventoryDays),
                        PayableDays = cur.PayableDays,
                        PayableDaysYoY = CalculatePercentageGrowth(cur.PayableDays, prevDb?.PayableDays)
                    };
                }
            }

            // 2. Primary: Try IndianAPI if available
            if (_indianApiClient != null)
            {
                try
                {
                    var indianData = await _indianApiClient.GetStockFinancialsAndOverviewAsync(cleanSymbol, "NSE", cancellationToken);
                    if (indianData != null)
                    {
                        StockBalanceSheet? latestBsDb = null;
                        if (stock != null)
                        {
                            var dbBs = await _balanceSheetRepository.GetRecentByStockIdAsync(stock.Id, 1);
                            latestBsDb = dbBs.FirstOrDefault();
                        }
                        decimal? eqCap = latestBsDb?.EquityCapital;

                        // Check live quote for real-time price & 52W high/low
                        var liveQuote = await GetLiveQuoteInternalAsync(cleanSymbol, stock?.Exchange ?? "NSE", cancellationToken);
                        if (liveQuote?.Price.HasValue == true && liveQuote.Price.Value > 0)
                        {
                            indianData.CurrentPrice = liveQuote.Price.Value;
                            if (liveQuote.YearHigh.HasValue) indianData.YearHigh = liveQuote.YearHigh;
                            if (liveQuote.YearLow.HasValue) indianData.YearLow = liveQuote.YearLow;
                        }

                        // Derive Face Value if missing
                        if (!indianData.FaceValue.HasValue && eqCap.HasValue && eqCap.Value > 0)
                        {
                            if (indianData.Financials.FirstOrDefault()?.TotalShares.HasValue == true && indianData.Financials.First().TotalShares!.Value > 0)
                            {
                                var sh = indianData.Financials.First().TotalShares!.Value;
                                indianData.FaceValue = sh > 10000000 ? Math.Round((eqCap.Value * 10000000m) / sh, 2) : Math.Round(eqCap.Value / sh, 2);
                            }
                            else if (indianData.CurrentPrice.HasValue && indianData.MarketCap.HasValue && indianData.MarketCap.Value > 0)
                            {
                                indianData.FaceValue = Math.Round((eqCap.Value * indianData.CurrentPrice.Value) / indianData.MarketCap.Value, 0);
                            }
                        }

                        var (marketCap, totalShares, eqCapVal, mcSource) = CalculateMarketCap(eqCap, indianData.FaceValue, indianData.CurrentPrice, indianData.MarketCap);

                        var latestAnnual = indianData.Financials.FirstOrDefault(f => f.PeriodType == "annual") ?? indianData.Financials.FirstOrDefault();

                        // Dynamic Book Value calculation if missing
                        if (!indianData.BookValue.HasValue && latestAnnual?.TotalEquity.HasValue == true)
                        {
                            var te = latestAnnual.TotalEquity.Value;
                            if (totalShares.HasValue && totalShares.Value > 0)
                            {
                                indianData.BookValue = totalShares.Value > 10000000 ? Math.Round((te * 10000000m) / totalShares.Value, 2) : Math.Round(te / totalShares.Value, 2);
                            }
                            else if ((eqCapVal ?? eqCap).HasValue && indianData.FaceValue.HasValue && indianData.FaceValue.Value > 0)
                            {
                                var shCr = (eqCapVal ?? eqCap)!.Value / indianData.FaceValue.Value;
                                if (shCr > 0) indianData.BookValue = Math.Round(te / shCr, 2);
                            }
                        }

                        // Dynamic ROE calculation if missing
                        if (!indianData.Roe.HasValue && latestAnnual?.NetProfit.HasValue == true && latestAnnual.TotalEquity.HasValue && latestAnnual.TotalEquity.Value > 0)
                        {
                            indianData.Roe = Math.Round((latestAnnual.NetProfit.Value / latestAnnual.TotalEquity.Value) * 100m, 2);
                        }

                        // Dynamic ROCE calculation if missing
                        if (!indianData.Roce.HasValue && latestAnnual != null)
                        {
                            decimal? ebit = latestAnnual.ProfitBeforeTax.HasValue ? (latestAnnual.ProfitBeforeTax.Value + (latestAnnual.Interest ?? 0)) : (latestAnnual.OperatingProfit.HasValue ? latestAnnual.OperatingProfit.Value + (latestAnnual.OtherIncome ?? 0) : null);
                            decimal? capEmp = latestAnnual.TotalEquity.HasValue ? (latestAnnual.TotalEquity.Value + (latestBsDb?.Borrowings ?? latestAnnual.TotalDebt ?? 0)) : null;
                            if (ebit.HasValue && capEmp.HasValue && capEmp.Value > 0) indianData.Roce = Math.Round((ebit.Value / capEmp.Value) * 100m, 2);
                        }

                        decimal? computedPe = null;
                        var eps = indianData.TtmEps ?? latestAnnual?.Eps;
                        if (eps.HasValue && eps.Value > 0 && indianData.CurrentPrice.HasValue && indianData.CurrentPrice.Value > 0)
                        {
                            computedPe = Math.Round(indianData.CurrentPrice.Value / eps.Value, 2);
                        }
                        else if (marketCap.HasValue && latestAnnual?.NetProfit.HasValue == true && latestAnnual.NetProfit.Value > 0)
                        {
                            computedPe = Math.Round(marketCap.Value / latestAnnual.NetProfit.Value, 2);
                        }
                        else
                        {
                            computedPe = indianData.PeRatio;
                        }

                        decimal? computedPb = null;
                        if (indianData.CurrentPrice.HasValue && indianData.CurrentPrice.Value > 0 && indianData.BookValue.HasValue && indianData.BookValue.Value > 0)
                        {
                            computedPb = Math.Round(indianData.CurrentPrice.Value / indianData.BookValue.Value, 2);
                        }
                        else
                        {
                            computedPb = indianData.PbRatio;
                        }

                        decimal? sectorPe = indianData.SectorPe;
                        string? sectorName = indianData.SectorName ?? indianData.Industry ?? stock?.Company?.Industry;
                        string? sectorAsOf = null;

                        if (!sectorPe.HasValue && !string.IsNullOrWhiteSpace(sectorName))
                        {
                            try
                            {
                                var secVal = await _sectorValuationService.GetSectorValuationAsync(sectorName, "NSE", cancellationToken);
                                if (secVal?.SectorPe.HasValue == true)
                                {
                                    sectorPe = secVal.SectorPe;
                                    sectorName = secVal.Sector ?? sectorName;
                                    sectorAsOf = secVal.AsOfDate;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to compute fallback sector valuation for {Sector}", sectorName);
                            }
                        }

                        var (totEq, eqSrc, eqPer) = DetermineTotalEquity(null, latestBsDb);

                        decimal? debtorDays = null, inventoryDays = null, payableDays = null;
                        if (latestAnnual != null)
                        {
                            if (latestAnnual.TradeReceivables.HasValue && latestAnnual.TradeReceivables.Value > 0 && latestAnnual.Revenue.HasValue && latestAnnual.Revenue.Value > 0)
                            {
                                debtorDays = Math.Round((latestAnnual.TradeReceivables.Value / latestAnnual.Revenue.Value) * 365m, 1);
                            }

                            var cogs = latestAnnual.Expenses ?? (latestAnnual.Revenue.HasValue && latestAnnual.OperatingProfit.HasValue ? latestAnnual.Revenue.Value - latestAnnual.OperatingProfit.Value : null);
                            if (cogs.HasValue && cogs.Value > 0)
                            {
                                if (latestAnnual.TotalInventory.HasValue && latestAnnual.TotalInventory.Value > 0)
                                {
                                    inventoryDays = Math.Round((latestAnnual.TotalInventory.Value / cogs.Value) * 365m, 1);
                                }

                                if (latestAnnual.AccountsPayable.HasValue && latestAnnual.AccountsPayable.Value > 0)
                                {
                                    payableDays = Math.Round((latestAnnual.AccountsPayable.Value / cogs.Value) * 365m, 1);
                                }
                            }
                        }

                        var annualPeriods = indianData.Financials
                            .Where(f => string.Equals(f.PeriodType, "annual", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => f.PeriodEndDate ?? DateTime.MinValue)
                            .ToList();
                        var prevAnnual = annualPeriods.Count > 1 ? annualPeriods[1] : null;

                        decimal? prevRoeInd = null;
                        if (prevAnnual?.NetProfit.HasValue == true && prevAnnual.TotalEquity.HasValue && prevAnnual.TotalEquity.Value > 0)
                        {
                            prevRoeInd = Math.Round((prevAnnual.NetProfit.Value / prevAnnual.TotalEquity.Value) * 100m, 2);
                        }

                        decimal? prevRoceInd = null;
                        if (prevAnnual != null)
                        {
                            decimal? ebit = prevAnnual.ProfitBeforeTax.HasValue ? (prevAnnual.ProfitBeforeTax.Value + (prevAnnual.Interest ?? 0)) : (prevAnnual.OperatingProfit.HasValue ? prevAnnual.OperatingProfit.Value + (prevAnnual.OtherIncome ?? 0) : null);
                            decimal? capEmp = prevAnnual.TotalEquity.HasValue ? (prevAnnual.TotalEquity.Value + (latestBsDb?.Borrowings ?? prevAnnual.TotalDebt ?? 0)) : null;
                            if (ebit.HasValue && capEmp.HasValue && capEmp.Value > 0) prevRoceInd = Math.Round((ebit.Value / capEmp.Value) * 100m, 2);
                        }

                        return new StockRatiosDto
                        {
                            Roe = indianData.Roe,
                            RoeYoY = CalculatePercentageGrowth(indianData.Roe, prevRoeInd),
                            RoeChange = (indianData.Roe.HasValue && prevRoeInd.HasValue) ? Math.Round(indianData.Roe.Value - prevRoeInd.Value, 2) : null,
                            RoePrevYear = prevRoeInd,
                            Roce = indianData.Roce,
                            RoceYoY = CalculatePercentageGrowth(indianData.Roce, prevRoceInd),
                            RoceChange = (indianData.Roce.HasValue && prevRoceInd.HasValue) ? Math.Round(indianData.Roce.Value - prevRoceInd.Value, 2) : null,
                            RocePrevYear = prevRoceInd,
                            PeRatio = computedPe,
                            TtmEps = indianData.TtmEps,
                            PbRatio = computedPb,
                            DividendYield = indianData.DividendYield,
                            Week52High = indianData.YearHigh,
                            Week52Low = indianData.YearLow,
                            CurrentPrice = indianData.CurrentPrice,
                            FinancialsFiscalYear = indianData.Financials.FirstOrDefault()?.FiscalYear,
                            FinancialsPeriodType = indianData.Financials.FirstOrDefault()?.PeriodType ?? "annual",
                            FaceValue = indianData.FaceValue,
                            EquityCapital = eqCapVal ?? eqCap,
                            TotalShares = totalShares,
                            TotalEquity = totEq ?? latestAnnual?.TotalEquity,
                            TotalEquityPeriod = eqPer ?? latestAnnual?.FiscalYear,
                            TotalEquitySource = eqSrc ?? "Reported",
                            BookValue = indianData.BookValue,
                            MarketCap = marketCap,
                            MarketCapSource = mcSource,
                            SectorPe = sectorPe,
                            SectorPeSector = sectorName,
                            SectorPeAsOfDate = sectorAsOf,
                            DebtorDays = debtorDays,
                            InventoryDays = inventoryDays,
                            PayableDays = payableDays
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch ratios directly from IndianAPI for {Symbol}. Attempting fallback.", cleanSymbol);
                }
            }

            // 3. Fallback: Backup Provider
            BharatStockRatiosRecord? ratios = null;
            BharatStockCompanyDetailsRecord? details = null;
            BharatStockScreenerRecord? screener = null;
            IReadOnlyList<BharatStockFinancialRecord>? financials = null;
            decimal? livePrice = null;

            try
            {
                ratios = await _financialProvider.GetRatiosAsync(cleanSymbol, cancellationToken);
                details = await _financialProvider.GetStockDetailsAsync(cleanSymbol, "NSE", cancellationToken);
                screener = await _financialProvider.GetScreenerDataAsync(cleanSymbol, "NSE", cancellationToken);
                financials = await _financialProvider.GetFinancialsAsync(cleanSymbol, "annual", 1, 1, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backup provider ratios fetch failed for {Symbol}.", cleanSymbol);
            }

            if (ratios == null && details == null && screener == null && (financials == null || financials.Count == 0) && livePrice == null) return null;

            var latestFinancial = financials != null && financials.Count > 0 ? financials[0] : null;
            StockBalanceSheet? latestBs = null;
            if (stock != null)
            {
                var dbBs = await _balanceSheetRepository.GetRecentByStockIdAsync(stock.Id, 1);
                latestBs = dbBs.FirstOrDefault();
            }
            var (totalEquity, equitySource, equityPeriod) = DetermineTotalEquity(latestFinancial, latestBs);

            SectorValuationResultDto? sectorValuation = null;
            var sector = details?.Sector ?? screener?.Sector;
            if (!string.IsNullOrWhiteSpace(sector))
            {
                try
                {
                    sectorValuation = await _sectorValuationService.GetSectorValuationAsync(sector, "NSE", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to compute sector valuation for {Sector}.", sector);
                }
            }

            var currentPrice = livePrice ?? ratios?.Price ?? details?.LatestPrice?.Close ?? screener?.Price;
            var faceValue = details?.FaceValue;
            var equityCapital = latestBs?.EquityCapital;
            var (marketCapFallback, totalSharesFallback, eqCapFallback, mcSourceFallback) = CalculateMarketCap(equityCapital, faceValue, currentPrice, screener?.MarketCap);

            return new StockRatiosDto
            {
                Roe = ratios?.Roe ?? screener?.Roe,
                Roce = ratios?.Roce ?? screener?.Roce,
                PeRatio = ratios?.PeRatio ?? screener?.PeRatio,
                PbRatio = ratios?.PbRatio ?? screener?.PbRatio,
                DividendYield = ratios?.DividendYield,
                Week52High = ratios?.Week52High,
                Week52Low = ratios?.Week52Low,
                CurrentPrice = currentPrice,
                AsOfDate = ratios?.AsOfDate ?? details?.LatestPrice?.TradeDate ?? screener?.ComputedAt,
                FinancialsFiscalYear = ratios?.FinancialsFiscalYear ?? latestFinancial?.ResolvedFiscalYear,
                FinancialsPeriodType = ratios?.FinancialsPeriodType ?? latestFinancial?.PeriodType,
                FaceValue = faceValue,
                EquityCapital = eqCapFallback,
                TotalShares = totalSharesFallback,
                TotalEquity = totalEquity,
                TotalEquityPeriod = equityPeriod,
                TotalEquitySource = equitySource,
                BookValue = screener?.ResolvedBookValue,
                MarketCap = marketCapFallback,
                MarketCapSource = mcSourceFallback,
                SectorPe = sectorValuation?.SectorPe,
                SectorPeSector = sectorValuation?.Sector ?? sector,
                SectorPeAsOfDate = sectorValuation?.AsOfDate
            };
        }

        private async Task<StockCashflowResponseDto> ProcessCashflowAsync(
            Stock stock,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var existingEntities = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "annual", 5);

            bool isFresh = false;
            if (existingEntities.Count > 0)
            {
                var lastSync = existingEntities.Max(e => e.LastSyncedAt);
                isFresh = (DateTime.UtcNow - lastSync).TotalDays < 7;
            }

            if (!forceRefresh && existingEntities.Count > 0 && isFresh)
            {
                _logger.LogInformation("Serving {Count} annual financial records from DB cache for stock {Symbol} (StockId: {StockId}, LastSynced: {LastSynced}).",
                    existingEntities.Count, stock.Symbol, stock.Id, existingEntities.Max(e => e.LastSyncedAt));

                var cur = existingEntities[0];
                var dbBs = await _balanceSheetRepository.GetRecentByStockIdAsync(stock.Id, 1);
                var latestBsDb = dbBs.FirstOrDefault();

                if (!cur.Interest.HasValue || !cur.Depreciation.HasValue)
                {
                    await EnsureInterestAndDepreciationPopulatedAsync(stock, cur, cancellationToken);
                }

                // 1. If cached entity doesn't have basic balance sheet metrics yet, sync fundamentals from IndianAPI
                if (cur.Roe == null ||
                    cur.FaceValue == null ||
                    cur.MarketCap == null ||
                    cur.BookValue == null ||
                    cur.Roce == null ||
                    cur.SectorPe == null ||
                    existingEntities.Count < 2)
                {
                    try
                    {
                        if (_indianApiClient != null)
                        {
                            var overview = await _indianApiClient.GetStockFinancialsAndOverviewAsync(stock.Symbol, stock.Exchange, cancellationToken);
                            if (overview != null)
                            {
                                var annualPeriods = overview.Financials
                                    .Where(p => string.Equals(p.PeriodType, "annual", StringComparison.OrdinalIgnoreCase))
                                    .OrderByDescending(p => p.PeriodEndDate ?? DateTime.MinValue)
                                    .ToList();
                                var prevAnn = annualPeriods.Count > 1 ? annualPeriods[1] : null;
                                ApplyIndianApiRatiosToEntity(cur, overview, latestBsDb, prevAnn);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not lazily populate metrics for {Symbol}.", stock.Symbol);
                    }
                }

                // Efficiency ratios are now fetched during the main sync cycle (SyncFromIndianApiAsync).
                // To avoid hammering the API on every cache hit for stocks that genuinely lack ratio data,
                // we do not lazily fetch them here. If missing, a full sync (forceRefresh=true) will retrieve them.
                var prevEntity = existingEntities.Count > 1 ? existingEntities[1] : null;

                // 2. ALWAYS fetch and apply real-time live price & 52W High/Low from Yahoo Finance
                try
                {
                    var liveQuote = await GetLiveQuoteInternalAsync(stock.Symbol, stock.Exchange, cancellationToken);
                    if (liveQuote?.Price.HasValue == true && liveQuote.Price.Value > 0)
                    {
                        cur.CurrentPrice = liveQuote.Price.Value;
                        if (liveQuote.YearHigh.HasValue) cur.Week52High = liveQuote.YearHigh;
                        if (liveQuote.YearLow.HasValue) cur.Week52Low = liveQuote.YearLow;
                        cur.LastSyncedAt = DateTime.UtcNow;
                        cur.RatiosAsOfDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                        if (cur.TtmEps.HasValue && cur.TtmEps.Value > 0)
                            cur.PeRatio = Math.Round(cur.CurrentPrice.Value / cur.TtmEps.Value, 2);
                        if (cur.BookValue.HasValue && cur.BookValue.Value > 0)
                            cur.PbRatio = Math.Round(cur.CurrentPrice.Value / cur.BookValue.Value, 2);

                        prevEntity = existingEntities.Count > 1 ? existingEntities[1] : null;
                        var epsGrowth = CalculatePercentageGrowth(cur.Eps, prevEntity?.Eps) ?? CalculatePercentageGrowth(cur.NetProfit, prevEntity?.NetProfit);
                        if (cur.PeRatio.HasValue && epsGrowth.HasValue && epsGrowth.Value > 0)
                            cur.PegRatio = Math.Round(cur.PeRatio.Value / epsGrowth.Value, 2);

                        decimal? eqCapDb = latestBsDb?.EquityCapital ?? cur.EquityCapital;
                        var (refreshedMc, refreshedShares, refreshedEqCap, refreshedMcSrc) = CalculateMarketCap(eqCapDb, cur.FaceValue, cur.CurrentPrice, cur.MarketCap);
                        cur.MarketCap = refreshedMc;
                        cur.MarketCapSource = refreshedMcSrc;
                        cur.TotalShares = refreshedShares ?? cur.TotalShares;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh real-time live price for {Symbol}", stock.Symbol);
                }

                // 3. Dynamic formula fallbacks for any still-missing metrics on cur (ZERO static hardcoding)
                decimal? eqCapCur = latestBsDb?.EquityCapital ?? cur.EquityCapital;
                if (!cur.FaceValue.HasValue && eqCapCur.HasValue && eqCapCur.Value > 0)
                {
                    if (cur.TotalShares.HasValue && cur.TotalShares.Value > 0)
                    {
                        var sh = cur.TotalShares.Value;
                        cur.FaceValue = sh > 10000000 ? Math.Round((eqCapCur.Value * 10000000m) / sh, 2) : Math.Round(eqCapCur.Value / sh, 2);
                    }
                    else if (cur.CurrentPrice.HasValue && cur.MarketCap.HasValue && cur.MarketCap.Value > 0)
                    {
                        cur.FaceValue = Math.Round((eqCapCur.Value * cur.CurrentPrice.Value) / cur.MarketCap.Value, 0);
                    }
                }

                var (totEq, eqSrc, eqPer) = DetermineTotalEquity(null, latestBsDb);
                var (mCap, tShares, eqCapOut, mSource) = CalculateMarketCap(eqCapCur, cur.FaceValue, cur.CurrentPrice, cur.MarketCap);
                if (mCap.HasValue)
                {
                    cur.MarketCap = mCap;
                    cur.MarketCapSource = mSource;
                    cur.TotalShares = tShares ?? cur.TotalShares;
                    cur.EquityCapital = eqCapOut ?? cur.EquityCapital;
                }

                if (!cur.BookValue.HasValue && (totEq ?? cur.TotalEquity).HasValue)
                {
                    var te = (totEq ?? cur.TotalEquity)!.Value;
                    if (tShares.HasValue && tShares.Value > 0)
                    {
                        cur.BookValue = tShares.Value > 10000000 ? Math.Round((te * 10000000m) / tShares.Value, 2) : Math.Round(te / tShares.Value, 2);
                    }
                    else if (eqCapOut.HasValue && cur.FaceValue.HasValue && cur.FaceValue.Value > 0)
                    {
                        var shCr = eqCapOut.Value / cur.FaceValue.Value;
                        if (shCr > 0) cur.BookValue = Math.Round(te / shCr, 2);
                    }
                }

                if (!cur.Roe.HasValue && cur.NetProfit.HasValue && (totEq ?? cur.TotalEquity).HasValue && (totEq ?? cur.TotalEquity)!.Value > 0)
                {
                    cur.Roe = Math.Round((cur.NetProfit.Value / (totEq ?? cur.TotalEquity)!.Value) * 100m, 2);
                }

                if (!cur.Roce.HasValue)
                {
                    decimal? ebit = cur.ProfitBeforeTax.HasValue ? (cur.ProfitBeforeTax.Value + (cur.Interest ?? 0)) : (cur.OperatingProfit.HasValue ? cur.OperatingProfit.Value + (cur.OtherIncome ?? 0) : null);
                    decimal? capEmp = (totEq ?? cur.TotalEquity).HasValue ? ((totEq ?? cur.TotalEquity)!.Value + (latestBsDb?.Borrowings ?? 0)) : null;
                    if (ebit.HasValue && capEmp.HasValue && capEmp.Value > 0) cur.Roce = Math.Round((ebit.Value / capEmp.Value) * 100m, 2);
                }

                if (!cur.PeRatio.HasValue && cur.CurrentPrice.HasValue && cur.CurrentPrice.Value > 0)
                {
                    var eps = cur.TtmEps ?? cur.Eps;
                    if (eps.HasValue && eps.Value > 0) cur.PeRatio = Math.Round(cur.CurrentPrice.Value / eps.Value, 2);
                    else if (mCap.HasValue && cur.NetProfit.HasValue && cur.NetProfit.Value > 0) cur.PeRatio = Math.Round(mCap.Value / cur.NetProfit.Value, 2);
                }

                if (!cur.PbRatio.HasValue && cur.CurrentPrice.HasValue && cur.CurrentPrice.Value > 0 && cur.BookValue.HasValue && cur.BookValue.Value > 0)
                {
                    cur.PbRatio = Math.Round(cur.CurrentPrice.Value / cur.BookValue.Value, 2);
                }

                if (cur.PeRatio.HasValue && (!cur.PegRatio.HasValue || cur.PegRatio == 0))
                {
                    prevEntity = existingEntities.Count > 1 ? existingEntities[1] : null;
                    var epsGrowth = CalculatePercentageGrowth(cur.Eps, prevEntity?.Eps) ?? CalculatePercentageGrowth(cur.NetProfit, prevEntity?.NetProfit);
                    if (epsGrowth.HasValue && epsGrowth.Value > 0)
                        cur.PegRatio = Math.Round(cur.PeRatio.Value / epsGrowth.Value, 2);
                }

                try
                {
                    await _financialRepository.UpdateAsync(cur);
                    await _financialRepository.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist updated ratios to DB for {Symbol}", stock.Symbol);
                }

                return await BuildResponseDtoAsync(stock, existingEntities, cancellationToken);
            }

            // Acquire per-stock lock to prevent thundering herd API calls
            var stockLock = StockLocks.GetOrAdd(stock.Id, _ => new SemaphoreSlim(1, 1));
            await stockLock.WaitAsync(cancellationToken);

            try
            {
                if (!forceRefresh)
                {
                    existingEntities = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "annual", 5);
                    if (existingEntities.Count > 0)
                    {
                        var lastSync = existingEntities.Max(e => e.LastSyncedAt);
                        if ((DateTime.UtcNow - lastSync).TotalDays < 7)
                        {
                            return await BuildResponseDtoAsync(stock, existingEntities, cancellationToken);
                        }
                    }
                }

                _logger.LogInformation("Syncing annual financials and ratios from BharatStock for stock {Symbol} (StockId: {StockId}, ForceRefresh: {ForceRefresh}).",
                    stock.Symbol, stock.Id, forceRefresh);

                await SyncFinancialsFromProviderAsync(stock, cancellationToken);

                existingEntities = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "annual", 5);
                if (existingEntities.Count == 0)
                {
                    throw new ProviderNotFoundException(stock.Symbol, $"Annual financial and cashflow data is not available for {stock.Symbol}.");
                }

                return await BuildResponseDtoAsync(stock, existingEntities, cancellationToken);
            }
            finally
            {
                stockLock.Release();
            }
        }

        private async Task SyncFinancialsFromProviderAsync(Stock stock, CancellationToken cancellationToken)
        {
            // 1. Primary: Try IndianAPI (Single unified call for CAS, INC, BAL, valuation metrics)
            bool indianApiSuccess = false;
            try
            {
                indianApiSuccess = await SyncFromIndianApiAsync(stock, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Primary IndianAPI sync encountered an error for stock {Symbol}. Attempting BharatStock fallback.", stock.Symbol);
                indianApiSuccess = false;
            }

            if (indianApiSuccess)
            {
                _logger.LogInformation("Successfully synced financial statements and ratios from IndianAPI for {Symbol}.", stock.Symbol);
                return;
            }

            // 2. Fallback: BharatStock
            _logger.LogInformation("Syncing annual financials and ratios from BharatStock fallback for stock {Symbol}.", stock.Symbol);
            await SyncFromBharatStockAsync(stock, cancellationToken);
        }

        private async Task<bool> SyncFromIndianApiAsync(Stock stock, CancellationToken cancellationToken)
        {
            if (_indianApiClient == null)
            {
                return false;
            }

            _logger.LogInformation("Attempting primary financial & cashflow sync from IndianAPI for {Symbol}", stock.Symbol);
            var dataTask = _indianApiClient.GetStockFinancialsAndOverviewAsync(stock.Symbol, stock.Exchange, cancellationToken);
            var liveQuoteTask = GetLiveQuoteInternalAsync(stock.Symbol, stock.Exchange, cancellationToken);
            
            async Task<IReadOnlyList<StockLens_Infrastructure.ExternalServices.IndianApi.Models.IndianApiNormalizedRatioRecord>?> SafeGetRatiosAsync()
            {
                if (_indianApiRatiosClient == null) return null;
                try { return await _indianApiRatiosClient.GetRatiosAsync(stock.Symbol, cancellationToken); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to fetch ratios for {Symbol}", stock.Symbol); return null; }
            }

            var data = await dataTask;
            var liveQuote = await liveQuoteTask;
            var ratiosData = await SafeGetRatiosAsync();

            if (data == null || data.Financials.Count == 0)
            {
                _logger.LogWarning("IndianAPI returned no financial periods for {Symbol}.", stock.Symbol);
                return false;
            }

            var now = DateTime.UtcNow;
            var balanceSheets = await _balanceSheetRepository.GetRecentByStockIdAsync(stock.Id, 5);

            if (!data.SectorPe.HasValue)
            {
                var sectorName = data.SectorName ?? data.Industry ?? stock.Company?.Industry;
                if (!string.IsNullOrWhiteSpace(sectorName))
                {
                    try
                    {
                        var secVal = await _sectorValuationService.GetSectorValuationAsync(sectorName, stock.Exchange, cancellationToken);
                        if (secVal?.SectorPe.HasValue == true)
                        {
                            data.SectorPe = secVal.SectorPe;
                            data.SectorName = secVal.Sector ?? sectorName;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to compute fallback sector valuation for {Sector}", sectorName);
                    }
                }
            }

            // Apply real-time live price & 52-week range from Yahoo Finance market feed
            if (liveQuote?.Price.HasValue == true && liveQuote.Price.Value > 0)
            {
                data.CurrentPrice = liveQuote.Price.Value;
                if (liveQuote.YearHigh.HasValue) data.YearHigh = liveQuote.YearHigh;
                if (liveQuote.YearLow.HasValue) data.YearLow = liveQuote.YearLow;
            }

            var annualPeriods = data.Financials
                .Where(p => string.Equals(p.PeriodType, "annual", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.PeriodEndDate ?? DateTime.MinValue)
                .ToList();

            var quarterlyPeriods = data.Financials
                .Where(p => string.Equals(p.PeriodType, "quarterly", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.PeriodEndDate ?? DateTime.MinValue)
                .ToList();

            var periodsToSave = annualPeriods.Take(5).Concat(quarterlyPeriods.Take(12)).ToList();
            if (periodsToSave.Count == 0)
            {
                periodsToSave = data.Financials.Take(10).ToList();
            }

            var latestAnnual = annualPeriods.FirstOrDefault() ?? periodsToSave.FirstOrDefault(p => string.Equals(p.PeriodType, "annual", StringComparison.OrdinalIgnoreCase)) ?? periodsToSave.FirstOrDefault();
            var prevAnnual = annualPeriods.Count > 1 ? annualPeriods[1] : null;

            var existingEntitiesForStock = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "all", 50);

            for (int i = 0; i < periodsToSave.Count; i++)
            {
                var period = periodsToSave[i];
                var periodKey = period.PeriodKey;
                if (string.IsNullOrWhiteSpace(periodKey))
                {
                    continue;
                }

                var cfo = period.OperatingCashFlow;
                var capex = period.Capex;
                var fcf = period.FreeCashFlow;

                decimal? dDays = null, iDays = null, pDays = null;
                if (ratiosData != null && string.Equals(period.PeriodType, "annual", StringComparison.OrdinalIgnoreCase))
                {
                    var pYearStr = period.PeriodEndDate?.ToString("yyyy") ?? period.FiscalYear?.Replace("FY", "20");
                    var matchedRatio = ratiosData.FirstOrDefault(r => r.PeriodDate?.ToString("yyyy") == pYearStr);
                    if (matchedRatio != null)
                    {
                        dDays = matchedRatio.DebtorDays;
                        iDays = matchedRatio.InventoryDays;
                        pDays = matchedRatio.PayableDays;
                    }
                }

                // Dynamic Mathematical Derivation Fallback from raw balance sheet & P&L
                if (string.Equals(period.PeriodType, "annual", StringComparison.OrdinalIgnoreCase))
                {
                    if (!dDays.HasValue && period.TradeReceivables.HasValue && period.TradeReceivables.Value > 0 && period.Revenue.HasValue && period.Revenue.Value > 0)
                    {
                        dDays = Math.Round((period.TradeReceivables.Value / period.Revenue.Value) * 365m, 1);
                    }

                    var cogs = period.Expenses ?? (period.Revenue.HasValue && period.OperatingProfit.HasValue ? period.Revenue.Value - period.OperatingProfit.Value : null);
                    if (cogs.HasValue && cogs.Value > 0)
                    {
                        if (!iDays.HasValue && period.TotalInventory.HasValue && period.TotalInventory.Value > 0)
                        {
                            iDays = Math.Round((period.TotalInventory.Value / cogs.Value) * 365m, 1);
                        }

                        if (!pDays.HasValue && period.AccountsPayable.HasValue && period.AccountsPayable.Value > 0)
                        {
                            pDays = Math.Round((period.AccountsPayable.Value / cogs.Value) * 365m, 1);
                        }
                    }
                }

                var matchingBs = balanceSheets.FirstOrDefault(b =>
                    (!string.IsNullOrEmpty(b.FiscalYear) && b.FiscalYear.Equals(period.FiscalYear, StringComparison.OrdinalIgnoreCase)) ||
                    (b.PeriodEndDate.HasValue && period.PeriodEndDate.HasValue && b.PeriodEndDate.Value.Date == period.PeriodEndDate.Value.Date))
                    ?? (period == latestAnnual ? balanceSheets.FirstOrDefault() : null);

                var existing = existingEntitiesForStock.FirstOrDefault(e =>
                    (!string.IsNullOrEmpty(e.PeriodKey) && e.PeriodKey.Equals(periodKey, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.FiscalYear) && !string.IsNullOrEmpty(period.FiscalYear) && e.FiscalYear.Equals(period.FiscalYear, StringComparison.OrdinalIgnoreCase) && string.Equals(e.PeriodType, period.PeriodType ?? "annual", StringComparison.OrdinalIgnoreCase)) ||
                    (e.PeriodEndDate.HasValue && period.PeriodEndDate.HasValue && e.PeriodEndDate.Value.Date == period.PeriodEndDate.Value.Date && string.Equals(e.PeriodType, period.PeriodType ?? "annual", StringComparison.OrdinalIgnoreCase))
                ) ?? await _financialRepository.GetByStockIdAndPeriodKeyAsync(stock.Id, periodKey);
                if (existing != null)
                {
                    existing.FiscalYear = period.FiscalYear;
                    existing.PeriodType = period.PeriodType ?? "annual";
                    existing.PeriodEndDate = period.PeriodEndDate ?? existing.PeriodEndDate;

                    if (period.Revenue.HasValue) existing.Revenue = period.Revenue;
                    if (period.OperatingProfit.HasValue) existing.OperatingProfit = period.OperatingProfit;
                    if (period.OperatingProfitMargin.HasValue) existing.OperatingProfitMargin = period.OperatingProfitMargin;
                    if (period.Interest.HasValue) existing.Interest = period.Interest;
                    if (period.Depreciation.HasValue) existing.Depreciation = period.Depreciation;
                    if (period.ProfitBeforeTax.HasValue) existing.ProfitBeforeTax = period.ProfitBeforeTax;
                    if (period.Tax.HasValue) existing.Tax = period.Tax;
                    if (period.TaxPercentage.HasValue) existing.TaxPercentage = period.TaxPercentage;
                    if (!existing.Tax.HasValue && existing.ProfitBeforeTax.HasValue && existing.NetProfit.HasValue)
                    {
                        var comp = existing.ProfitBeforeTax.Value - existing.NetProfit.Value;
                        if (comp >= 0) existing.Tax = Math.Round(comp, 2);
                    }
                    if (!existing.TaxPercentage.HasValue && existing.Tax.HasValue && existing.ProfitBeforeTax.HasValue && existing.ProfitBeforeTax.Value > 0)
                    {
                        existing.TaxPercentage = Math.Round((existing.Tax.Value / existing.ProfitBeforeTax.Value) * 100m, 2);
                    }
                    if (period.OtherIncome.HasValue) existing.OtherIncome = period.OtherIncome;
                    if (period.NetProfit.HasValue) existing.NetProfit = period.NetProfit;
                    if (period.Eps.HasValue) existing.Eps = period.Eps;
                    if (period.NetProfitAttributableToMinorityInterest.HasValue)
                        existing.NetProfitAttributableToMinorityInterest = period.NetProfitAttributableToMinorityInterest;
                    if (period.OtherEquity.HasValue) existing.OtherEquity = period.OtherEquity;
                    if (period.EquityCapital.HasValue) existing.EquityCapital = period.EquityCapital;
                    if (period.TotalShares.HasValue) existing.TotalShares = period.TotalShares;
                    if (cfo.HasValue) existing.OperatingCashFlow = cfo;
                    if (capex.HasValue) existing.Capex = capex;
                    if (fcf.HasValue) existing.FreeCashFlow = fcf;
                    if (period.NetCashFlow.HasValue) existing.NetCashFlow = period.NetCashFlow;
                    if (period.TotalEquity.HasValue) existing.TotalEquity = period.TotalEquity;
                    if (period.BookValuePerShare.HasValue) existing.BookValue = period.BookValuePerShare;
                    existing.ConsolidationType = period.ConsolidationType ?? "consolidated";

                    if (dDays.HasValue) existing.DebtorDays = dDays;
                    if (iDays.HasValue) existing.InventoryDays = iDays;
                    if (pDays.HasValue) existing.PayableDays = pDays;

                    if (period == latestAnnual)
                    {
                        ApplyIndianApiRatiosToEntity(existing, data, matchingBs, prevAnnual);
                    }

                    existing.Source = "IndianAPI";
                    existing.LastSyncedAt = now;
                    existing.UpdatedAt = now;

                    await _financialRepository.UpdateAsync(existing);
                }
                else
                {
                    var calculatedTax = period.Tax ?? (period.ProfitBeforeTax.HasValue && period.NetProfit.HasValue && period.ProfitBeforeTax.Value >= period.NetProfit.Value ? Math.Round(period.ProfitBeforeTax.Value - period.NetProfit.Value, 2) : null);
                    var calculatedTaxPct = period.TaxPercentage ?? (calculatedTax.HasValue && period.ProfitBeforeTax.HasValue && period.ProfitBeforeTax.Value > 0 ? Math.Round((calculatedTax.Value / period.ProfitBeforeTax.Value) * 100m, 2) : null);

                    var newEntity = new StockFinancial
                    {
                        StockId = stock.Id,
                        PeriodKey = periodKey,
                        PeriodType = period.PeriodType ?? "annual",
                        FiscalYear = period.FiscalYear,
                        PeriodEndDate = period.PeriodEndDate,
                        Revenue = period.Revenue,
                        OperatingProfit = period.OperatingProfit,
                        OperatingProfitMargin = period.OperatingProfitMargin,
                        Interest = period.Interest,
                        Depreciation = period.Depreciation,
                        ProfitBeforeTax = period.ProfitBeforeTax,
                        Tax = calculatedTax,
                        TaxPercentage = calculatedTaxPct,
                        OtherIncome = period.OtherIncome,
                        NetProfit = period.NetProfit,
                        Eps = period.Eps,
                        NetProfitAttributableToMinorityInterest = period.NetProfitAttributableToMinorityInterest,
                        OtherEquity = period.OtherEquity,
                        EquityCapital = period.EquityCapital,
                        TotalShares = period.TotalShares,
                        TotalEquity = period.TotalEquity,
                        OperatingCashFlow = cfo,
                        Capex = capex,
                        FreeCashFlow = fcf,
                        NetCashFlow = period.NetCashFlow,
                        BookValue = period.BookValuePerShare,
                        ConsolidationType = period.ConsolidationType ?? "consolidated",
                        DebtorDays = dDays,
                        InventoryDays = iDays,
                        PayableDays = pDays,
                        Source = "IndianAPI",
                        LastSyncedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    if (period == latestAnnual)
                    {
                        ApplyIndianApiRatiosToEntity(newEntity, data, matchingBs, prevAnnual);
                    }

                    await _financialRepository.AddAsync(newEntity);
                }
            }

            await _financialRepository.SaveChangesAsync();
            return true;
        }

        private static void ApplyIndianApiRatiosToEntity(
            StockFinancial entity,
            IndianApiStockOverviewDto data,
            StockBalanceSheet? matchingBalanceSheet,
            IndianApiFinancialPeriodDto? prevAnnualPeriod = null)
        {
            if (!entity.CurrentPrice.HasValue && data.CurrentPrice.HasValue) entity.CurrentPrice = data.CurrentPrice;
            if (data.TtmEps.HasValue) entity.TtmEps = data.TtmEps;
            if (data.Roe.HasValue) entity.Roe = data.Roe;
            if (data.Roce.HasValue) entity.Roce = data.Roce;
            if (data.DividendYield.HasValue) entity.DividendYield = data.DividendYield;
            if (data.YearHigh.HasValue && !entity.Week52High.HasValue) entity.Week52High = data.YearHigh;
            if (data.YearLow.HasValue && !entity.Week52Low.HasValue) entity.Week52Low = data.YearLow;
            if (data.FaceValue.HasValue) entity.FaceValue = data.FaceValue;
            if (data.BookValue.HasValue) entity.BookValue = data.BookValue;
            if (data.MarketCap.HasValue) entity.MarketCap = data.MarketCap;
            if (data.SectorPe.HasValue)
            {
                entity.SectorPe = data.SectorPe;
                entity.SectorPeSector = data.SectorName;
            }

            var latestFin = data.Financials.FirstOrDefault(f => f.PeriodType == "annual") ?? data.Financials.FirstOrDefault();

            if (!entity.Interest.HasValue && latestFin?.Interest.HasValue == true) entity.Interest = latestFin.Interest;
            if (!entity.Depreciation.HasValue && latestFin?.Depreciation.HasValue == true) entity.Depreciation = latestFin.Depreciation;
            if (!entity.ProfitBeforeTax.HasValue && latestFin?.ProfitBeforeTax.HasValue == true) entity.ProfitBeforeTax = latestFin.ProfitBeforeTax;
            if (!entity.Tax.HasValue && latestFin?.Tax.HasValue == true) entity.Tax = latestFin.Tax;
            if (!entity.TaxPercentage.HasValue && latestFin?.TaxPercentage.HasValue == true) entity.TaxPercentage = latestFin.TaxPercentage;
            if (!entity.OtherIncome.HasValue && latestFin?.OtherIncome.HasValue == true) entity.OtherIncome = latestFin.OtherIncome;
            if (!entity.OperatingProfitMargin.HasValue && latestFin?.OperatingProfitMargin.HasValue == true) entity.OperatingProfitMargin = latestFin.OperatingProfitMargin;

            decimal? equityCapital = matchingBalanceSheet?.EquityCapital ?? entity.EquityCapital ?? latestFin?.EquityCapital;
            if (equityCapital.HasValue) entity.EquityCapital = equityCapital;

            if (latestFin?.TotalShares.HasValue == true && !entity.TotalShares.HasValue)
            {
                entity.TotalShares = latestFin.TotalShares;
            }

            decimal? faceValue = entity.FaceValue ?? data.FaceValue;
            decimal? currentPrice = entity.CurrentPrice ?? data.CurrentPrice;
            decimal? marketCapRaw = entity.MarketCap ?? data.MarketCap;

            // Fallback dynamic FaceValue calculation
            if (!faceValue.HasValue && equityCapital.HasValue && equityCapital.Value > 0)
            {
                if (entity.TotalShares.HasValue && entity.TotalShares.Value > 0)
                {
                    var sh = entity.TotalShares.Value;
                    faceValue = sh > 10000000 ? Math.Round((equityCapital.Value * 10000000m) / sh, 2) : Math.Round(equityCapital.Value / sh, 2);
                    entity.FaceValue = faceValue;
                }
                else if (currentPrice.HasValue && marketCapRaw.HasValue && marketCapRaw.Value > 0)
                {
                    faceValue = Math.Round((equityCapital.Value * currentPrice.Value) / marketCapRaw.Value, 0);
                    entity.FaceValue = faceValue;
                }
            }

            var (marketCap, totalShares, eqCap, mcSource) = CalculateMarketCap(equityCapital, faceValue, currentPrice, marketCapRaw);
            if (marketCap.HasValue)
            {
                entity.MarketCap = marketCap;
                entity.MarketCapSource = mcSource;
                entity.TotalShares = totalShares ?? entity.TotalShares;
                entity.EquityCapital = eqCap ?? entity.EquityCapital;
            }

            if (!entity.FaceValue.HasValue && entity.EquityCapital.HasValue && entity.EquityCapital.Value > 0 && currentPrice.HasValue && entity.MarketCap.HasValue && entity.MarketCap.Value > 0)
            {
                entity.FaceValue = Math.Round((entity.EquityCapital.Value * currentPrice.Value) / entity.MarketCap.Value, 0);
            }

            // Dynamic calculations for entity metrics if missing (ZERO static hardcoding)
            if (!entity.BookValue.HasValue && entity.TotalEquity.HasValue)
            {
                if (entity.TotalShares.HasValue && entity.TotalShares.Value > 0)
                {
                    var sh = entity.TotalShares.Value;
                    entity.BookValue = sh > 10000000 ? Math.Round((entity.TotalEquity.Value * 10000000m) / sh, 2) : Math.Round(entity.TotalEquity.Value / sh, 2);
                }
                else if (equityCapital.HasValue && entity.FaceValue.HasValue && entity.FaceValue.Value > 0)
                {
                    var shCr = equityCapital.Value / entity.FaceValue.Value;
                    if (shCr > 0) entity.BookValue = Math.Round(entity.TotalEquity.Value / shCr, 2);
                }
            }

            if (!entity.Roe.HasValue && entity.NetProfit.HasValue && entity.TotalEquity.HasValue && entity.TotalEquity.Value > 0)
            {
                entity.Roe = Math.Round((entity.NetProfit.Value / entity.TotalEquity.Value) * 100m, 2);
            }

            if (!entity.Roce.HasValue)
            {
                decimal? ebit = entity.ProfitBeforeTax.HasValue
                    ? (entity.ProfitBeforeTax.Value + (entity.Interest ?? 0))
                    : (entity.OperatingProfit.HasValue ? entity.OperatingProfit.Value + (entity.OtherIncome ?? 0) : null);

                decimal? capEmp = entity.TotalEquity.HasValue
                    ? (entity.TotalEquity.Value + (matchingBalanceSheet?.Borrowings ?? 0))
                    : null;

                if (ebit.HasValue && capEmp.HasValue && capEmp.Value > 0)
                {
                    entity.Roce = Math.Round((ebit.Value / capEmp.Value) * 100m, 2);
                }
            }

            // Dynamic P/E Calculation prioritizing live Yahoo price
            var eps = entity.TtmEps ?? entity.Eps;
            if (eps.HasValue && eps.Value > 0 && currentPrice.HasValue && currentPrice.Value > 0)
            {
                entity.PeRatio = Math.Round(currentPrice.Value / eps.Value, 2);
            }
            else if (entity.MarketCap.HasValue && entity.NetProfit.HasValue && entity.NetProfit.Value > 0)
            {
                entity.PeRatio = Math.Round(entity.MarketCap.Value / entity.NetProfit.Value, 2);
            }
            else if (data.PeRatio.HasValue)
            {
                entity.PeRatio = data.PeRatio;
            }

            // Dynamic P/B Calculation prioritizing live Yahoo price
            if (currentPrice.HasValue && currentPrice.Value > 0 && entity.BookValue.HasValue && entity.BookValue.Value > 0)
            {
                entity.PbRatio = Math.Round(currentPrice.Value / entity.BookValue.Value, 2);
            }
            else if (data.PbRatio.HasValue)
            {
                entity.PbRatio = data.PbRatio;
            }

            // Dynamic PEG Calculation
            if (entity.PeRatio.HasValue && (!entity.PegRatio.HasValue || entity.PegRatio == 0))
            {
                var epsGrowth = CalculatePercentageGrowth(entity.Eps, prevAnnualPeriod?.Eps) ?? CalculatePercentageGrowth(entity.NetProfit, prevAnnualPeriod?.NetProfit);
                if (epsGrowth.HasValue && epsGrowth.Value > 0)
                {
                    entity.PegRatio = Math.Round(entity.PeRatio.Value / epsGrowth.Value, 2);
                }
            }
        }

        private async Task SyncFromBharatStockAsync(Stock stock, CancellationToken cancellationToken)
        {
            IReadOnlyList<BharatStockFinancialRecord>? records = null;
            BharatStockRatiosRecord? ratios = null;
            BharatStockCompanyDetailsRecord? details = null;
            BharatStockScreenerRecord? screener = null;
            decimal? livePrice = null;

            try
            {
                records = await _financialProvider.GetFinancialsAsync(stock.Symbol, "annual", 1, 3, cancellationToken);
                ratios = await _financialProvider.GetRatiosAsync(stock.Symbol, cancellationToken);
                details = await _financialProvider.GetStockDetailsAsync(stock.Symbol, stock.Exchange, cancellationToken);
                screener = await _financialProvider.GetScreenerDataAsync(stock.Symbol, stock.Exchange, cancellationToken);
                var liveQuote = await GetLiveQuoteInternalAsync(stock.Symbol, stock.Exchange, cancellationToken);
                livePrice = liveQuote?.Price;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve financials or ratios from backup provider for stock {Symbol}.", stock.Symbol);
            }

            if (records == null || records.Count == 0)
            {
                _logger.LogWarning("No financial records returned by BharatStock for {Symbol}.", stock.Symbol);
                return;
            }

            // Derive sector valuation
            SectorValuationResultDto? sectorValuation = null;
            var sector = details?.Sector ?? screener?.Sector;
            if (!string.IsNullOrWhiteSpace(sector))
            {
                try
                {
                    sectorValuation = await _sectorValuationService.GetSectorValuationAsync(sector, stock.Exchange, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch sector valuation for {Sector}.", sector);
                }
            }

            var now = DateTime.UtcNow;
            var balanceSheets = await _balanceSheetRepository.GetRecentByStockIdAsync(stock.Id, 5);

            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var periodKey = record.ResolvedPeriodKey;
                if (string.IsNullOrWhiteSpace(periodKey))
                {
                    continue;
                }

                var cfo = record.CashFlowOperating;
                var capex = record.Capex;
                decimal? fcf = null;
                if (cfo.HasValue && capex.HasValue)
                {
                    fcf = cfo.Value - capex.Value;
                }
                else if (cfo.HasValue)
                {
                    fcf = cfo.Value;
                }

                var matchingBs = balanceSheets.FirstOrDefault(b =>
                    (!string.IsNullOrEmpty(b.PeriodKey) && !string.IsNullOrEmpty(record.ResolvedPeriodKey) && b.PeriodKey.Equals(record.ResolvedPeriodKey, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(b.FiscalYear) && !string.IsNullOrEmpty(record.ResolvedFiscalYear) && b.FiscalYear.Equals(record.ResolvedFiscalYear, StringComparison.OrdinalIgnoreCase)) ||
                    (b.PeriodEndDate.HasValue && record.ResolvedPeriodEndDate.HasValue && b.PeriodEndDate.Value.Date == record.ResolvedPeriodEndDate.Value.Date))
                    ?? (i == 0 ? balanceSheets.FirstOrDefault() : null);

                var totalEquityInfo = DetermineTotalEquity(record, matchingBs);

                var existing = await _financialRepository.GetByStockIdAndPeriodKeyAsync(stock.Id, periodKey);
                if (existing != null)
                {
                    existing.FiscalYear = record.ResolvedFiscalYear;
                    existing.PeriodType = record.PeriodType ?? "annual";
                    existing.PeriodEndDate = record.ResolvedPeriodEndDate ?? existing.PeriodEndDate;

                    if (record.ResolvedRevenueFromOperations.HasValue) existing.Revenue = record.ResolvedRevenueFromOperations;
                    if (record.ResolvedOperatingProfitEbitda.HasValue) existing.OperatingProfit = record.ResolvedOperatingProfitEbitda;
                    if (record.NetProfit.HasValue) existing.NetProfit = record.NetProfit;
                    if (record.Eps.HasValue) existing.Eps = record.Eps;
                    if (record.NetProfitAttributableToMinorityInterest.HasValue)
                        existing.NetProfitAttributableToMinorityInterest = record.NetProfitAttributableToMinorityInterest;
                    if (record.OtherEquity.HasValue) existing.OtherEquity = record.OtherEquity;
                    if (cfo.HasValue) existing.OperatingCashFlow = cfo;
                    if (capex.HasValue) existing.Capex = capex;
                    if (fcf.HasValue) existing.FreeCashFlow = fcf;
                    if (record.ResolvedNetCashFlow.HasValue) existing.NetCashFlow = record.ResolvedNetCashFlow;
                    if (!string.IsNullOrWhiteSpace(record.ConsolidationType)) existing.ConsolidationType = record.ConsolidationType;

                    if (totalEquityInfo.TotalEquity.HasValue)
                    {
                        existing.TotalEquity = totalEquityInfo.TotalEquity.Value;
                    }

                    if (i == 0)
                    {
                        var prevRecord = records.Count > 1 ? records[1] : null;
                        ApplyRatiosToEntity(existing, ratios, details, screener, sectorValuation, totalEquityInfo, matchingBs, livePrice, prevRecord);
                    }

                    existing.Source = !string.IsNullOrWhiteSpace(record.Source) ? record.Source : existing.Source;
                    existing.LastSyncedAt = now;
                    existing.UpdatedAt = now;

                    await _financialRepository.UpdateAsync(existing);
                }
                else
                {
                    var newEntity = new StockFinancial
                    {
                        StockId = stock.Id,
                        PeriodKey = periodKey,
                        PeriodType = record.PeriodType ?? "annual",
                        FiscalYear = record.ResolvedFiscalYear,
                        PeriodEndDate = record.ResolvedPeriodEndDate,
                        Revenue = record.ResolvedRevenueFromOperations,
                        OperatingProfit = record.ResolvedOperatingProfitEbitda,
                        NetProfit = record.NetProfit,
                        Eps = record.Eps,
                        NetProfitAttributableToMinorityInterest = record.NetProfitAttributableToMinorityInterest,
                        OtherEquity = record.OtherEquity,
                        TotalEquity = totalEquityInfo.TotalEquity,
                        TotalEquitySource = totalEquityInfo.Source,
                        OperatingCashFlow = cfo,
                        Capex = capex,
                        FreeCashFlow = fcf,
                        NetCashFlow = record.ResolvedNetCashFlow,
                        ConsolidationType = record.ConsolidationType ?? "consolidated",
                        Source = !string.IsNullOrWhiteSpace(record.Source) ? record.Source : "BharatStock",
                        LastSyncedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    if (i == 0)
                    {
                        var prevRecord = records.Count > 1 ? records[1] : null;
                        ApplyRatiosToEntity(newEntity, ratios, details, screener, sectorValuation, totalEquityInfo, matchingBs, livePrice, prevRecord);
                    }

                    await _financialRepository.AddAsync(newEntity);
                }
            }

            await _financialRepository.SaveChangesAsync();
        }

        private static void ApplyRatiosToEntity(
            StockFinancial entity,
            BharatStockRatiosRecord? ratios,
            BharatStockCompanyDetailsRecord? details = null,
            BharatStockScreenerRecord? screener = null,
            SectorValuationResultDto? sectorValuation = null,
            (decimal? TotalEquity, string? Source, string? Period)? totalEquityInfo = null,
            StockBalanceSheet? matchingBalanceSheet = null,
            decimal? livePrice = null,
            BharatStockFinancialRecord? prevRecord = null)
        {
            if (ratios != null)
            {
                if (ratios.Roe.HasValue) entity.Roe = ratios.Roe;
                if (ratios.Roce.HasValue) entity.Roce = ratios.Roce;
                if (ratios.PeRatio.HasValue) entity.PeRatio = ratios.PeRatio;
                if (ratios.PbRatio.HasValue) entity.PbRatio = ratios.PbRatio;
                if (ratios.DividendYield.HasValue) entity.DividendYield = ratios.DividendYield;
                if (ratios.Week52High.HasValue) entity.Week52High = ratios.Week52High;
                if (ratios.Week52Low.HasValue) entity.Week52Low = ratios.Week52Low;
                if (!entity.CurrentPrice.HasValue && ratios.Price.HasValue) entity.CurrentPrice = ratios.Price;
                if (!string.IsNullOrWhiteSpace(ratios.AsOfDate)) entity.RatiosAsOfDate = ratios.AsOfDate;
            }

            if (details != null)
            {
                if (details.FaceValue.HasValue) entity.FaceValue = details.FaceValue;
                if (!entity.CurrentPrice.HasValue && details.LatestPrice?.Close.HasValue == true)
                    entity.CurrentPrice = details.LatestPrice.Close;
            }

            if (livePrice.HasValue)
            {
                entity.CurrentPrice = livePrice.Value;
            }

            if (screener != null)
            {
                if (screener.ResolvedBookValue.HasValue) entity.BookValue = screener.ResolvedBookValue;
                if (screener.MarketCap.HasValue) entity.MarketCap = screener.MarketCap;
                if (!entity.Roe.HasValue && screener.Roe.HasValue) entity.Roe = screener.Roe;
                if (!entity.Roce.HasValue && screener.Roce.HasValue) entity.Roce = screener.Roce;
                if (!entity.PeRatio.HasValue && screener.PeRatio.HasValue) entity.PeRatio = screener.PeRatio;
                if (!entity.PbRatio.HasValue && screener.PbRatio.HasValue) entity.PbRatio = screener.PbRatio;
            }

            if (totalEquityInfo.HasValue)
            {
                if (totalEquityInfo.Value.TotalEquity.HasValue)
                {
                    entity.TotalEquity = totalEquityInfo.Value.TotalEquity.Value;
                }
                entity.TotalEquitySource = totalEquityInfo.Value.Source;
            }

            if (sectorValuation != null)
            {
                if (sectorValuation.SectorPe.HasValue)
                {
                    entity.SectorPe = sectorValuation.SectorPe.Value;
                }
                entity.SectorPeSector = sectorValuation.Sector;
            }

            // Market Cap Manual Calculation:
            // Total Num of Shares = Equity Capital / Face Value
            // Market Cap = Total Num of Shares * Current share price
            decimal? equityCapital = matchingBalanceSheet?.EquityCapital ?? entity.EquityCapital;
            var (marketCap, totalShares, eqCap, mcSource) = CalculateMarketCap(equityCapital, entity.FaceValue, entity.CurrentPrice, entity.MarketCap);
            if (marketCap.HasValue)
            {
                entity.MarketCap = marketCap;
                entity.MarketCapSource = mcSource;
                entity.TotalShares = totalShares;
                entity.EquityCapital = eqCap;
            }

            // Dynamic PEG Calculation
            if (entity.PeRatio.HasValue && (!entity.PegRatio.HasValue || entity.PegRatio == 0))
            {
                var epsGrowth = CalculatePercentageGrowth(entity.Eps, prevRecord?.Eps) ?? CalculatePercentageGrowth(entity.NetProfit, prevRecord?.NetProfit);
                if (epsGrowth.HasValue && epsGrowth.Value > 0)
                {
                    entity.PegRatio = Math.Round(entity.PeRatio.Value / epsGrowth.Value, 2);
                }
            }
        }

        private static readonly ConcurrentDictionary<string, (YahooLiveQuoteDto Quote, DateTime FetchedAt)> LiveQuoteMemoryCache = new();

        private async Task<YahooLiveQuoteDto?> GetLiveQuoteInternalAsync(string symbol, string? exchange, CancellationToken cancellationToken)
        {
            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
            var cacheKey = $"{cleanSymbol}:{cleanExchange}";

            if (LiveQuoteMemoryCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.FetchedAt).TotalSeconds < 15)
            {
                return cached.Quote;
            }

            // Real-time Live Quote directly from Yahoo Finance
            if (_yahooFinanceClient != null)
            {
                try
                {
                    var quote = await _yahooFinanceClient.GetLiveQuoteAsync(cleanSymbol, cleanExchange, cancellationToken);
                    if (quote?.Price.HasValue == true && quote.Price.Value > 0)
                    {
                        LiveQuoteMemoryCache[cacheKey] = (quote, DateTime.UtcNow);
                        return quote;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get live quote from Yahoo Finance for {Symbol}", cleanSymbol);
                }
            }

            return null;
        }

        private static (decimal? MarketCap, decimal? TotalShares, decimal? EquityCapital, string? Source) CalculateMarketCap(
            decimal? equityCapital,
            decimal? faceValue,
            decimal? currentPrice,
            decimal? fallbackMarketCap = null)
        {
            if (equityCapital.HasValue && equityCapital.Value > 0 && faceValue.HasValue && faceValue.Value > 0)
            {
                // Formula: Total Num of Shares = Equity Capital (in ₹ Cr) / Face Value (in ₹)
                var totalShares = Math.Round(equityCapital.Value / faceValue.Value, 4); // in Crores of shares

                if (currentPrice.HasValue && currentPrice.Value > 0)
                {
                    // Formula: Market Cap = Total Num of Shares (in Cr) * Current Share Price (in ₹)
                    var marketCap = Math.Round(totalShares * currentPrice.Value, 2); // in ₹ Crores
                    return (marketCap, totalShares, equityCapital, "Calculated (Equity Capital ÷ Face Value × Current Price)");
                }

                return (fallbackMarketCap, totalShares, equityCapital, fallbackMarketCap.HasValue ? "Reported" : null);
            }

            if (fallbackMarketCap.HasValue && fallbackMarketCap.Value > 0 && currentPrice.HasValue && currentPrice.Value > 0)
            {
                var totalShares = Math.Round(fallbackMarketCap.Value / currentPrice.Value, 4);
                return (fallbackMarketCap, totalShares, equityCapital, "Reported");
            }

            return (fallbackMarketCap, null, equityCapital, fallbackMarketCap.HasValue ? "Reported" : null);
        }

        private static (decimal? TotalEquity, string? Source, string? Period) DetermineTotalEquity(
            BharatStockFinancialRecord? record,
            StockBalanceSheet? matchingBalanceSheet = null)
        {
            // Direct reported shareholders' equity / total equity field only (No calculated formulas)
            if (record?.TotalEquity.HasValue == true)
            {
                return (record.TotalEquity.Value, "Reported", record.ResolvedFiscalYear);
            }

            if (record?.ShareholdersEquity.HasValue == true)
            {
                return (record.ShareholdersEquity.Value, "Reported", record.ResolvedFiscalYear);
            }

            return (null, null, null);
        }

        private async Task<StockCashflowResponseDto> BuildResponseDtoAsync(
            Stock stock,
            List<StockFinancial> entities,
            CancellationToken cancellationToken = default)
        {
            if (entities.Count == 0)
            {
                throw new KeyNotFoundException($"No financial records found for stock {stock.Symbol}.");
            }

            var current = entities[0];
            StockFinancial? previous = entities.Count > 1 ? entities[1] : null;

            // Derived calculations
            decimal? cfoToOpRatio = null;
            if (current.OperatingCashFlow.HasValue && current.OperatingProfit.HasValue && current.OperatingProfit.Value != 0)
            {
                cfoToOpRatio = Math.Round(current.OperatingCashFlow.Value / current.OperatingProfit.Value, 2);
            }

            decimal? cfoToNpRatio = null;
            if (current.OperatingCashFlow.HasValue && current.NetProfit.HasValue && current.NetProfit.Value != 0)
            {
                cfoToNpRatio = Math.Round(current.OperatingCashFlow.Value / current.NetProfit.Value, 2);
            }

            decimal? fcfMarginPct = null;
            if (current.FreeCashFlow.HasValue && current.Revenue.HasValue && current.Revenue.Value != 0)
            {
                fcfMarginPct = Math.Round((current.FreeCashFlow.Value / current.Revenue.Value) * 100, 2);
            }

            decimal? capexToCfoPct = null;
            if (current.Capex.HasValue && current.OperatingCashFlow.HasValue && current.OperatingCashFlow.Value != 0)
            {
                capexToCfoPct = Math.Round((current.Capex.Value / current.OperatingCashFlow.Value) * 100, 2);
            }

            // Total equity metadata
            string? equitySource = current.TotalEquitySource;
            if (string.IsNullOrWhiteSpace(equitySource) && current.TotalEquity.HasValue)
            {
                equitySource = "Reported";
            }
            string? equityPeriod = current.FiscalYear;
            decimal? totalEquity = current.TotalEquity;
            var dbBs = await _balanceSheetRepository.GetRecentByStockIdAsync(stock.Id, 1);
            var latestBs = dbBs.FirstOrDefault();

            // Sector P/E metadata
            string? sectorName = current.SectorPeSector ?? stock.Company?.Industry ?? stock.Company?.CompanyName;
            string? sectorAsOfDate = current.RatiosAsOfDate;
            decimal? sectorPe = current.SectorPe;

            // Market Cap & Face Value Calculation
            decimal? currentPrice = current.CurrentPrice;
            decimal? week52High = current.Week52High;
            decimal? week52Low = current.Week52Low;
            decimal? equityCapital = latestBs?.EquityCapital ?? current.EquityCapital;
            decimal? faceValue = current.FaceValue;
            decimal? marketCap = current.MarketCap;
            decimal? roe = current.Roe;
            decimal? roce = current.Roce;
            decimal? peRatio = current.PeRatio;
            decimal? bookValue = current.BookValue;

            // Always fetch real-time live price & 52W range from Yahoo Finance to ensure immediate accuracy
            try
            {
                var liveQuote = await GetLiveQuoteInternalAsync(stock.Symbol, stock.Exchange, cancellationToken);
                if (liveQuote?.Price.HasValue == true && liveQuote.Price.Value > 0)
                {
                    currentPrice = liveQuote.Price.Value;
                    if (liveQuote.YearHigh.HasValue) week52High = liveQuote.YearHigh;
                    if (liveQuote.YearLow.HasValue) week52Low = liveQuote.YearLow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch live quote for {Symbol} during BuildResponseDtoAsync", stock.Symbol);
            }

            if (!faceValue.HasValue && equityCapital.HasValue && equityCapital.Value > 0)
            {
                if (current.TotalShares.HasValue && current.TotalShares.Value > 0)
                {
                    var sh = current.TotalShares.Value;
                    faceValue = sh > 10000000 ? Math.Round((equityCapital.Value * 10000000m) / sh, 2) : Math.Round(equityCapital.Value / sh, 2);
                }
                else if (currentPrice.HasValue && (marketCap ?? current.MarketCap).HasValue && (marketCap ?? current.MarketCap)!.Value > 0)
                {
                    faceValue = Math.Round((equityCapital.Value * currentPrice.Value) / (marketCap ?? current.MarketCap)!.Value, 0);
                }
            }

            var (calculatedMc, totalShares, eqCap, mcSource) = CalculateMarketCap(equityCapital, faceValue, currentPrice, marketCap ?? current.MarketCap);
            marketCap = calculatedMc ?? marketCap;

            if (!faceValue.HasValue && equityCapital.HasValue && equityCapital.Value > 0 && currentPrice.HasValue && currentPrice.Value > 0 && marketCap.HasValue && marketCap.Value > 0)
            {
                faceValue = Math.Round((equityCapital.Value * currentPrice.Value) / marketCap.Value, 0);
            }

            if (!bookValue.HasValue && totalEquity.HasValue)
            {
                if (totalShares.HasValue && totalShares.Value > 0)
                {
                    bookValue = totalShares.Value > 10000000 ? Math.Round((totalEquity.Value * 10000000m) / totalShares.Value, 2) : Math.Round(totalEquity.Value / totalShares.Value, 2);
                }
                else if (eqCap.HasValue && faceValue.HasValue && faceValue.Value > 0)
                {
                    var shCr = eqCap.Value / faceValue.Value;
                    if (shCr > 0) bookValue = Math.Round(totalEquity.Value / shCr, 2);
                }
            }

            if (!roe.HasValue && current.NetProfit.HasValue && totalEquity.HasValue && totalEquity.Value > 0)
            {
                roe = Math.Round((current.NetProfit.Value / totalEquity.Value) * 100m, 2);
            }

            if (!roce.HasValue)
            {
                decimal? ebit = current.ProfitBeforeTax.HasValue ? (current.ProfitBeforeTax.Value + (current.Interest ?? 0)) : (current.OperatingProfit.HasValue ? current.OperatingProfit.Value + (current.OtherIncome ?? 0) : null);
                decimal? capEmp = totalEquity.HasValue ? (totalEquity.Value + (latestBs?.Borrowings ?? 0)) : null;
                if (ebit.HasValue && capEmp.HasValue && capEmp.Value > 0) roce = Math.Round((ebit.Value / capEmp.Value) * 100m, 2);
            }

            // Derive EPS fallback from TtmEps or NetProfit / TotalShares if EPS is missing
            if (!current.Eps.HasValue && current.TtmEps.HasValue) current.Eps = current.TtmEps;
            if (!current.Eps.HasValue && current.NetProfit.HasValue && totalShares.HasValue && totalShares.Value > 0)
                current.Eps = Math.Round((current.NetProfit.Value * 10000000m) / totalShares.Value, 2);

            if (previous != null)
            {
                if (!previous.Eps.HasValue && previous.NetProfit.HasValue && totalShares.HasValue && totalShares.Value > 0)
                    previous.Eps = Math.Round((previous.NetProfit.Value * 10000000m) / totalShares.Value, 2);
            }

            var yoy = new CashflowYoYChangeDto();

            if (previous != null)
            {
                yoy.FreeCashFlowChange = CalculateAbsoluteChange(current.FreeCashFlow, previous.FreeCashFlow);
                yoy.FreeCashFlowGrowth = CalculatePercentageGrowth(current.FreeCashFlow, previous.FreeCashFlow);

                yoy.OperatingCashFlowChange = CalculateAbsoluteChange(current.OperatingCashFlow, previous.OperatingCashFlow);
                yoy.OperatingCashFlowGrowth = CalculatePercentageGrowth(current.OperatingCashFlow, previous.OperatingCashFlow);

                yoy.CapexChange = CalculateAbsoluteChange(current.Capex, previous.Capex);
                yoy.CapexGrowth = CalculatePercentageGrowth(current.Capex, previous.Capex);

                yoy.NetProfitChange = CalculateAbsoluteChange(current.NetProfit, previous.NetProfit);
                yoy.NetProfitGrowth = CalculatePercentageGrowth(current.NetProfit, previous.NetProfit);

                yoy.EpsChange = CalculateAbsoluteChange(current.Eps, previous.Eps) ?? yoy.NetProfitChange;
                yoy.EpsGrowth = CalculatePercentageGrowth(current.Eps, previous.Eps) ?? yoy.NetProfitGrowth;

                yoy.RevenueChange = CalculateAbsoluteChange(current.Revenue, previous.Revenue);
                yoy.RevenueGrowth = CalculatePercentageGrowth(current.Revenue, previous.Revenue);

                yoy.NetCashFlowChange = CalculateAbsoluteChange(current.NetCashFlow, previous.NetCashFlow);
                yoy.NetCashFlowGrowth = CalculatePercentageGrowth(current.NetCashFlow, previous.NetCashFlow);
            }
            else
            {
                // Fallback from quarterly records if annual history is single-year
                try
                {
                    var quarterlyEntities = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "quarterly", 8);
                    if (quarterlyEntities != null && quarterlyEntities.Count >= 2)
                    {
                        var qCur = quarterlyEntities[0];
                        var qPrev = quarterlyEntities.Count >= 5 ? quarterlyEntities[4] : quarterlyEntities[1];
                        yoy.NetProfitGrowth = CalculatePercentageGrowth(qCur.NetProfit, qPrev.NetProfit);
                        yoy.EpsGrowth = CalculatePercentageGrowth(qCur.Eps, qPrev.Eps) ?? yoy.NetProfitGrowth;
                        yoy.RevenueGrowth = CalculatePercentageGrowth(qCur.Revenue, qPrev.Revenue);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not fetch quarterly fallback for EPS growth for {Symbol}", stock.Symbol);
                }
            }

            // Dynamic P/E calculation based on current live price
            var eps = current.TtmEps ?? current.Eps;
            if (eps.HasValue && eps.Value > 0 && currentPrice.HasValue && currentPrice.Value > 0)
            {
                peRatio = Math.Round(currentPrice.Value / eps.Value, 2);
            }
            else if (marketCap.HasValue && current.NetProfit.HasValue && current.NetProfit.Value > 0)
            {
                peRatio = Math.Round(marketCap.Value / current.NetProfit.Value, 2);
            }
            else
            {
                peRatio = peRatio ?? current.PeRatio;
            }

            // Dynamic P/B calculation based on current live price
            decimal? pbRatio = null;
            if (currentPrice.HasValue && currentPrice.Value > 0 && (bookValue ?? current.BookValue).HasValue && (bookValue ?? current.BookValue)!.Value > 0)
            {
                pbRatio = Math.Round(currentPrice.Value / (bookValue ?? current.BookValue)!.Value, 2);
            }
            else
            {
                pbRatio = current.PbRatio;
            }

            // PEG Ratio Calculation: P/E / Trailing EPS Growth Rate
            decimal? effectiveGrowth = yoy.EpsGrowth ?? yoy.NetProfitGrowth ?? yoy.RevenueGrowth;
            decimal? pegRatio = null;
            if (peRatio.HasValue && effectiveGrowth.HasValue && effectiveGrowth.Value > 0)
            {
                pegRatio = Math.Round(peRatio.Value / effectiveGrowth.Value, 2);
            }
            pegRatio = pegRatio ?? current.PegRatio;

            if ((pegRatio.HasValue && current.PegRatio != pegRatio) || (peRatio.HasValue && current.PeRatio != peRatio) || (pbRatio.HasValue && current.PbRatio != pbRatio))
            {
                if (pegRatio.HasValue) current.PegRatio = pegRatio;
                if (peRatio.HasValue) current.PeRatio = peRatio;
                if (pbRatio.HasValue) current.PbRatio = pbRatio;
                try
                {
                    await _financialRepository.UpdateAsync(current);
                    await _financialRepository.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist updated ratios to DB in BuildResponseDtoAsync for {Symbol}", stock.Symbol);
                }
            }

            decimal? prevRoe = previous?.Roe;
            if (!prevRoe.HasValue && previous?.NetProfit.HasValue == true && previous.TotalEquity.HasValue && previous.TotalEquity.Value > 0)
            {
                prevRoe = Math.Round((previous.NetProfit.Value / previous.TotalEquity.Value) * 100m, 2);
            }

            decimal? prevRoce = previous?.Roce;
            if (!prevRoce.HasValue && previous != null)
            {
                decimal? prevEbit = previous.ProfitBeforeTax.HasValue ? (previous.ProfitBeforeTax.Value + (previous.Interest ?? 0)) : (previous.OperatingProfit.HasValue ? previous.OperatingProfit.Value + (previous.OtherIncome ?? 0) : null);
                decimal? prevCapEmp = previous.TotalEquity.HasValue ? (previous.TotalEquity.Value + (latestBs?.Borrowings ?? 0)) : null;
                if (prevEbit.HasValue && prevCapEmp.HasValue && prevCapEmp.Value > 0)
                    prevRoce = Math.Round((prevEbit.Value / prevCapEmp.Value) * 100m, 2);
            }

            var ratiosDto = new StockRatiosDto
            {
                Roe = roe,
                RoeYoY = CalculatePercentageGrowth(roe, prevRoe),
                RoeChange = (roe.HasValue && prevRoe.HasValue) ? Math.Round(roe.Value - prevRoe.Value, 2) : null,
                RoePrevYear = prevRoe,
                Roce = roce,
                RoceYoY = CalculatePercentageGrowth(roce, prevRoce),
                RoceChange = (roce.HasValue && prevRoce.HasValue) ? Math.Round(roce.Value - prevRoce.Value, 2) : null,
                RocePrevYear = prevRoce,
                PeRatio = peRatio,
                TtmEps = current.TtmEps,
                PbRatio = pbRatio,
                DividendYield = current.DividendYield,
                Week52High = week52High,
                Week52Low = week52Low,
                CurrentPrice = currentPrice,
                AsOfDate = current.RatiosAsOfDate,
                FinancialsFiscalYear = current.FiscalYear,
                FinancialsPeriodType = current.PeriodType,
                FaceValue = faceValue,
                EquityCapital = eqCap,
                TotalShares = totalShares,
                TotalEquity = totalEquity,
                TotalEquityPeriod = equityPeriod,
                TotalEquitySource = equitySource,
                BookValue = bookValue,
                MarketCap = marketCap,
                MarketCapSource = mcSource,
                SectorPe = sectorPe,
                SectorPeSector = sectorName,
                SectorPeAsOfDate = sectorAsOfDate,
                PegRatio = pegRatio,
                DebtorDays = current.DebtorDays,
                DebtorDaysYoY = CalculatePercentageGrowth(current.DebtorDays, previous?.DebtorDays),
                InventoryDays = current.InventoryDays,
                InventoryDaysYoY = CalculatePercentageGrowth(current.InventoryDays, previous?.InventoryDays),
                PayableDays = current.PayableDays,
                PayableDaysYoY = CalculatePercentageGrowth(current.PayableDays, previous?.PayableDays)
            };

            if (!current.Interest.HasValue || !current.Depreciation.HasValue)
            {
                await EnsureInterestAndDepreciationPopulatedAsync(stock, current, cancellationToken);
            }

            var summary = new CashflowSummaryDto
            {
                FiscalYear = current.FiscalYear,
                PeriodEndDate = current.PeriodEndDate?.ToString("yyyy-MM-dd"),
                OperatingCashFlow = current.OperatingCashFlow,
                Capex = current.Capex,
                FreeCashFlow = current.FreeCashFlow,
                NetCashFlow = current.NetCashFlow,
                Revenue = current.Revenue,
                OperatingProfit = current.OperatingProfit,
                NetProfit = current.NetProfit,
                Eps = current.Eps,
                Interest = current.Interest,
                Depreciation = current.Depreciation,
                OtherEquity = current.OtherEquity,
                TotalEquity = totalEquity,
                CfoToOperatingProfitRatio = cfoToOpRatio,
                CfoToNetProfitRatio = cfoToNpRatio,
                FcfMarginPercent = fcfMarginPct,
                CapexToCfoPercent = capexToCfoPct,
                ConsolidationType = current.ConsolidationType,
                Ratios = ratiosDto,
                YoYChange = yoy
            };

            return new StockCashflowResponseDto
            {
                StockId = stock.Id,
                Symbol = stock.Symbol,
                Exchange = stock.Exchange,
                CompanyName = stock.Company?.CompanyName ?? $"{stock.Symbol} Limited",
                LatestFiscalYear = current.FiscalYear,
                DataAsOf = current.PeriodEndDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? current.FiscalYear,
                Source = current.Source,
                LastSyncedAt = current.LastSyncedAt,
                Summary = summary,
                Ratios = ratiosDto
            };
        }

        private static decimal? CalculateAbsoluteChange(decimal? current, decimal? previous)
        {
            if (current.HasValue && previous.HasValue)
            {
                return Math.Round(current.Value - previous.Value, 2);
            }
            return null;
        }

        private static decimal? CalculatePercentageGrowth(decimal? current, decimal? previous)
        {
            if (current.HasValue && previous.HasValue && previous.Value != 0)
            {
                var growth = ((current.Value - previous.Value) / Math.Abs(previous.Value)) * 100;
                return Math.Round(growth, 2);
            }
            return null;
        }

        private async Task EnsureInterestAndDepreciationPopulatedAsync(Stock stock, StockFinancial cur, CancellationToken cancellationToken)
        {
            if (!cur.Interest.HasValue || cur.Interest.Value == 0)
            {
                // 1. Try summing 4 quarterly records from DB
                var dbQuarters = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "quarterly", 4);
                if (dbQuarters.Count > 0 && dbQuarters.Any(q => q.Interest.HasValue && q.Interest.Value > 0))
                {
                    cur.Interest = dbQuarters.Sum(q => q.Interest ?? 0);
                }
                else if (_yahooFinanceClient != null)
                {
                    try
                    {
                        var yfQuarters = await _yahooFinanceClient.GetQuarterlyIncomeStatementsAsync(stock.Symbol, stock.Exchange, cancellationToken);
                        if (yfQuarters != null && yfQuarters.Count > 0)
                        {
                            var sumInt = yfQuarters.Take(4).Sum(q => q.Interest ?? 0);
                            if (sumInt > 0)
                            {
                                cur.Interest = sumInt;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fallback interest from Yahoo Finance for {Symbol}", stock.Symbol);
                    }
                }
            }

            if (!cur.Depreciation.HasValue || cur.Depreciation.Value == 0)
            {
                var dbQuarters = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "quarterly", 4);
                if (dbQuarters.Count > 0 && dbQuarters.Any(q => q.Depreciation.HasValue && q.Depreciation.Value > 0))
                {
                    cur.Depreciation = dbQuarters.Sum(q => q.Depreciation ?? 0);
                }
                else if (_yahooFinanceClient != null)
                {
                    try
                    {
                        var yfQuarters = await _yahooFinanceClient.GetQuarterlyIncomeStatementsAsync(stock.Symbol, stock.Exchange, cancellationToken);
                        if (yfQuarters != null && yfQuarters.Count > 0)
                        {
                            var sumDep = yfQuarters.Take(4).Sum(q => q.Depreciation ?? 0);
                            if (sumDep > 0)
                            {
                                cur.Depreciation = sumDep;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fallback depreciation from Yahoo Finance for {Symbol}", stock.Symbol);
                    }
                }
            }

            if (cur.Interest.HasValue || cur.Depreciation.HasValue)
            {
                try
                {
                    await _financialRepository.UpdateAsync(cur);
                    await _financialRepository.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist fallback interest/depreciation to DB for {Symbol}", stock.Symbol);
                }
            }
        }
    }
}


