using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockLens_BusinessLayer.Services
{
    public class MarketNewsCache : IMarketNewsCache
    {
        private readonly object _syncLock = new();
        private readonly ILogger<MarketNewsCache> _logger;

        private List<MarketNewsItemDto> _articles = new();
        private DateTime? _lastFetchedAt;
        private string? _lastError;
        private bool _isHealthy = true;
        private int _failureCount = 0;
        private int _nextRetryInMinutes = 1;

        public MarketNewsCache(ILogger<MarketNewsCache> logger)
        {
            _logger = logger;
        }

        public DateTime? LastFetchedAt
        {
            get { lock (_syncLock) return _lastFetchedAt; }
        }

        public string? LastError
        {
            get { lock (_syncLock) return _lastError; }
        }

        public bool IsHealthy
        {
            get { lock (_syncLock) return _isHealthy; }
        }

        public int FailureCount
        {
            get { lock (_syncLock) return _failureCount; }
        }

        public int NextRetryInMinutes
        {
            get { lock (_syncLock) return _nextRetryInMinutes; }
        }

        public IReadOnlyList<MarketNewsItemDto> GetArticles()
        {
            lock (_syncLock)
            {
                return _articles.ToList();
            }
        }

        public MarketNewsResponseDto GetResponse(int limit = 10, string? category = null)
        {
            lock (_syncLock)
            {
                var query = _articles.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(a => string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase));
                }

                var filteredList = query.Take(Math.Clamp(limit, 1, 50)).ToList();

                // Re-calculate relative time
                foreach (var item in filteredList)
                {
                    item.TimeAgo = FormatTimeAgo(item.PublishedAt);
                }

                var hasArticles = _articles.Count > 0;
                var status = hasArticles ? (_isHealthy ? "ok" : "degraded") : (_isHealthy ? "empty" : "error");
                var message = _lastError ?? (!hasArticles ? "No market news articles available." : null);

                return new MarketNewsResponseDto
                {
                    News = filteredList,
                    TotalCount = _articles.Count,
                    LastFetchedAt = _lastFetchedAt,
                    IsCached = hasArticles,
                    Status = status,
                    Message = message,
                    NextRetryInMinutes = _nextRetryInMinutes
                };
            }
        }

        public void UpdateArticles(IEnumerable<IndianApiStandardArticle> articles)
        {
            if (articles == null) return;

            var dtoList = articles.Select(a =>
            {
                var url = !string.IsNullOrWhiteSpace(a.Content) ? a.Content : a.SourceUrl;
                var isHot = (a.Title ?? "").ToLowerInvariant().ContainsAny("surge", "jump", "record high", "plunge", "crash", "rbi", "rate cut", "budget");

                return new MarketNewsItemDto
                {
                    Id = a.ExternalNewsId ?? Guid.NewGuid().ToString("N"),
                    Title = a.Title ?? string.Empty,
                    Description = a.Description,
                    Content = a.Content,
                    SourceName = string.IsNullOrWhiteSpace(a.SourceName) ? "Market Wire" : a.SourceName,
                    SourceUrl = a.SourceUrl ?? string.Empty,
                    ArticleUrl = url ?? string.Empty,
                    ImageUrl = a.ImageUrl,
                    PublishedAt = a.PublishedAt,
                    Category = string.IsNullOrWhiteSpace(a.Category) ? "Market Updates" : a.Category,
                    TimeAgo = FormatTimeAgo(a.PublishedAt),
                    IsHot = isHot
                };
            }).ToList();

            lock (_syncLock)
            {
                _articles = dtoList;
                _lastFetchedAt = DateTime.UtcNow;
                _lastError = null;
                _isHealthy = true;
                _failureCount = 0;
                _nextRetryInMinutes = 1;
            }

            _logger.LogInformation("MarketNewsCache updated with {Count} articles at UTC {Time}.", dtoList.Count, _lastFetchedAt);
        }

        public void RecordFailure(string errorMessage, int nextRetryMinutes)
        {
            lock (_syncLock)
            {
                _lastError = errorMessage;
                _failureCount++;
                _nextRetryInMinutes = nextRetryMinutes;
                _isHealthy = _articles.Count > 0; // If we already have articles, service remains available in degraded state
            }

            _logger.LogWarning("MarketNewsCache failure recorded: {Error}. Total consecutive failures: {Failures}. Next retry in {Minutes} min.",
                errorMessage, _failureCount, nextRetryMinutes);
        }

        private static string FormatTimeAgo(DateTime dt)
        {
            var diff = DateTime.UtcNow - dt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return $"{(int)diff.TotalDays}d ago";
        }
    }

    internal static class StringExtensions
    {
        public static bool ContainsAny(this string text, params string[] values)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var val in values)
            {
                if (text.Contains(val, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
