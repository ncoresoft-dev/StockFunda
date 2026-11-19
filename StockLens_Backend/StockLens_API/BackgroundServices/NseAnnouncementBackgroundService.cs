using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.Interfaces;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.BackgroundServices
{
    public class NseAnnouncementBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NseAnnouncementBackgroundService> _logger;

        private DateTime _lastFetchAttemptUtc = DateTime.MinValue;
        private TimeSpan _currentWait = TimeSpan.FromMinutes(1);
        private bool _hasDoneInitialFullSync = false;
        private const int MaxWaitMinutes = 16;
        private const int BaseWaitMinutes = 1;
        private const int NormalIntervalMinutes = 3; // Poll gently every 3 minutes for the whole feed

        public NseAnnouncementBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<NseAnnouncementBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public TimeSpan CurrentWait => _currentWait;
        public DateTime LastFetchAttemptUtc => _lastFetchAttemptUtc;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NseAnnouncementBackgroundService started. Polling NSE corporate announcements gently every {Minutes} minutes.", NormalIntervalMinutes);

            // Brief initial delay on server boot
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // RATE GUARD: Never fetch twice within the minute
                var elapsed = DateTime.UtcNow - _lastFetchAttemptUtc;
                if (elapsed < TimeSpan.FromMinutes(1))
                {
                    var waitNeeded = TimeSpan.FromMinutes(1) - elapsed;
                    _logger.LogInformation("Rate guard: Waiting {Seconds:F1}s to ensure NSE is never polled twice within the minute.", waitNeeded.TotalSeconds);
                    await Task.Delay(waitNeeded, stoppingToken);
                }

                _lastFetchAttemptUtc = DateTime.UtcNow;
                bool fetchSuccess = false;
                string? errorMsg = null;

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var announcementService = scope.ServiceProvider.GetRequiredService<IStockAnnouncementService>();

                    _logger.LogInformation("NseAnnouncementBackgroundService: Fetching latest equities corporate announcements from NSE (InitialFullSync: {IsInitial})...", !_hasDoneInitialFullSync);
                    int monthsBack = !_hasDoneInitialFullSync ? 3 : 0;
                    var newCount = await announcementService.SyncLatestAnnouncementsAsync(monthsBack, stoppingToken);
                    _hasDoneInitialFullSync = true;

                    _logger.LogInformation("NseAnnouncementBackgroundService: Completed sync at UTC {Time}. New records added: {Count}.",
                        DateTime.UtcNow, newCount);

                    fetchSuccess = true;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("NseAnnouncementBackgroundService: Received HTTP 429 (Too Many Requests) from NSE. Activating exponential backoff.");
                    fetchSuccess = false;
                    errorMsg = "HTTP 429: Too Many Requests from NSE.";
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized || ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("NseAnnouncementBackgroundService: Received HTTP {StatusCode} from NSE. Activating exponential backoff.", ex.StatusCode);
                    fetchSuccess = false;
                    errorMsg = $"HTTP {ex.StatusCode} from NSE.";
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("NseAnnouncementBackgroundService: HTTP request failed ({StatusCode}): {Message}", ex.StatusCode, ex.Message);
                    fetchSuccess = false;
                    errorMsg = $"HTTP Error: {ex.Message}";
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("NseAnnouncementBackgroundService: Cancellation requested. Stopping service.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NseAnnouncementBackgroundService: Unexpected failure during NSE announcements sync.");
                    fetchSuccess = false;
                    errorMsg = ex.Message;
                }

                if (fetchSuccess)
                {
                    // Reset backoff delay on successful fetch
                    _currentWait = TimeSpan.FromMinutes(BaseWaitMinutes);

                    // Wait normal interval (3 minutes) before next gentle background poll
                    _logger.LogInformation("NseAnnouncementBackgroundService: Next regular poll scheduled in {Minutes} minutes.", NormalIntervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(NormalIntervalMinutes), stoppingToken);
                }
                else
                {
                    // BACKOFF: On 401/403/429 or network errors, wait with increasing delays (1 -> 2 -> 4 -> 8 -> 16 minutes)
                    _logger.LogWarning("NseAnnouncementBackgroundService: Failure occurred ({Error}). Backing off for {Minutes} min before retrying. Next attempt at ~{NextTime} UTC.",
                        errorMsg, _currentWait.TotalMinutes, DateTime.UtcNow.Add(_currentWait));

                    await Task.Delay(_currentWait, stoppingToken);

                    // Double the wait time, capped at MaxWaitMinutes
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
