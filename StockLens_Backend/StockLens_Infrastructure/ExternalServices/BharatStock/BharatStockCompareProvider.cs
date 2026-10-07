using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions;

namespace StockLens_Infrastructure.ExternalServices.BharatStock
{
    public class BharatStockCompareProvider : ICompareProvider
    {
        private readonly HttpClient _httpClient;
        private readonly BharatStockSettings _settings;
        private readonly ILogger<BharatStockCompareProvider> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public BharatStockCompareProvider(
            HttpClient httpClient,
            IOptions<BharatStockSettings> settings,
            ILogger<BharatStockCompareProvider> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<BharatStockCompareRecord>> GetPeersBySectorAsync(
            string sector,
            string sort = "market_cap",
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sector))
            {
                return Array.Empty<BharatStockCompareRecord>();
            }

            var cleanSector = sector.Trim();
            var endpoint = $"v1/stocks/compare?sector={Uri.EscapeDataString(cleanSector)}&sort={Uri.EscapeDataString(sort)}&limit={limit}";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Requesting peer comparison data from BharatStock for sector: {Sector} via {Endpoint}", cleanSector, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Timeout occurred while contacting BharatStock Compare API for sector {Sector}", cleanSector);
                throw new ProviderApiException($"BharatStock API request timed out for sector '{cleanSector}'.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP network failure contacting BharatStock Compare API for sector {Sector}", cleanSector);
                throw new ProviderApiException($"Network error connecting to BharatStock API: {ex.Message}", innerException: ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new ProviderApiException($"BharatStock API returned 401 Unauthorized: {errorBody}", (int)response.StatusCode);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("BharatStock returned 404 Not Found for sector compare of sector: {Sector}", cleanSector);
                    return Array.Empty<BharatStockCompareRecord>();
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    throw new ProviderRateLimitException($"Rate limit exceeded for BharatStock API.", retryAfter);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned error status {StatusCode} for sector compare of sector {Sector}. Response: {ErrorBody}", statusCode, cleanSector, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Raw BharatStock JSON response for sector {Sector}: {Json}", cleanSector, json);
                return ParseCompareListResponse(json);
            }
        }

        public static IReadOnlyList<BharatStockCompareRecord> ParseCompareListResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<BharatStockCompareRecord>();
            }

            var trimmed = json.Trim();

            // 1. Direct array
            if (trimmed.StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<BharatStockCompareRecord>>(json, JsonOptions);
                return list ?? (IReadOnlyList<BharatStockCompareRecord>)Array.Empty<BharatStockCompareRecord>();
            }

            // 2. Wrapped object
            if (trimmed.StartsWith("{"))
            {
                var wrapper = JsonSerializer.Deserialize<BharatStockCompareApiResponseWrapper>(json, JsonOptions);
                if (wrapper?.Data != null && wrapper.Data.Count > 0)
                {
                    return wrapper.Data;
                }

                var single = JsonSerializer.Deserialize<BharatStockCompareRecord>(json, JsonOptions);
                if (single != null && !string.IsNullOrWhiteSpace(single.Symbol))
                {
                    return new List<BharatStockCompareRecord> { single };
                }
            }

            return Array.Empty<BharatStockCompareRecord>();
        }
    }
}
