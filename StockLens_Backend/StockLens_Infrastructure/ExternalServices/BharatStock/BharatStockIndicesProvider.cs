using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace StockLens_Infrastructure.ExternalServices.BharatStock
{
    public class BharatStockIndicesProvider : IIndicesProvider
    {
        private readonly HttpClient _httpClient;
        private readonly BharatStockSettings _settings;
        private readonly ILogger<BharatStockIndicesProvider> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        public BharatStockIndicesProvider(
            HttpClient httpClient,
            IOptions<BharatStockSettings> settings,
            ILogger<BharatStockIndicesProvider> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
            _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        }

        public async Task<List<BharatStockIndexModel>> GetIndicesAsync(string? category = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var endpoint = "v1/indices";
                if (!string.IsNullOrWhiteSpace(category))
                {
                    endpoint += $"?category={HttpUtility.UrlEncode(category)}";
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                    if (!string.IsNullOrWhiteSpace(cleanApiKey))
                    {
                        request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                    }
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                var wrapper = JsonSerializer.Deserialize<BharatStockDealsApiResponseWrapper<BharatStockIndexModel>>(json, JsonOptions);
                return wrapper?.Data ?? new List<BharatStockIndexModel>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching indices from BharatStock");
                return new List<BharatStockIndexModel>();
            }
        }

        public async Task<JsonElement> GetIndexCompaniesAsync(string indexName, CancellationToken cancellationToken = default)
        {
            try
            {
                var endpoint = $"v1/indices/{HttpUtility.UrlEncode(indexName)}/companies";
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                    if (!string.IsNullOrWhiteSpace(cleanApiKey))
                    {
                        request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                    }
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching index companies from BharatStock for index {IndexName}", indexName);
                return default;
            }
        }

        public async Task<JsonElement> GetTop50CompaniesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var endpoint = "v1/screener?sort_by=market_cap&sort_order=desc&page_size=50";
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                    if (!string.IsNullOrWhiteSpace(cleanApiKey))
                    {
                        request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                    }
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching top 50 companies via screener");
                return default;
            }
        }
    }
}
