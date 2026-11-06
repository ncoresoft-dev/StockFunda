using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.Seeders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.BackgroundServices
{
    public class BackgroundSeedingStatus
    {
        public bool IsRunning { get; set; }
        public string? CurrentSymbol { get; set; }
        public int TotalSeededInSession { get; set; }
        public int TotalFailedInSession { get; set; }
        public int TotalRemainingInDb { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastSeededAt { get; set; }
        public string Message { get; set; } = "Idle";
    }

    public class StockSeedingBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StockSeedingBackgroundService> _logger;

        private bool _isEnabled = false;
        private int _totalSeeded = 0;
        private int _totalFailed = 0;
        private string? _currentSymbol;
        private DateTime? _startedAt;
        private DateTime? _lastSeededAt;
        private string _statusMessage = "Paused. Call /api/seeder/background-start to begin autonomous seeding.";

        public StockSeedingBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<StockSeedingBackgroundService> logger,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            if (configuration.GetValue<bool>("SeederSettings:AutoStartBackgroundSeeder", false))
            {
                _isEnabled = true;
                _startedAt = DateTime.UtcNow;
                _statusMessage = "Autonomous background stock seeder enabled via configuration.";
            }
        }

        public void StartSeeding()
        {
            _isEnabled = true;
            _startedAt ??= DateTime.UtcNow;
            _statusMessage = "Autonomous background seeder is running.";
            _logger.LogInformation("Autonomous background stock seeder enabled.");
        }

        public void PauseSeeding()
        {
            _isEnabled = false;
            _statusMessage = "Autonomous background seeder is paused.";
            _logger.LogInformation("Autonomous background stock seeder paused.");
        }

        public async Task<BackgroundSeedingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            int remaining = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<StockLensDataContext>();

                var seededStockIds = await dbContext.StockFinancials
                    .AsNoTracking()
                    .Select(f => f.StockId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var seededSymbols = await dbContext.Stocks
                    .AsNoTracking()
                    .Where(s => seededStockIds.Contains(s.Id))
                    .Select(s => s.Symbol.ToUpper())
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var totalCompanies = await dbContext.CompanyMaster.CountAsync(cancellationToken);
                remaining = Math.Max(totalCompanies - seededSymbols.Count, 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not compute remaining companies count");
            }

            return new BackgroundSeedingStatus
            {
                IsRunning = _isEnabled,
                CurrentSymbol = _currentSymbol,
                TotalSeededInSession = _totalSeeded,
                TotalFailedInSession = _totalFailed,
                TotalRemainingInDb = remaining,
                StartedAt = _startedAt,
                LastSeededAt = _lastSeededAt,
                Message = _statusMessage
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StockSeedingBackgroundService started.");

            // Wait 10 seconds after server startup before commencing background work
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_isEnabled)
                {
                    // Check every 3 seconds if enabled
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<StockLensDataContext>();
                    var seeder = scope.ServiceProvider.GetRequiredService<StockFundamentalsSeeder>();

                    // 1. Identify which stocks are already seeded
                    var seededSymbols = await dbContext.StockFinancials
                        .AsNoTracking()
                        .Include(f => f.Stock)
                        .Select(f => f.Stock.Symbol.ToUpper())
                        .Distinct()
                        .ToListAsync(stoppingToken);

                    var seededSet = new HashSet<string>(seededSymbols, StringComparer.OrdinalIgnoreCase);

                    // 2. Prioritize candidate stocks:
                    // Priority A: Nifty 50 constituents not yet seeded
                    var nextSymbol = await dbContext.MarketIndexConstituents
                        .AsNoTracking()
                        .Where(m => !seededSet.Contains(m.Symbol.ToUpper()))
                        .Select(m => m.Symbol.Trim().ToUpperInvariant())
                        .FirstOrDefaultAsync(stoppingToken);

                    // Priority B: Sector leaders not yet seeded
                    if (string.IsNullOrWhiteSpace(nextSymbol))
                    {
                        var unseededBySector = await dbContext.CompanyMaster
                            .AsNoTracking()
                            .Where(c => !string.IsNullOrWhiteSpace(c.Symbol) && !seededSet.Contains(c.Symbol.ToUpper()) && !string.IsNullOrWhiteSpace(c.Sector))
                            .GroupBy(c => c.Sector)
                            .Select(g => g.Select(c => c.Symbol.Trim().ToUpperInvariant()).FirstOrDefault())
                            .FirstOrDefaultAsync(stoppingToken);

                        nextSymbol = unseededBySector;
                    }

                    // Priority C: Any unseeded company in CompanyMaster
                    if (string.IsNullOrWhiteSpace(nextSymbol))
                    {
                        nextSymbol = await dbContext.CompanyMaster
                            .AsNoTracking()
                            .Where(c => !string.IsNullOrWhiteSpace(c.Symbol) && !seededSet.Contains(c.Symbol.ToUpper()))
                            .Select(c => c.Symbol.Trim().ToUpperInvariant())
                            .FirstOrDefaultAsync(stoppingToken);
                    }

                    // If no unseeded symbols left, all companies are seeded!
                    if (string.IsNullOrWhiteSpace(nextSymbol))
                    {
                        _statusMessage = "All registered companies in DB are fully seeded!";
                        _logger.LogInformation("All companies in CompanyMaster have been seeded. Pausing background seeder.");
                        _isEnabled = false;
                        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                        continue;
                    }

                    _currentSymbol = nextSymbol;
                    _statusMessage = $"Seeding {nextSymbol}...";
                    _logger.LogInformation("Background seeder processing symbol: {Symbol}", nextSymbol);

                    var success = await seeder.SeedSingleStockAsync(nextSymbol, stoppingToken);
                    _lastSeededAt = DateTime.UtcNow;

                    if (success)
                    {
                        _totalSeeded++;
                        _statusMessage = $"Successfully seeded {nextSymbol}. (Total session: {_totalSeeded})";
                    }
                    else
                    {
                        _totalFailed++;
                        _statusMessage = $"Failed to seed {nextSymbol}. (Failed session: {_totalFailed})";
                    }

                    // Gentle throttle: 4 seconds pause per stock (~15 stocks / min)
                    // Zero IndianAPI calls spent (uses free Yahoo Finance API Refinitiv feed)
                    await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in background seeder loop");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            _logger.LogInformation("StockSeedingBackgroundService stopped.");
        }
    }
}

