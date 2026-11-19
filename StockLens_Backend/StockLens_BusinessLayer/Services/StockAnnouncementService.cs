using AutoMapper;
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
        private readonly ILogger<StockAnnouncementService> _logger;

        public StockAnnouncementService(
            IStockAnnouncementRepository announcementRepository,
            INseAnnouncementClient nseClient,
            IMapper mapper,
            ILogger<StockAnnouncementService> logger)
        {
            _announcementRepository = announcementRepository;
            _nseClient = nseClient;
            _mapper = mapper;
            _logger = logger;
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

            var entities = symbols != null
                ? await _announcementRepository.GetAnnouncementsAsync(symbol, symbols, search, category, limit, page, cancellationToken)
                : await _announcementRepository.GetAnnouncementsAsync(symbol, search, category, limit, page, cancellationToken);

            var totalCount = symbols != null
                ? await _announcementRepository.GetTotalCountAsync(symbol, symbols, search, category, cancellationToken)
                : await _announcementRepository.GetTotalCountAsync(symbol, search, category, cancellationToken);
            var lastSyncedAt = await _announcementRepository.GetLatestBroadcastDateTimeAsync(cancellationToken);
            var categories = await _announcementRepository.GetDistinctCategoriesAsync(cancellationToken);

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
            return await _announcementRepository.GetDistinctCategoriesAsync(cancellationToken);
        }

        public Task<int> SyncLatestAnnouncementsAsync(CancellationToken cancellationToken = default)
        {
            // Default to past 3 months (90 days) so all followed companies have quarterly historical filings
            return SyncLatestAnnouncementsAsync(3, cancellationToken);
        }

        public async Task<int> SyncLatestAnnouncementsAsync(int monthsBack, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("StockAnnouncementService: Polling corporate announcements from NSE for past {Months} months...", monthsBack);

            var istNow = DateTime.UtcNow.AddHours(5.5);
            var fromDate = monthsBack > 0 ? istNow.AddMonths(-monthsBack) : istNow;
            var announcements = await _nseClient.FetchAnnouncementsAsync(fromDate, istNow, cancellationToken);

            if (announcements == null || announcements.Count == 0)
            {
                _logger.LogInformation("StockAnnouncementService: 0 corporate announcements received from NSE.");
                return 0;
            }

            var savedCount = await _announcementRepository.SaveAnnouncementsAsync(announcements, cancellationToken);
            _logger.LogInformation("StockAnnouncementService: Synced {Total} records, persisted {Saved} new records into DB.",
                announcements.Count, savedCount);

            return savedCount;
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
