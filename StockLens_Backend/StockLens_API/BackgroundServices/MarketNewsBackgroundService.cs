using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.GoogleNews;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.BackgroundServices
{
    public class MarketNewsBackgroundService : BackgroundService
    {
        public const string DefaultMarketNewsUrl = "https://news.google.com/rss/search?q=Sensex+OR+Nifty+OR+%22stock+market%22+when:1d&hl=en-IN&gl=IN&ceid=IN:en";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMarketNewsCache _newsCache;
        private readonly ILogger<MarketNewsBackgroundService> _logger;

        private DateTime _lastFetchAttemptUtc = DateTime.MinValue;
        private TimeSpan _currentWait = TimeSpan.FromMinutes(1);
        private const int MaxWaitMinutes = 15;
        private const int BaseWaitMinutes = 1;
        private const int NormalIntervalMinutes = 2; // Normal healthy polling interval (ensures > 1 min between fetches)

        public MarketNewsBackgroundService(
            IServiceScopeFactory scopeFactory,
            IMarketNewsCache newsCache,
            ILogger<MarketNewsBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _newsCache = newsCache;
            _logger = logger;
        }

        public TimeSpan CurrentWait => _currentWait;
        public DateTime LastFetchAttemptUtc => _lastFetchAttemptUtc;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MarketNewsBackgroundService started. Monitoring Google News RSS for Sensex/Nifty/Stock Market.");

            // Brief initial delay on server boot
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // RULE 1: Never fetch the same URL twice within the minute
                var elapsed = DateTime.UtcNow - _lastFetchAttemptUtc;
                if (elapsed < TimeSpan.FromMinutes(1))
                {
                    var waitNeeded = TimeSpan.FromMinutes(1) - elapsed;
                    _logger.LogInformation("Rate guard: Waiting {Seconds:F1}s before fetch to ensure URL is never fetched twice within the minute.", waitNeeded.TotalSeconds);
                    await Task.Delay(waitNeeded, stoppingToken);
                }

                _lastFetchAttemptUtc = DateTime.UtcNow;
                bool fetchSuccess = false;
                string? errorMsg = null;

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var googleNewsClient = scope.ServiceProvider.GetRequiredService<IGoogleNewsClient>();

                    _logger.LogInformation("MarketNewsBackgroundService: Fetching latest market news from Google News RSS...");
                    var articles = await googleNewsClient.GetMarketNewsRssAsync(DefaultMarketNewsUrl, stoppingToken);

                    if (articles != null && articles.Count > 0)
                    {
                        _newsCache.UpdateArticles(articles);
                        _logger.LogInformation("MarketNewsBackgroundService: Successfully fetched and cached {Count} articles at UTC {Time}.", articles.Count, DateTime.UtcNow);
                        fetchSuccess = true;
                    }
                    else
                    {
                        _logger.LogWarning("MarketNewsBackgroundService: Google News RSS returned 0 articles.");
                        if (_newsCache.GetArticles().Count > 0)
                        {
                            fetchSuccess = true;
                        }
                        else
                        {
                            fetchSuccess = false;
                            errorMsg = "Empty article list returned from Google News RSS.";
                        }
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("MarketNewsBackgroundService: Received HTTP 429 Too Many Requests from Google News RSS. Activating exponential backoff.");
                    fetchSuccess = false;
                    errorMsg = "HTTP 429: Too Many Requests from Google News.";
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("MarketNewsBackgroundService: HTTP request failed ({StatusCode}): {Message}", ex.StatusCode, ex.Message);
                    fetchSuccess = false;
                    errorMsg = $"HTTP Error: {ex.Message}";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MarketNewsBackgroundService: Unexpected failure during Google News RSS fetch.");
                    fetchSuccess = false;
                    errorMsg = ex.Message;
                }

                if (fetchSuccess)
                {
                    // Reset backoff delay on successful fetch
                    _currentWait = TimeSpan.FromMinutes(BaseWaitMinutes);

                    // Wait regular interval before next background poll (2 minutes ensures never fetched twice within the minute)
                    _logger.LogInformation("MarketNewsBackgroundService: Next regular poll scheduled in {Minutes} minutes.", NormalIntervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(NormalIntervalMinutes), stoppingToken);
                }
                else
                {
                    // RULE 2: On 429 or any failure, wait and retry later, with the wait doubling each time (1 min, 2, 4, ... up to about 15 min).
                    // Do not retry in a tight loop.
                    _logger.LogWarning("MarketNewsBackgroundService: Failure occurred ({Error}). Backing off for {Minutes} min before retrying. Next attempt at ~{NextTime} UTC.",
                        errorMsg, _currentWait.TotalMinutes, DateTime.UtcNow.Add(_currentWait));

                    _newsCache.RecordFailure(errorMsg ?? "Unknown error", (int)_currentWait.TotalMinutes);

                    // Await the backoff delay (avoids tight loop)
                    await Task.Delay(_currentWait, stoppingToken);

                    // Double the wait time, capped at 15 minutes (1 -> 2 -> 4 -> 8 -> 15 min)
                    var nextMinutes = _currentWait.TotalMinutes * 2;
                    if (nextMinutes > MaxWaitMinutes)
                    {
                        nextMinutes = MaxWaitMinutes;
                    }
                    _currentWait = TimeSpan.FromMinutes(nextMinutes);
                }
            }
        }
    }
}
