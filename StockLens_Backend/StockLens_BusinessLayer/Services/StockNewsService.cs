using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.GoogleNews;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockNewsService : IStockNewsService
    {
        private readonly IStockRepository _stockRepository;
        private readonly IStockNewsRepository _newsRepository;
        private readonly IGoogleNewsClient _googleNewsClient;
        private readonly IMapper _mapper;
        private readonly IndianApiSettings _settings;
        private readonly ILogger<StockNewsService> _logger;
        private readonly IYahooFinanceClient _yahooFinanceClient;

        public StockNewsService(
            IStockRepository stockRepository,
            IStockNewsRepository newsRepository,
            IGoogleNewsClient googleNewsClient,
            IMapper mapper,
            IOptions<IndianApiSettings> settings,
            ILogger<StockNewsService> logger,
            IYahooFinanceClient yahooFinanceClient)
        {
            _stockRepository = stockRepository;
            _newsRepository = newsRepository;
            _googleNewsClient = googleNewsClient;
            _mapper = mapper;
            _settings = settings.Value;
            _logger = logger;
            _yahooFinanceClient = yahooFinanceClient;
        }

        public async Task<StockNewsResponseDto> GetLatestNewsBySymbolAsync(
            string symbol,
            string? exchange = "NSE",
            int limit = 15,
            int page = 1,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();

            var stock = await _stockRepository.GetBySymbolAsync(cleanSymbol, cleanExchange);
            if (stock == null)
            {
                var details = await _yahooFinanceClient.GetCompanyDetailsAsync(cleanSymbol, cleanExchange, cancellationToken);
                stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, cleanExchange, details.CompanyName, details.Industry, cancellationToken);
            }

            return await ProcessStockNewsAsync(stock, limit, page, forceRefresh, cancellationToken);
        }

        public async Task<IEnumerable<StockDto>> GetAllStocksAsync()
        {
            var stocks = await _stockRepository.GetAllStocksAsync();
            return _mapper.Map<IEnumerable<StockDto>>(stocks);
        }

        private async Task<StockNewsResponseDto> ProcessStockNewsAsync(
            Stock stock,
            int limit,
            int page,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var lastFetchedAt = await _newsRepository.GetLatestFetchedAtByStockIdAsync(stock.Id);
            var isCacheFresh = lastFetchedAt.HasValue &&
                (DateTime.UtcNow - lastFetchedAt.Value).TotalMinutes < _settings.NewsCacheMinutes;

            var existingEntities = (await _newsRepository.GetNewsByStockIdAsync(stock.Id, limit, page)).ToList();

            // Detect if previously cached news rows have raw HTML tags (<a href, <font, etc.) or if cache is stale
            bool hasCorruptHtmlData = existingEntities.Any(e =>
                !string.IsNullOrWhiteSpace(e.Description) && (e.Description.Contains("<a href") || e.Description.Contains("<font") || e.Description.Contains("&nbsp;")));

            if (forceRefresh || !isCacheFresh || hasCorruptHtmlData || existingEntities.Count == 0)
            {
                _logger.LogInformation(
                    "News cache stale, corrupt HTML detected, or refresh requested for Stock {Symbol} (StockId: {StockId}, ForceRefresh: {ForceRefresh}, CorruptHtml: {CorruptHtml}). Syncing news.",
                    stock.Symbol, stock.Id, forceRefresh, hasCorruptHtmlData);

                await SyncNewsFromGoogleNewsAsync(stock, forceRefresh || hasCorruptHtmlData, cancellationToken);
                existingEntities = (await _newsRepository.GetNewsByStockIdAsync(stock.Id, limit, page)).ToList();
            }
            else
            {
                _logger.LogInformation(
                    "Serving news from DB cache for Stock {Symbol} (StockId: {StockId}, LastFetched: {LastFetchedAt}, Count: {Count}).",
                    stock.Symbol, stock.Id, lastFetchedAt, existingEntities.Count);
            }

            var newsDtos = _mapper.Map<List<StockNewsItemDto>>(existingEntities);

            // Restore the full URL (which is stored in Content to avoid DB limits)
            foreach (var dto in newsDtos)
            {
                var entity = existingEntities.FirstOrDefault(e => e.Id == dto.Id);
                if (entity != null && !string.IsNullOrWhiteSpace(entity.Content))
                {
                    dto.SourceUrl = entity.Content;
                    dto.ArticleUrl = entity.Content;
                }
            }

            return new StockNewsResponseDto
            {
                StockId = stock.Id,
                Symbol = stock.Symbol,
                Exchange = stock.Exchange,
                CompanyName = stock.Company?.CompanyName ?? stock.Symbol,
                News = newsDtos,
                Page = page,
                Limit = limit,
                LastFetchedAt = lastFetchedAt ?? DateTime.UtcNow
            };
        }

        private async Task SyncNewsFromGoogleNewsAsync(Stock stock, bool clearExisting, CancellationToken cancellationToken)
        {
            try
            {
                if (clearExisting)
                {
                    _logger.LogInformation("Purging previous cached news for {Symbol} prior to fresh sync.", stock.Symbol);
                    await _newsRepository.DeleteOldNewsAsync(stock.Id, 0);
                    await _newsRepository.SaveChangesAsync();
                }

                _logger.LogInformation("Fetching news from Google News RSS for {Symbol}...", stock.Symbol);
                var articles = await _googleNewsClient.GetStockNewsAsync(stock.Symbol, stock.Company?.CompanyName, cancellationToken);

                if (articles == null || articles.Count == 0)
                {
                    _logger.LogInformation("No articles returned from Google News RSS for {Symbol}.", stock.Symbol);
                    return;
                }

                var externalIds = articles
                    .Where(a => !string.IsNullOrWhiteSpace(a.ExternalNewsId))
                    .Select(a => a.ExternalNewsId!)
                    .ToList();

                var sourceUrls = articles
                    .Where(a => !string.IsNullOrWhiteSpace(a.SourceUrl))
                    .Select(a => a.SourceUrl)
                    .ToList();

                var existingExternalIds = await _newsRepository.GetExistingExternalNewsIdsAsync(stock.Id, externalIds);
                var existingSourceUrls = await _newsRepository.GetExistingSourceUrlsAsync(stock.Id, sourceUrls);

                var now = DateTime.UtcNow;
                var newEntities = new List<StockNews>();

                foreach (var article in articles)
                {
                    // Deduplication check: ExternalNewsId or SourceUrl
                    if (!string.IsNullOrWhiteSpace(article.ExternalNewsId) && existingExternalIds.Contains(article.ExternalNewsId))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(article.SourceUrl) && existingSourceUrls.Contains(article.SourceUrl))
                    {
                        continue;
                    }

                    var entity = _mapper.Map<StockNews>(article);
                    entity.StockId = stock.Id;
                    entity.Description = CleanHtmlSnippet(article.Description, article.Title);
                    entity.FetchedAt = now;
                    entity.CreatedAt = now;
                    entity.UpdatedAt = now;

                    newEntities.Add(entity);

                    // Track in local set for intra-batch duplicate prevention
                    if (!string.IsNullOrWhiteSpace(article.ExternalNewsId))
                    {
                        existingExternalIds.Add(article.ExternalNewsId);
                    }
                    if (!string.IsNullOrWhiteSpace(article.SourceUrl))
                    {
                        existingSourceUrls.Add(article.SourceUrl);
                    }
                }

                if (newEntities.Count > 0)
                {
                    _logger.LogInformation("Saving {Count} new stock news articles for {Symbol} (StockId: {StockId}).", newEntities.Count, stock.Symbol, stock.Id);
                    await _newsRepository.AddRangeAsync(newEntities);
                    await _newsRepository.SaveChangesAsync();

                    await _newsRepository.DeleteOldNewsAsync(stock.Id, 20);
                    await _newsRepository.SaveChangesAsync();
                }
                else
                {
                    _logger.LogInformation("All {Count} fetched articles for {Symbol} are already present in DB.", articles.Count, stock.Symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing news from Google News RSS for stock {Symbol} (ID: {StockId})", stock.Symbol, stock.Id);
            }
        }

        private static string CleanHtmlSnippet(string? raw, string? title)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var clean = Regex.Replace(raw, "<.*?>", string.Empty);
            clean = System.Net.WebUtility.HtmlDecode(clean);
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(title) && string.Equals(clean, title.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            return clean;
        }
    }
}
