using Microsoft.Extensions.Logging;
using StockLens_DataLayer.Entities;
using StockLens_Infrastructure.ExternalServices.Nse.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.Nse
{
    public class NseAnnouncementClient : INseAnnouncementClient, IDisposable
    {
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        private const string NseBaseUrl = "https://www.nseindia.in";
        private const string AnnouncementsUrl = "https://www.nseindia.in/api/corporate-announcements?index=equities";
        private const string AnnouncementsReferer = "https://www.nseindia.in/companies-listing/corporate-filings/announcements";

        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);
        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd HH:mm:ss",
            "dd-MMM-yyyy HH:mm:ss",
            "dd-MMM-yyyy HH:mm",
            "yyyy-MM-ddTHH:mm:ss",
            "dd-MM-yyyy HH:mm:ss",
            "dd-MM-yyyy"
        };

        private readonly ILogger<NseAnnouncementClient> _logger;
        private readonly SemaphoreSlim _sessionLock = new(1, 1);

        private HttpClient? _httpClient;
        private HttpClientHandler? _httpHandler;
        private CookieContainer? _cookieContainer;
        private DateTime _sessionPrimedAt = DateTime.MinValue;
        private bool _disposed;

        public NseAnnouncementClient(ILogger<NseAnnouncementClient> logger)
        {
            _logger = logger;
        }

        public async Task<IReadOnlyList<StockAnnouncement>> FetchAnnouncementsAsync(
            DateTime? fromDate = null,
            DateTime? toDate = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureSessionPrimedAsync(cancellationToken);

            var queryUrl = BuildRequestUrl(fromDate, toDate);
            _logger.LogInformation("NseAnnouncementClient: Fetching corporate announcements from {Url}", queryUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Add("Accept", "application/json, text/plain, */*");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            request.Headers.Add("Referer", AnnouncementsReferer);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "NseAnnouncementClient: Network error while sending request to NSE.");
                throw;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("NseAnnouncementClient: Received HTTP {StatusCode} from NSE. Invalidating session cookies.", response.StatusCode);
                await InvalidateSessionAsync();
                throw new HttpRequestException($"NSE returned {response.StatusCode}. Session expired or blocked.", null, response.StatusCode);
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                _logger.LogWarning("NseAnnouncementClient: Received HTTP 429 (Too Many Requests) from NSE.");
                throw new HttpRequestException("NSE rate limit reached (HTTP 429).", null, response.StatusCode);
            }

            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogWarning("NseAnnouncementClient: Empty JSON payload received from NSE.");
                return Array.Empty<StockAnnouncement>();
            }

            List<NseAnnouncementRawItem>? rawItems;
            try
            {
                rawItems = JsonSerializer.Deserialize<List<NseAnnouncementRawItem>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "NseAnnouncementClient: Failed to deserialize NSE announcements JSON payload. Preview: {Preview}",
                    jsonContent.Length > 200 ? jsonContent[..200] : jsonContent);
                return Array.Empty<StockAnnouncement>();
            }

            if (rawItems == null || rawItems.Count == 0)
            {
                _logger.LogInformation("NseAnnouncementClient: NSE returned 0 announcement records.");
                return Array.Empty<StockAnnouncement>();
            }

            var results = new List<StockAnnouncement>(rawItems.Count);
            foreach (var item in rawItems)
            {
                if (string.IsNullOrWhiteSpace(item.SeqId) || string.IsNullOrWhiteSpace(item.Symbol))
                {
                    continue;
                }

                results.Add(new StockAnnouncement
                {
                    SeqId = item.SeqId.Trim(),
                    Symbol = item.Symbol.Trim().ToUpperInvariant(),
                    CompanyName = !string.IsNullOrWhiteSpace(item.SmName) ? item.SmName.Trim() : item.Symbol.Trim(),
                    Isin = item.SmIsin?.Trim(),
                    Industry = item.SmIndustry?.Trim(),
                    Subject = item.Desc?.Trim(),
                    Details = item.AttchmntText?.Trim(),
                    AttachmentUrl = item.AttchmntFile?.Trim(),
                    AttachmentFileSize = (!string.IsNullOrWhiteSpace(item.AttFileSize) ? item.AttFileSize : item.FileSize)?.Trim(),
                    HasXbrl = item.HasXbrl ?? false,
                    BroadcastDateTime = ParseDateTime(item.SortDate, item.AnDt, item.Exchdisstime),
                    DisseminationTime = item.Exchdisstime?.Trim(),
                    Difference = item.Difference?.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            _logger.LogInformation("NseAnnouncementClient: Successfully parsed {Count} corporate announcements from NSE.", results.Count);
            return results;
        }

        private async Task EnsureSessionPrimedAsync(CancellationToken cancellationToken)
        {
            if (_httpClient != null && (DateTime.UtcNow - _sessionPrimedAt) < SessionLifetime)
            {
                return; // Cookie session is still fresh, reuse it!
            }

            await _sessionLock.WaitAsync(cancellationToken);
            try
            {
                // Double check pattern inside lock
                if (_httpClient != null && (DateTime.UtcNow - _sessionPrimedAt) < SessionLifetime)
                {
                    return;
                }

                _logger.LogInformation("NseAnnouncementClient: Priming session cookies from {BaseUrl}...", NseBaseUrl);
                CreateNewClient();

                using var primeRequest = new HttpRequestMessage(HttpMethod.Get, NseBaseUrl);
                primeRequest.Headers.Add("User-Agent", UserAgent);
                primeRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                primeRequest.Headers.Add("Accept-Language", "en-US,en;q=0.9");

                using var primeResponse = await _httpClient!.SendAsync(primeRequest, cancellationToken);
                _logger.LogInformation("NseAnnouncementClient: Session primed with status {StatusCode}.", primeResponse.StatusCode);

                _sessionPrimedAt = DateTime.UtcNow;

                // Brief courtesy pause after priming before API query
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private async Task InvalidateSessionAsync()
        {
            await _sessionLock.WaitAsync();
            try
            {
                _sessionPrimedAt = DateTime.MinValue;
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private void CreateNewClient()
        {
            _httpClient?.Dispose();
            _httpHandler?.Dispose();

            _cookieContainer = new CookieContainer();
            _httpHandler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true
            };

            _httpClient = new HttpClient(_httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
        }

        private static string BuildRequestUrl(DateTime? fromDate, DateTime? toDate)
        {
            // Default to today's date in IST (UTC + 5:30) if not specified
            var istNow = DateTime.UtcNow.AddHours(5.5);
            var from = fromDate ?? istNow;
            var to = toDate ?? istNow;

            var fromStr = from.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            var toStr = to.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

            return $"{AnnouncementsUrl}&from_date={fromStr}&to_date={toStr}";
        }

        private static DateTime? ParseDateTime(string? sortDate, string? anDt, string? exchdisstime)
        {
            var candidates = new[] { sortDate, anDt, exchdisstime };
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                if (DateTime.TryParseExact(candidate.Trim(), DateFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal, out var exactDt))
                {
                    return exactDt;
                }

                if (DateTime.TryParse(candidate.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal, out var looseDt))
                {
                    return looseDt;
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _sessionLock.Dispose();
            _httpClient?.Dispose();
            _httpHandler?.Dispose();
        }
    }
}
