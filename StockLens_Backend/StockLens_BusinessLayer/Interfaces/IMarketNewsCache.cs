using StockLens_BusinessLayer.DTOs;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IMarketNewsCache
    {
        MarketNewsResponseDto GetResponse(int limit = 10, string? category = null);
        IReadOnlyList<MarketNewsItemDto> GetArticles();
        void UpdateArticles(IEnumerable<IndianApiStandardArticle> articles);
        void RecordFailure(string errorMessage, int nextRetryMinutes);
        DateTime? LastFetchedAt { get; }
        string? LastError { get; }
        bool IsHealthy { get; }
        int FailureCount { get; }
        int NextRetryInMinutes { get; }
    }
}
