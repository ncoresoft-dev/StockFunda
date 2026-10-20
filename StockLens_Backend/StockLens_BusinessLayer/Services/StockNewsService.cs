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
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockNewsService : IStockNewsService
    {
        private readonly IStockRepository _stockRepository;
        private readonly IStockNewsRepository _newsRepository;
        private readonly IIndianApiNewsClient _indianApiClient;
        private readonly IGoogleNewsClient _googleNewsClient;
        private readonly IMapper _mapper;
        private readonly IndianApiSettings _settings;
        private readonly ILogger<StockNewsService> _logger;
        private readonly IYahooFinanceClient _yahooFinanceClient;
        private readonly ICompanyRepository _companyRepository;

        public StockNewsService(
            IStockRepository stockRepository,
            IStockNewsRepository newsRepository,
            IIndianApiNewsClient indianApiClient,
            IGoogleNewsClient googleNewsClient,
            IMapper mapper,
            IOptions<IndianApiSettings> settings,
            ILogger<StockNewsService> logger,
            IYahooFinanceClient yahooFinanceClient,
            ICompanyRepository companyRepository)
        {
            _stockRepository = stockRepository;
            _newsRepository = newsRepository;
            _indianApiClient = indianApiClient;
            _googleNewsClient = googleNewsClient;
            _mapper = mapper;
            _settings = settings.Value;
            _logger = logger;
            _yahooFinanceClient = yahooFinanceClient;
            _companyRepository = companyRepository;
        }

        public async Task<StockNewsResponseDto> GetLatestNewsByStockIdAsync(
            int stockId,
            int limit = 5,
            int page = 1,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var stock = await _stockRepository.GetByIdAsync(stockId);
            if (stock == null)
            {
                throw new KeyNotFoundException($"Stock with ID {stockId} was not found.");
            }

            return await ProcessStockNewsAsync(stock, limit, page, forceRefresh, cancellationToken);
        }

        public async Task<StockNewsResponseDto> GetLatestNewsBySymbolAsync(
            string symbol,
            string? exchange = "NSE",
            int limit = 5,
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

            if (forceRefresh || !isCacheFresh)
            {
                _logger.LogInformation(
                    "News cache stale or refresh requested for Stock {Symbol} (StockId: {StockId}, ForceRefresh: {ForceRefresh}). Fetching from Google News.",
                    stock.Symbol, stock.Id, forceRefresh);

                await SyncNewsFromIndianApiAsync(stock, cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "Serving news from DB cache for Stock {Symbol} (StockId: {StockId}, LastFetched: {LastFetchedAt}).",
                    stock.Symbol, stock.Id, lastFetchedAt);
            }

            var newsEntities = (await _newsRepository.GetNewsByStockIdAsync(stock.Id, limit, page)).ToList();

            // If cache was considered fresh but DB has 0 items, auto-sync to fetch news
            if (!forceRefresh && isCacheFresh && newsEntities.Count == 0)
            {
                _logger.LogInformation("Stock {Symbol} has 0 news in DB. Triggering fresh sync from Google News.", stock.Symbol);
                await SyncNewsFromIndianApiAsync(stock, cancellationToken);
                newsEntities = (await _newsRepository.GetNewsByStockIdAsync(stock.Id, limit, page)).ToList();
            }

            var newsDtos = _mapper.Map<List<StockNewsItemDto>>(newsEntities);

            // Restore the full URL (which is stored in Content to avoid DB limits)
            foreach (var dto in newsDtos)
            {
                var entity = newsEntities.FirstOrDefault(e => e.Id == dto.Id);
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

        private async Task SyncNewsFromIndianApiAsync(Stock stock, CancellationToken cancellationToken)
        {
            try
            {
                // Commented out IndianAPI call as per user request to use Google News
                // var articles = await _indianApiClient.GetStockNewsAsync(stock.Symbol, stock.Company?.CompanyName, cancellationToken);
                
                var articles = await _googleNewsClient.GetStockNewsAsync(stock.Symbol, stock.Company?.CompanyName, cancellationToken);

                if (articles == null || articles.Count == 0)
                {
                    _logger.LogInformation("No articles returned from IndianAPI for {Symbol}.", stock.Symbol);
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

                    await _newsRepository.DeleteOldNewsAsync(stock.Id, 5);
                    await _newsRepository.SaveChangesAsync();
                }
                else
                {
                    _logger.LogInformation("All {Count} fetched articles for {Symbol} are already present in DB.", articles.Count, stock.Symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing news from IndianAPI for stock {Symbol} (ID: {StockId})", stock.Symbol, stock.Id);
            }
        }
    }
}
