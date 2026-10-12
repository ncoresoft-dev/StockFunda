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
    public class BharatStockShareholdingProvider : IShareholdingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly BharatStockSettings _settings;
        private readonly ILogger<BharatStockShareholdingProvider> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        public BharatStockShareholdingProvider(
            HttpClient httpClient,
            IOptions<BharatStockSettings> settings,
            ILogger<BharatStockShareholdingProvider> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<BharatStockShareholdingRecord>> GetShareholdingAsync(
            string ticker,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                throw new ArgumentException("Ticker symbol is required.", nameof(ticker));
            }

            var cleanTicker = ticker.Trim().ToUpperInvariant();
            var endpoint = $"v1/stocks/{Uri.EscapeDataString(cleanTicker)}/shareholding";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Requesting shareholding data from BharatStock for ticker: {Ticker} via {Endpoint}", cleanTicker, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Timeout occurred while contacting BharatStock API for ticker {Ticker}", cleanTicker);
                throw new ProviderApiException($"BharatStock API request timed out for ticker '{cleanTicker}'.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP network failure contacting BharatStock API for ticker {Ticker}", cleanTicker);
                throw new ProviderApiException($"Network error connecting to BharatStock API: {ex.Message}", innerException: ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned {StatusCode} Unauthorized for ticker {Ticker}. Response: {ErrorBody}", (int)response.StatusCode, cleanTicker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned 401 Unauthorized: {errorBody}", (int)response.StatusCode);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("BharatStock returned 404 Not Found for ticker: {Ticker}", cleanTicker);
                    throw new ProviderNotFoundException(cleanTicker, $"Shareholding data is not available for ticker '{cleanTicker}'.");
                }

                if (response.StatusCode == (HttpStatusCode)429) // Too Many Requests
                {
                    _logger.LogWarning("BharatStock API rate limit exceeded (HTTP 429) for ticker {Ticker}. Falling back to mock shareholding.", cleanTicker);
                    return MockShareholdingProvider.GenerateMockHistory(cleanTicker);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned error status {StatusCode} for ticker {Ticker}. Response: {ErrorBody}", statusCode, cleanTicker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine("Raw JSON:");
                Console.WriteLine(json);
                return ParseShareholdingResponse(json, cleanTicker);
            }
        }

        public static IReadOnlyList<BharatStockShareholdingRecord> ParseShareholdingResponse(string json, string ticker)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<BharatStockShareholdingRecord>();
            }

            var trimmed = json.Trim();

            // 1. Direct JSON Array: [ { promoter_holding: ... }, ... ]
            if (trimmed.StartsWith("["))
            {
                var records = JsonSerializer.Deserialize<List<BharatStockShareholdingRecord>>(json, JsonOptions);
                Console.WriteLine("Deserialized Records:");
                Console.WriteLine(JsonSerializer.Serialize(records, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                return records ?? (IReadOnlyList<BharatStockShareholdingRecord>)Array.Empty<BharatStockShareholdingRecord>();
            }

            // 2. Wrapped Object or Single Object
            if (trimmed.StartsWith("{"))
            {
                var wrapper = JsonSerializer.Deserialize<BharatStockApiResponseWrapper>(json, JsonOptions);
                Console.WriteLine("Deserialized wrapper:");
                Console.WriteLine(JsonSerializer.Serialize(wrapper, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                if (wrapper != null)
                {
                    if (wrapper.Shareholding != null && wrapper.Shareholding.Count > 0)
                        return wrapper.Shareholding;

                    if (wrapper.Data != null && wrapper.Data.Count > 0)
                        return wrapper.Data;

                    if (wrapper.History != null && wrapper.History.Count > 0)
                        return wrapper.History;
                }

                // Try parsing as single record object
                var singleRecord = JsonSerializer.Deserialize<BharatStockShareholdingRecord>(json, JsonOptions);
                if (singleRecord != null &&
                    (singleRecord.PromoterHolding.HasValue ||
                     singleRecord.FiiHolding.HasValue ||
                     singleRecord.DiiHolding.HasValue ||
                     singleRecord.PublicHolding.HasValue))
                {
                    return new List<BharatStockShareholdingRecord> { singleRecord };
                }
            }

            return Array.Empty<BharatStockShareholdingRecord>();
        }
    }
}
