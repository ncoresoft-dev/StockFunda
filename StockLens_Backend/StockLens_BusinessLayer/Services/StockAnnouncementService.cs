using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.Nse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockAnnouncementService : IStockAnnouncementService
    {
        private readonly IStockAnnouncementRepository _announcementRepository;
        private readonly INseAnnouncementClient _nseClient;
        private readonly IMapper _mapper;
        private readonly IMemoryCache _cache;
        private readonly ILogger<StockAnnouncementService> _logger;

        private const string CategoriesCacheKey = "StockAnnouncements_DistinctCategories";
        private const string LastSyncedAtCacheKey = "StockAnnouncements_LastSyncedAt";
        private static readonly TimeSpan CategoriesCacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan LastSyncedCacheDuration = TimeSpan.FromMinutes(5);

        // Guard against simultaneous redundant syncs within the same service instance
        private readonly SemaphoreSlim _syncLock = new(1, 1);

        public StockAnnouncementService(
            IStockAnnouncementRepository announcementRepository,
            INseAnnouncementClient nseClient,
            IMapper mapper,
            ILogger<StockAnnouncementService> logger,
            IMemoryCache? cache = null)
        {
            _announcementRepository = announcementRepository;
            _nseClient = nseClient;
            _mapper = mapper;
            _logger = logger;
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        }

        // Overload to preserve DI compatibility when IMemoryCache is registered
        public StockAnnouncementService(
            IStockAnnouncementRepository announcementRepository,
            INseAnnouncementClient nseClient,
            IMapper mapper,
            IMemoryCache cache,
            ILogger<StockAnnouncementService> logger)
            : this(announcementRepository, nseClient, mapper, logger, cache)
        {
        }

        public Task<StockAnnouncementResponseDto> GetAnnouncementsAsync(
            string? symbol = null,
            string? search = null,
            string? category = null,
            int limit = 30,
            int page = 1,
            CancellationToken cancellationToken = default)
        {
            return GetAnnouncementsAsync(symbol, null, search, category, limit, page, cancellationToken);
        }

        public async Task<StockAnnouncementResponseDto> GetAnnouncementsAsync(
            string? symbol,
            IEnumerable<string>? symbols,
            string? search,
            string? category,
            int limit = 30,
            int page = 1,
            CancellationToken cancellationToken = default)
        {
            if (limit <= 0) limit = 30;
            if (limit > 100) limit = 100;
            if (page < 1) page = 1;

            // 1. Fetch paged entities using indexed ordering (fast Index Scan / Seek)
            var entities = (symbols != null
                ? await _announcementRepository.GetAnnouncementsAsync(symbol, symbols, search, category, limit, page, cancellationToken)
                : await _announcementRepository.GetAnnouncementsAsync(symbol, search, category, limit, page, cancellationToken)).ToList();

            // 2. Query optimization: If page 1 and returned items < limit, we already know the exact total without a separate COUNT(*) DB query!
            int totalCount;
            if (page == 1 && entities.Count < limit)
            {
                totalCount = entities.Count;
            }
            else
            {
                totalCount = symbols != null
                    ? await _announcementRepository.GetTotalCountAsync(symbol, symbols, search, category, cancellationToken)
                    : await _announcementRepository.GetTotalCountAsync(symbol, search, category, cancellationToken);
            }

            // 3. LastSyncedAt (cached in memory for 5 min to avoid querying on every single page load)
            var lastSyncedAt = await GetLastSyncedAtCachedAsync(cancellationToken);

            // 4. Categories (cached in memory for 30 min to eliminate full-table distinct scans on every request)
            var categories = await GetCategoriesAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var dtos = entities.Select(entity =>
            {
                var dto = _mapper.Map<StockAnnouncementDto>(entity);
                if (entity.BroadcastDateTime.HasValue)
                {
                    dto.TimeAgo = FormatRelativeTime(entity.BroadcastDateTime.Value, now);
                }
                else
                {
                    dto.TimeAgo = FormatRelativeTime(entity.CreatedAt, now);
                }
                return dto;
            }).ToList();

            return new StockAnnouncementResponseDto
            {
                TotalCount = totalCount,
                Page = page,
                Limit = limit,
                LastSyncedAt = lastSyncedAt,
                Categories = categories,
                Announcements = dtos
            };
        }

        public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CategoriesCacheKey, out IReadOnlyList<string>? cached) && cached != null)
            {
                return cached;
            }

            var categories = await _announcementRepository.GetDistinctCategoriesAsync(cancellationToken);
            if (categories.Count > 0)
            {
                _cache.Set(CategoriesCacheKey, categories, CategoriesCacheDuration);
            }
            return categories;
        }

        private async Task<DateTime?> GetLastSyncedAtCachedAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(LastSyncedAtCacheKey, out DateTime? cached))
            {
                return cached;
            }

            var latest = await _announcementRepository.GetLatestBroadcastDateTimeAsync(cancellationToken);
            if (latest.HasValue)
            {
                _cache.Set(LastSyncedAtCacheKey, latest, LastSyncedCacheDuration);
            }
            return latest;
        }

        public Task<int> SyncLatestAnnouncementsAsync(CancellationToken cancellationToken = default)
        {
            return SyncLatestAnnouncementsAsync(3, cancellationToken);
        }

        public async Task<int> SyncLatestAnnouncementsByDaysAsync(int daysBack, CancellationToken cancellationToken = default)
        {
            if (daysBack <= 0) daysBack = 2;

            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("StockAnnouncementService: Polling corporate announcements from NSE for past {Days} days...", daysBack);

                var istNow = DateTime.UtcNow.AddHours(5.5);
                var fromDate = istNow.AddDays(-daysBack);
                var announcements = await _nseClient.FetchAnnouncementsAsync(fromDate, istNow, cancellationToken);

                if (announcements == null || announcements.Count == 0)
                {
                    _logger.LogInformation("StockAnnouncementService: 0 corporate announcements received from NSE.");
                    return 0;
                }

                var savedCount = await _announcementRepository.SaveAnnouncementsAsync(announcements, cancellationToken);
                _logger.LogInformation("StockAnnouncementService: Synced {Total} records, persisted {Saved} new records into DB.",
                    announcements.Count, savedCount);

                _cache.Remove(LastSyncedAtCacheKey);
                _cache.Remove(CategoriesCacheKey);

                return savedCount;
            }
            finally
            {
                _syncLock.Release();
            }
        }

        public async Task<int> SyncLatestAnnouncementsAsync(int monthsBack, CancellationToken cancellationToken = default)
        {
            if (monthsBack <= 0)
            {
                return await SyncLatestAnnouncementsByDaysAsync(2, cancellationToken);
            }

            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("StockAnnouncementService: Polling corporate announcements from NSE for past {Months} months...", monthsBack);

                var istNow = DateTime.UtcNow.AddHours(5.5);
                var fromDate = istNow.AddMonths(-monthsBack);
                var announcements = await _nseClient.FetchAnnouncementsAsync(fromDate, istNow, cancellationToken);

                if (announcements == null || announcements.Count == 0)
                {
                    _logger.LogInformation("StockAnnouncementService: 0 corporate announcements received from NSE.");
                    return 0;
                }

                var savedCount = await _announcementRepository.SaveAnnouncementsAsync(announcements, cancellationToken);
                _logger.LogInformation("StockAnnouncementService: Synced {Total} records, persisted {Saved} new records into DB.",
                    announcements.Count, savedCount);

                _cache.Remove(LastSyncedAtCacheKey);
                _cache.Remove(CategoriesCacheKey);

                return savedCount;
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private static string FormatRelativeTime(DateTime dateTime, DateTime nowUtc)
        {
            // If the date looks like local IST, adjust comparison
            var diff = nowUtc - dateTime;
            if (diff.TotalDays > 30 || diff.TotalSeconds < -3600)
            {
                // If timestamp is ahead (e.g. IST vs UTC) or older than a month, show formatted date
                return dateTime.ToString("dd MMM yyyy, HH:mm");
            }

            if (diff.TotalSeconds < 0) diff = TimeSpan.Zero;

            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";

            return dateTime.ToString("dd MMM yyyy");
        }
    }
}
