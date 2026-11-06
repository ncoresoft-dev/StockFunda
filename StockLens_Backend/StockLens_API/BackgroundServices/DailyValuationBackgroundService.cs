using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StockLens_Infrastructure.Seeders;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.BackgroundServices
{
    public class DailyValuationStatusDto
    {
        public bool IsRunning { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DailyValuationUpdateResult? LastResult { get; set; }
        public string Message { get; set; } = "Scheduled daily at 18:00 IST.";
    }

    public class DailyValuationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DailyValuationBackgroundService> _logger;

        private DateTime? _lastRunDateIst;
        private DailyValuationUpdateResult? _lastResult;
        private bool _isExecuting = false;

        public DailyValuationBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<DailyValuationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<DailyValuationUpdateResult> RunNowAsync(CancellationToken cancellationToken = default)
        {
            if (_isExecuting)
            {
                return new DailyValuationUpdateResult
                {
                    Errors = { "A daily valuation update is already running." }
                };
            }

            _isExecuting = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dailyService = scope.ServiceProvider.GetRequiredService<IDailyValuationService>();
                var result = await dailyService.UpdateDailyValuationsAsync(cancellationToken);
                _lastResult = result;
                _lastRunDateIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetIndianTimeZone()).Date;
                return result;
            }
            finally
            {
                _isExecuting = false;
            }
        }

        public DailyValuationStatusDto GetStatus()
        {
            return new DailyValuationStatusDto
            {
                IsRunning = _isExecuting,
                LastRunAt = _lastRunDateIst,
                LastResult = _lastResult,
                Message = _isExecuting ? "Update in progress..." : "Scheduled daily at 18:00 IST (market close)."
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DailyValuationBackgroundService started. Scheduled for 18:00 IST daily.");

            // Wait 30 seconds after server startup
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var istTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetIndianTimeZone());

                    // Check if it's after 18:00 IST and hasn't run today yet
                    bool isAfterMarketClose = istTime.Hour >= 18;
                    bool alreadyRanToday = _lastRunDateIst.HasValue && _lastRunDateIst.Value == istTime.Date;

                    if (isAfterMarketClose && !alreadyRanToday && !_isExecuting)
                    {
                        _logger.LogInformation("Triggering automatic daily EOD valuation update at IST time: {Time}", istTime);
                        await RunNowAsync(stoppingToken);
                    }

                    // Check every 15 minutes
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking daily valuation update schedule");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("DailyValuationBackgroundService stopped.");
        }

        private static TimeZoneInfo GetIndianTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }
            catch
            {
                // Fallback for non-Windows environments
                return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "IST");
            }
        }
    }
}

