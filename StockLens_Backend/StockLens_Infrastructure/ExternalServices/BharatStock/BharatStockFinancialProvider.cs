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
    public class BharatStockFinancialProvider : IFinancialProvider
    {
        private readonly HttpClient _httpClient;
        private readonly BharatStockSettings _settings;
        private readonly ILogger<BharatStockFinancialProvider> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        public BharatStockFinancialProvider(
            HttpClient httpClient,
            IOptions<BharatStockSettings> settings,
            ILogger<BharatStockFinancialProvider> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<BharatStockFinancialRecord>> GetFinancialsAsync(
            string ticker,
            string periodType = "annual",
            int page = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                throw new ArgumentException("Ticker symbol is required.", nameof(ticker));
            }

            var cleanTicker = ticker.Trim().ToUpperInvariant();
            var cleanPeriodType = string.IsNullOrWhiteSpace(periodType) ? "annual" : periodType.Trim().ToLowerInvariant();
            var endpoint = $"v1/stocks/{Uri.EscapeDataString(cleanTicker)}/financials?period_type={cleanPeriodType}&page={page}&page_size={pageSize}";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Requesting financials from BharatStock for ticker: {Ticker} via {Endpoint}", cleanTicker, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
                Console.WriteLine($"BharatStock API HTTP response status for ticker {cleanTicker}: {(int)response.StatusCode} {response.ReasonPhrase}");
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
                    throw new ProviderNotFoundException(cleanTicker, $"Financial data is not available for ticker '{cleanTicker}'.");
                }

                if (response.StatusCode == (HttpStatusCode)429) // Too Many Requests
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("BharatStock API rate limit exceeded (HTTP 429) for financials of ticker {Ticker}. Response: {ErrorBody}.", cleanTicker, errorBody);
                    if (_settings.UseMockData)
                    {
                        return MockFinancialProvider.GenerateMockFinancials(cleanTicker);
                    }
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    throw new ProviderRateLimitException($"Rate limit exceeded for BharatStock API. {errorBody}", retryAfter);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned error status {StatusCode} for ticker {Ticker}. Response: {ErrorBody}", statusCode, cleanTicker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"BharatStock API response for ticker {cleanTicker}: {json}");
                return ParseFinancialResponse(json, cleanTicker);
            }
        }

        public async Task<BharatStockRatiosRecord?> GetRatiosAsync(
            string ticker,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                throw new ArgumentException("Ticker symbol is required.", nameof(ticker));
            }

            var cleanTicker = ticker.Trim().ToUpperInvariant();
            var endpoint = $"v1/stocks/{Uri.EscapeDataString(cleanTicker)}/ratios";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Requesting ratios from BharatStock for ticker: {Ticker} via {Endpoint}", cleanTicker, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
                Console.WriteLine($"BharatStock Ratios API HTTP response status for ticker {cleanTicker}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Timeout occurred while contacting BharatStock Ratios API for ticker {Ticker}", cleanTicker);
                throw new ProviderApiException($"BharatStock API request timed out for ticker '{cleanTicker}'.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP network failure contacting BharatStock Ratios API for ticker {Ticker}", cleanTicker);
                throw new ProviderApiException($"Network error connecting to BharatStock API: {ex.Message}", innerException: ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned {StatusCode} Unauthorized for ratios of ticker {Ticker}. Response: {ErrorBody}", (int)response.StatusCode, cleanTicker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned 401 Unauthorized: {errorBody}", (int)response.StatusCode);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("BharatStock returned 404 Not Found for ratios of ticker: {Ticker}", cleanTicker);
                    return null;
                }

                if (response.StatusCode == (HttpStatusCode)429) // Too Many Requests
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("BharatStock API rate limit exceeded (HTTP 429) for ratios of ticker {Ticker}. Response: {ErrorBody}.", cleanTicker, errorBody);
                    if (_settings.UseMockData)
                    {
                        return MockFinancialProvider.GenerateMockRatios(cleanTicker);
                    }
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    throw new ProviderRateLimitException($"Rate limit exceeded for BharatStock API. {errorBody}", retryAfter);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned error status {StatusCode} for ratios of ticker {Ticker}. Response: {ErrorBody}", statusCode, cleanTicker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"BharatStock Ratios API response for ticker {cleanTicker}: {json}");
                return ParseRatiosResponse(json, cleanTicker);
            }
        }

        public async Task<BharatStockCompanyDetailsRecord?> GetStockDetailsAsync(
            string ticker,
            string? exchange = "NSE",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                throw new ArgumentException("Ticker symbol is required.", nameof(ticker));
            }

            var cleanTicker = ticker.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
            var endpoint = $"v1/stocks/{Uri.EscapeDataString(cleanTicker)}?exchange={Uri.EscapeDataString(cleanExchange)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Requesting stock company details from BharatStock for ticker: {Ticker} ({Exchange}) via {Endpoint}", cleanTicker, cleanExchange, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Timeout occurred while contacting BharatStock Stock Details API for ticker {Ticker}", cleanTicker);
                throw new ProviderApiException($"BharatStock API request timed out for ticker '{cleanTicker}'.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP network failure contacting BharatStock Stock Details API for ticker {Ticker}", cleanTicker);
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
                    _logger.LogWarning("BharatStock returned 404 Not Found for stock details of ticker: {Ticker}", cleanTicker);
                    return null;
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("BharatStock API rate limit exceeded (HTTP 429) for stock details of ticker {Ticker}. Response: {ErrorBody}.", cleanTicker, errorBody);
                    if (_settings.UseMockData)
                    {
                        return MockFinancialProvider.GenerateMockStockDetails(cleanTicker, cleanExchange);
                    }
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    throw new ProviderRateLimitException($"Rate limit exceeded for BharatStock API. {errorBody}", retryAfter);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned error status {StatusCode} for stock details of ticker {Ticker}. Response: {ErrorBody}", statusCode, cleanTicker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseCompanyDetailsResponse(json, cleanTicker);
            }
        }

        public async Task<BharatStockScreenerRecord?> GetScreenerDataAsync(
            string ticker,
            string? exchange = "NSE",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                throw new ArgumentException("Ticker symbol is required.", nameof(ticker));
            }

            var cleanTicker = ticker.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
            var endpoint = $"v1/screener?exchange={Uri.EscapeDataString(cleanExchange)}&page_size=200";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Requesting screener metrics from BharatStock for ticker: {Ticker} ({Exchange}) via {Endpoint}", cleanTicker, cleanExchange, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Timeout occurred while contacting BharatStock Screener API for ticker {Ticker}", cleanTicker);
                throw new ProviderApiException($"BharatStock API request timed out for ticker '{cleanTicker}'.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP network failure contacting BharatStock Screener API for ticker {Ticker}", cleanTicker);
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
                    _logger.LogWarning("BharatStock returned 404 Not Found for screener metrics of ticker: {Ticker}", cleanTicker);
                    return null;
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("BharatStock API rate limit exceeded (HTTP 429) for screener data of ticker {Ticker}. Response: {ErrorBody}.", cleanTicker, errorBody);
                    if (_settings.UseMockData)
                    {
                        return MockFinancialProvider.GenerateMockScreenerData(cleanTicker, cleanExchange);
                    }
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    throw new ProviderRateLimitException($"Rate limit exceeded for BharatStock API. {errorBody}", retryAfter);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("BharatStock API returned error status {StatusCode} for screener metrics of ticker {Ticker}. Response: {ErrorBody}", statusCode, cleanTicker, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseScreenerResponse(json, cleanTicker);
            }
        }

        public async Task<IReadOnlyList<BharatStockScreenerRecord>> GetScreenerBySectorAsync(
            string sector,
            string? exchange = "NSE",
            int page = 1,
            int pageSize = 200,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sector))
            {
                return Array.Empty<BharatStockScreenerRecord>();
            }

            var cleanSector = sector.Trim();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
            var endpoint = $"v1/screener?sector={Uri.EscapeDataString(cleanSector)}&exchange={Uri.EscapeDataString(cleanExchange)}&page={page}&page_size={pageSize}&sort_by=market_cap&sort_order=desc";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Requesting sector screener data from BharatStock for sector: {Sector} ({Exchange}) via {Endpoint}", cleanSector, cleanExchange, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Timeout occurred while contacting BharatStock Screener API for sector {Sector}", cleanSector);
                throw new ProviderApiException($"BharatStock API request timed out for sector '{cleanSector}'.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP network failure contacting BharatStock Screener API for sector {Sector}", cleanSector);
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
                    _logger.LogWarning("BharatStock returned 404 Not Found for sector screener of sector: {Sector}", cleanSector);
                    return Array.Empty<BharatStockScreenerRecord>();
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
                    _logger.LogError("BharatStock API returned error status {StatusCode} for sector screener of sector {Sector}. Response: {ErrorBody}", statusCode, cleanSector, errorBody);
                    throw new ProviderApiException($"BharatStock API returned status code {statusCode}: {errorBody}", statusCode);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseScreenerListResponse(json);
            }
        }

        public static IReadOnlyList<BharatStockFinancialRecord> ParseFinancialResponse(string json, string ticker)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<BharatStockFinancialRecord>();
            }

            var trimmed = json.Trim();

            // 1. Direct JSON Array: [ { fiscal_year: "FY25", ... }, ... ]
            if (trimmed.StartsWith("["))
            {
                var records = JsonSerializer.Deserialize<List<BharatStockFinancialRecord>>(json, JsonOptions);
                return records ?? (IReadOnlyList<BharatStockFinancialRecord>)Array.Empty<BharatStockFinancialRecord>();
            }

            // 2. Wrapped Object or Single Object
            if (trimmed.StartsWith("{"))
            {
                var wrapper = JsonSerializer.Deserialize<BharatStockFinancialApiResponseWrapper>(json, JsonOptions);
                if (wrapper != null)
                {
                    if (wrapper.Financials != null && wrapper.Financials.Count > 0)
                        return wrapper.Financials;

                    if (wrapper.Data != null && wrapper.Data.Count > 0)
                        return wrapper.Data;

                    if (wrapper.Results != null && wrapper.Results.Count > 0)
                        return wrapper.Results;
                }

                // Try parsing as single record object
                var singleRecord = JsonSerializer.Deserialize<BharatStockFinancialRecord>(json, JsonOptions);
                if (singleRecord != null &&
                    (!string.IsNullOrWhiteSpace(singleRecord.FiscalYear) ||
                     singleRecord.Revenue.HasValue ||
                     singleRecord.CashFlowOperating.HasValue ||
                     singleRecord.NetProfit.HasValue))
                {
                    return new List<BharatStockFinancialRecord> { singleRecord };
                }
            }

            return Array.Empty<BharatStockFinancialRecord>();
        }

        public static BharatStockRatiosRecord? ParseRatiosResponse(string json, string ticker)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var trimmed = json.Trim();
            if (trimmed.StartsWith("{"))
            {
                var record = JsonSerializer.Deserialize<BharatStockRatiosRecord>(json, JsonOptions);
                if (record != null && (record.Roe.HasValue || record.Roce.HasValue || record.PeRatio.HasValue || record.Week52High.HasValue || record.Week52Low.HasValue || record.Price.HasValue))
                {
                    return record;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<BharatStockRatiosRecord>(dataElem.GetRawText(), JsonOptions);
                }
                if (doc.RootElement.TryGetProperty("ratios", out var ratiosElem) && ratiosElem.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<BharatStockRatiosRecord>(ratiosElem.GetRawText(), JsonOptions);
                }
                if (doc.RootElement.TryGetProperty("results", out var resultsElem) && resultsElem.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<BharatStockRatiosRecord>(resultsElem.GetRawText(), JsonOptions);
                }
            }

            return null;
        }

        public static BharatStockCompanyDetailsRecord? ParseCompanyDetailsResponse(string json, string ticker)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var trimmed = json.Trim();
            if (trimmed.StartsWith("{"))
            {
                var record = JsonSerializer.Deserialize<BharatStockCompanyDetailsRecord>(json, JsonOptions);
                if (record != null && (!string.IsNullOrWhiteSpace(record.Symbol) || record.FaceValue.HasValue || !string.IsNullOrWhiteSpace(record.CompanyName)))
                {
                    return record;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<BharatStockCompanyDetailsRecord>(dataElem.GetRawText(), JsonOptions);
                }
                if (doc.RootElement.TryGetProperty("results", out var resultsElem) && resultsElem.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<BharatStockCompanyDetailsRecord>(resultsElem.GetRawText(), JsonOptions);
                }
            }

            return null;
        }

        public static BharatStockScreenerRecord? ParseScreenerResponse(string json, string ticker)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var cleanTicker = ticker.Trim().ToUpperInvariant();
            var trimmed = json.Trim();

            // 1. Direct array of screener records
            if (trimmed.StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<BharatStockScreenerRecord>>(json, JsonOptions);
                if (list != null)
                {
                    return list.Find(r => string.Equals(r.Symbol, cleanTicker, StringComparison.OrdinalIgnoreCase)) ?? (list.Count > 0 ? list[0] : null);
                }
            }

            // 2. Wrapped object or single screener record
            if (trimmed.StartsWith("{"))
            {
                var wrapper = JsonSerializer.Deserialize<BharatStockScreenerApiResponseWrapper>(json, JsonOptions);
                if (wrapper?.Data != null && wrapper.Data.Count > 0)
                {
                    return wrapper.Data.Find(r => string.Equals(r.Symbol, cleanTicker, StringComparison.OrdinalIgnoreCase)) ?? wrapper.Data[0];
                }
                if (wrapper?.Results != null && wrapper.Results.Count > 0)
                {
                    return wrapper.Results.Find(r => string.Equals(r.Symbol, cleanTicker, StringComparison.OrdinalIgnoreCase)) ?? wrapper.Results[0];
                }
                if (wrapper?.Items != null && wrapper.Items.Count > 0)
                {
                    return wrapper.Items.Find(r => string.Equals(r.Symbol, cleanTicker, StringComparison.OrdinalIgnoreCase)) ?? wrapper.Items[0];
                }

                var single = JsonSerializer.Deserialize<BharatStockScreenerRecord>(json, JsonOptions);
                if (single != null && (single.MarketCap.HasValue || single.BookValuePerShare.HasValue || single.BookValue.HasValue || !string.IsNullOrWhiteSpace(single.Symbol)))
                {
                    return single;
                }
            }

            return null;
        }

        public static IReadOnlyList<BharatStockScreenerRecord> ParseScreenerListResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<BharatStockScreenerRecord>();
            }

            var trimmed = json.Trim();

            // 1. Direct array of screener records
            if (trimmed.StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<BharatStockScreenerRecord>>(json, JsonOptions);
                return list ?? (IReadOnlyList<BharatStockScreenerRecord>)Array.Empty<BharatStockScreenerRecord>();
            }

            // 2. Wrapped object
            if (trimmed.StartsWith("{"))
            {
                var wrapper = JsonSerializer.Deserialize<BharatStockScreenerApiResponseWrapper>(json, JsonOptions);
                if (wrapper?.Data != null && wrapper.Data.Count > 0)
                {
                    return wrapper.Data;
                }
                if (wrapper?.Results != null && wrapper.Results.Count > 0)
                {
                    return wrapper.Results;
                }
                if (wrapper?.Items != null && wrapper.Items.Count > 0)
                {
                    return wrapper.Items;
                }

                var single = JsonSerializer.Deserialize<BharatStockScreenerRecord>(json, JsonOptions);
                if (single != null && (single.MarketCap.HasValue || !string.IsNullOrWhiteSpace(single.Symbol)))
                {
                    return new List<BharatStockScreenerRecord> { single };
                }
            }

            return Array.Empty<BharatStockScreenerRecord>();
        }
    }
}


