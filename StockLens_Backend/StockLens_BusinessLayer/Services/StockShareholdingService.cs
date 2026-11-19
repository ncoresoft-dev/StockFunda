using AutoMapper;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions;
using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockShareholdingService : IStockShareholdingService
    {
        private readonly IStockRepository _stockRepository;
        private readonly IStockShareholdingRepository _shareholdingRepository;
        private readonly IIndianApiShareholdingClient _indianApiClient;
        private readonly IShareholdingProvider _bharatStockProvider;
        private readonly IMapper _mapper;
        private readonly ILogger<StockShareholdingService> _logger;

        // Keyed concurrency locks to prevent thundering-herd API calls per stock
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> StockLocks = new();

        public StockShareholdingService(
            IStockRepository stockRepository,
            IStockShareholdingRepository shareholdingRepository,
            ICompanyRepository? companyRepository,
            IIndianApiShareholdingClient indianApiClient,
            IShareholdingProvider bharatStockProvider,
            IMapper mapper,
            ILogger<StockShareholdingService> logger)
        {
            _stockRepository = stockRepository;
            _shareholdingRepository = shareholdingRepository;
            _indianApiClient = indianApiClient;
            _bharatStockProvider = bharatStockProvider;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<StockShareholdingResponseDto> GetShareholdingByStockIdAsync(
            int stockId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var stock = await _stockRepository.GetByIdAsync(stockId);
            if (stock == null)
            {
                throw new KeyNotFoundException($"Stock with ID {stockId} was not found.");
            }

            return await ProcessShareholdingAsync(stock, forceRefresh, cancellationToken);
        }

        public async Task<StockShareholdingResponseDto> GetShareholdingBySymbolAsync(
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

            return await ProcessShareholdingAsync(stock, forceRefresh, cancellationToken);
        }

        private async Task<StockShareholdingResponseDto> ProcessShareholdingAsync(
            Stock stock,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var existingEntities = await _shareholdingRepository.GetShareholdingsByStockIdAsync(stock.Id, 12);

            bool isFresh = false;
            if (existingEntities.Count > 0)
            {
                var lastSync = existingEntities.Max(e => e.LastSyncedAt);
                isFresh = (DateTime.UtcNow - lastSync).TotalDays < 7;
            }

            if (!forceRefresh && existingEntities.Count > 0 && isFresh)
            {
                _logger.LogInformation("Serving {Count} shareholding records from DB cache for stock {Symbol} (StockId: {StockId}, LastSynced: {LastSynced}).",
                    existingEntities.Count, stock.Symbol, stock.Id, existingEntities.Max(e => e.LastSyncedAt));
                return BuildResponseDto(stock, existingEntities);
            }

            // Acquire per-stock lock to prevent concurrent redundant external sync calls
            var stockLock = StockLocks.GetOrAdd(stock.Id, _ => new SemaphoreSlim(1, 1));
            await stockLock.WaitAsync(cancellationToken);

            try
            {
                // Double-check DB inside lock if not a force-refresh
                if (!forceRefresh)
                {
                    existingEntities = await _shareholdingRepository.GetShareholdingsByStockIdAsync(stock.Id, 12);
                    if (existingEntities.Count > 0)
                    {
                        var lastSync = existingEntities.Max(e => e.LastSyncedAt);
                        if ((DateTime.UtcNow - lastSync).TotalDays < 7)
                        {
                            return BuildResponseDto(stock, existingEntities);
                        }
                    }
                }

                _logger.LogInformation("Syncing shareholding data from canonical provider (IndianAPI) for stock {Symbol} (StockId: {StockId}, ForceRefresh: {ForceRefresh}).",
                    stock.Symbol, stock.Id, forceRefresh);

                await SyncShareholdingFromProviderAsync(stock, cancellationToken);

                // Fetch updated records from database
                existingEntities = await _shareholdingRepository.GetShareholdingsByStockIdAsync(stock.Id, 12);
                if (existingEntities.Count == 0)
                {
                    throw new ProviderNotFoundException(stock.Symbol, $"Shareholding data is currently not available for {stock.Symbol}.");
                }

                return BuildResponseDto(stock, existingEntities);
            }
            finally
            {
                stockLock.Release();
            }
        }

        private async Task SyncShareholdingFromProviderAsync(Stock stock, CancellationToken cancellationToken)
        {
            IReadOnlyList<IndianApiNormalizedQuarterRecord>? records = null;

            // 1. Attempt primary canonical sync via IndianAPI
            try
            {
                _logger.LogInformation("Fetching quarterly shareholding for {Symbol} from IndianAPI", stock.Symbol);
                records = await _indianApiClient.GetQuarterlyShareholdingAsync(stock.Symbol, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IndianAPI historical shareholding request failed for {Symbol}: {Message}.",
                    stock.Symbol, ex.Message);
            }

            // 2. If IndianAPI failed at transport/provider level, fallback to safe backup provider
            if (records == null || records.Count == 0)
            {
                try
                {
                    _logger.LogInformation("Attempting fallback shareholding fetch for stock {Symbol}", stock.Symbol);
                    var bharatRecords = await _bharatStockProvider.GetShareholdingAsync(stock.Symbol, cancellationToken);

                    if (bharatRecords != null && bharatRecords.Count > 0)
                    {
                        records = bharatRecords.Select(b => new IndianApiNormalizedQuarterRecord
                        {
                            Period = b.ResolvedPeriod,
                            PeriodKey = b.ResolvedPeriodKey,
                            PeriodDate = b.ResolvedPeriodDate,
                            PeriodType = b.PeriodType ?? "Quarterly",
                            PromoterHolding = b.PromoterHolding,
                            FiiHolding = b.FiiHolding,
                            DiiHolding = b.DiiHolding,
                            PublicHolding = b.PublicHolding,
                            Source = !string.IsNullOrWhiteSpace(b.Source) ? b.Source : "IndianAPI"
                        }).ToList();
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx, "Backup shareholding provider failed for stock {Symbol}", stock.Symbol);
                }
            }

            if (records == null || records.Count == 0)
            {
                _logger.LogWarning("No shareholding records could be retrieved from either provider for stock {Symbol}.", stock.Symbol);
                return;
            }

            var now = DateTime.UtcNow;

            foreach (var record in records)
            {
                var periodKey = record.PeriodKey;

                // Deterministic PeriodKey check: do not persist invalid unkeyed records
                if (string.IsNullOrWhiteSpace(periodKey))
                {
                    _logger.LogWarning("Skipping shareholding record for {Symbol} because deterministic PeriodKey could not be resolved. Period: '{Period}'",
                        stock.Symbol, record.Period);
                    continue;
                }

                var promoter = ClampHolding(record.PromoterHolding);
                var fii = ClampHolding(record.FiiHolding);
                var dii = ClampHolding(record.DiiHolding);
                var govt = ClampHolding(record.GovernmentHolding);
                var pub = ClampHolding(record.PublicHolding);
                var other = ClampHolding(record.OtherHolding);
                var shareholders = record.ShareholdersCount;

                var existing = await _shareholdingRepository.GetByStockIdAndPeriodKeyAsync(stock.Id, periodKey);
                if (existing != null)
                {
                    // Update existing period labels
                    existing.Period = !string.IsNullOrWhiteSpace(record.Period) ? record.Period : existing.Period;
                    existing.PeriodDate = record.PeriodDate ?? existing.PeriodDate;
                    existing.PeriodType = record.PeriodType ?? existing.PeriodType;

                    // Safe Non-Null Overwrite: Never overwrite existing non-null database values with incoming nulls
                    if (promoter.HasValue) existing.PromoterHolding = promoter;
                    if (fii.HasValue) existing.FiiHolding = fii;
                    if (dii.HasValue) existing.DiiHolding = dii;
                    if (govt.HasValue) existing.GovernmentHolding = govt;
                    if (pub.HasValue) existing.PublicHolding = pub;
                    if (other.HasValue) existing.OtherHolding = other;
                    if (shareholders.HasValue) existing.ShareholdersCount = shareholders;

                    // Update source matching whichever provider actually supplied the data
                    existing.Source = !string.IsNullOrWhiteSpace(record.Source) ? record.Source : existing.Source;
                    existing.LastSyncedAt = now;
                    existing.UpdatedAt = now;

                    await _shareholdingRepository.UpdateAsync(existing);
                }
                else
                {
                    // Insert new period
                    var newEntity = new StockShareholding
                    {
                        StockId = stock.Id,
                        PeriodKey = periodKey,
                        Period = !string.IsNullOrWhiteSpace(record.Period) ? record.Period : periodKey,
                        PeriodDate = record.PeriodDate,
                        PeriodType = record.PeriodType ?? "Quarterly",
                        PromoterHolding = promoter,
                        FiiHolding = fii,
                        DiiHolding = dii,
                        GovernmentHolding = govt,
                        PublicHolding = pub,
                        OtherHolding = other,
                        ShareholdersCount = shareholders,
                        Source = !string.IsNullOrWhiteSpace(record.Source) ? record.Source : "IndianAPI",
                        LastSyncedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    await _shareholdingRepository.AddAsync(newEntity);
                }
            }

            await _shareholdingRepository.SaveChangesAsync();
        }

        private static decimal? ClampHolding(decimal? value)
        {
            if (!value.HasValue) return null;
            return Math.Round(Math.Max(0.00m, Math.Min(100.00m, value.Value)), 2);
        }

        private StockShareholdingResponseDto BuildResponseDto(Stock stock, List<StockShareholding> entities)
        {
            if (entities.Count == 0)
            {
                throw new KeyNotFoundException($"No shareholding data found for stock {stock.Symbol}.");
            }

            // Entities are ordered descending by PeriodDate/PeriodKey (latest first)
            var historyDtos = entities.Select(e =>
            {
                var dto = _mapper.Map<ShareholdingPeriodDto>(e);
                dto.DataAsOf = e.PeriodDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? e.Period;
                return dto;
            }).ToList();

            var current = historyDtos[0];
            ShareholdingPeriodDto? previous = historyDtos.Count > 1 ? historyDtos[1] : null;

            var change = new ShareholdingChangeDto();
            var relChange = new ShareholdingRelativeChangeDto();

            if (previous != null)
            {
                // Absolute percentage-point movement: current - previous
                change.Promoter = CalculatePpChange(current.Promoter, previous.Promoter);
                change.Fii = CalculatePpChange(current.Fii, previous.Fii);
                change.Dii = CalculatePpChange(current.Dii, previous.Dii);
                change.Government = CalculatePpChange(current.Government, previous.Government);
                change.Public = CalculatePpChange(current.Public, previous.Public);
                change.Others = CalculatePpChange(current.Others, previous.Others);

                // Relative percentage change: (current - previous) / previous * 100
                relChange.Promoter = CalculateRelativeChange(current.Promoter, previous.Promoter);
                relChange.Fii = CalculateRelativeChange(current.Fii, previous.Fii);
                relChange.Dii = CalculateRelativeChange(current.Dii, previous.Dii);
                relChange.Government = CalculateRelativeChange(current.Government, previous.Government);
                relChange.Public = CalculateRelativeChange(current.Public, previous.Public);
                relChange.Others = CalculateRelativeChange(current.Others, previous.Others);
            }

            // Reconciliation check with consistent ±0.50 percentage-point tolerance
            var totalSum = current.Total ?? 0.00m;
            var isTotalReconciled = Math.Abs(totalSum - 100.00m) <= 0.50m;
            var validation = new ShareholdingValidationSummaryDto
            {
                IsValid = isTotalReconciled,
                TotalPercentage = totalSum,
                Notes = isTotalReconciled
                    ? "Ownership categories successfully reconcile."
                    : $"Sum of categories ({totalSum}%) deviates from 100% (tolerance: ±0.50%)."
            };

            var latestEntity = entities[0];

            return new StockShareholdingResponseDto
            {
                StockId = stock.Id,
                Symbol = stock.Symbol,
                Exchange = stock.Exchange,
                CompanyName = stock.Company?.CompanyName ?? $"{stock.Symbol} Limited",
                CurrentPeriod = current,
                PreviousPeriod = previous,
                Change = change,
                RelativeChange = relChange,
                History = historyDtos,
                DataAsOf = current.DataAsOf,
                Source = latestEntity.Source,
                LastSyncedAt = latestEntity.LastSyncedAt,
                Validation = validation
            };
        }

        private static decimal? CalculatePpChange(decimal? current, decimal? previous)
        {
            if (current.HasValue && previous.HasValue)
            {
                return Math.Round(current.Value - previous.Value, 2);
            }
            return null;
        }

        private static decimal? CalculateRelativeChange(decimal? current, decimal? previous)
        {
            if (current.HasValue && previous.HasValue && previous.Value > 0)
            {
                return Math.Round(((current.Value - previous.Value) / previous.Value) * 100, 2);
            }
            return null;
        }
    }
}
