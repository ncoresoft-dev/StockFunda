using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions;
using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.BharatStock
{
    public class BharatStockDealsProvider : IDealsProvider
    {
        private readonly HttpClient _httpClient;
        private readonly BharatStockSettings _settings;
        private readonly ILogger<BharatStockDealsProvider> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        public BharatStockDealsProvider(
            HttpClient httpClient,
            IOptions<BharatStockSettings> settings,
            ILogger<BharatStockDealsProvider> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<BharatStockDealRecord>> GetBulkDealsAsync(
            string ticker, string? exchange = "NSE", int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
        {
            var endpoint = $"v1/stocks/{Uri.EscapeDataString(ticker.Trim().ToUpperInvariant())}/bulk-deals?page={page}&page_size={pageSize}";
            if (!string.IsNullOrWhiteSpace(exchange)) endpoint += $"&exchange={Uri.EscapeDataString(exchange.Trim().ToUpperInvariant())}";
            
            return await ExecuteRequestAsync<BharatStockDealRecord>(endpoint, ticker, cancellationToken);
        }

        public async Task<IReadOnlyList<BharatStockDealRecord>> GetBlockDealsAsync(
            string ticker, string? exchange = "NSE", int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
        {
            var endpoint = $"v1/stocks/{Uri.EscapeDataString(ticker.Trim().ToUpperInvariant())}/block-deals?page={page}&page_size={pageSize}";
            if (!string.IsNullOrWhiteSpace(exchange)) endpoint += $"&exchange={Uri.EscapeDataString(exchange.Trim().ToUpperInvariant())}";
            
            return await ExecuteRequestAsync<BharatStockDealRecord>(endpoint, ticker, cancellationToken);
        }

        public async Task<IReadOnlyList<BharatStockInsiderTradeRecord>> GetInsiderTradesAsync(
            string ticker, string? exchange = "NSE", int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
        {
            var endpoint = $"v1/stocks/{Uri.EscapeDataString(ticker.Trim().ToUpperInvariant())}/insider-trades?page={page}&page_size={pageSize}";
            if (!string.IsNullOrWhiteSpace(exchange)) endpoint += $"&exchange={Uri.EscapeDataString(exchange.Trim().ToUpperInvariant())}";
            
            return await ExecuteRequestAsync<BharatStockInsiderTradeRecord>(endpoint, ticker, cancellationToken);
        }

        private async Task<IReadOnlyList<T>> ExecuteRequestAsync<T>(string endpoint, string ticker, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTTP network failure contacting BharatStock Deals API for ticker {Ticker}", ticker);
                throw new ProviderApiException($"Network error connecting to BharatStock API: {ex.Message}", innerException: ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Array.Empty<T>();
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned error status {StatusCode} for ticker {Ticker}. Response: {ErrorBody}", statusCode, ticker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var wrapper = JsonSerializer.Deserialize<BharatStockDealsApiResponseWrapper<T>>(json, JsonOptions);
                
                return wrapper?.Data ?? (IReadOnlyList<T>)Array.Empty<T>();
            }
        }
    }
}
