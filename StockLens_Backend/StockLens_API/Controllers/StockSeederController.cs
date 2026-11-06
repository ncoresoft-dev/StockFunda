using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockLens_API.BackgroundServices;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.Seeders;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/seeder")]
    [Produces("application/json")]
    public class StockSeederController : ControllerBase
    {
        private readonly StockFundamentalsSeeder _seeder;
        private readonly IDailyValuationService _dailyValuationService;
        private readonly StockSeedingBackgroundService _backgroundSeeder;
        private readonly DailyValuationBackgroundService _dailyValuationBackground;
        private readonly StockLensDataContext _dbContext;

        public StockSeederController(
            StockFundamentalsSeeder seeder,
            IDailyValuationService dailyValuationService,
            StockSeedingBackgroundService backgroundSeeder,
            DailyValuationBackgroundService dailyValuationBackground,
            StockLensDataContext dbContext)
        {
            _seeder = seeder;
            _dailyValuationService = dailyValuationService;
            _backgroundSeeder = backgroundSeeder;
            _dailyValuationBackground = dailyValuationBackground;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Dynamically discovers all sectors from CompanyMaster and seeds the top stocks per sector
        /// with live fundamentals (ratios, quarters, price, ownership) via Yahoo Finance (100% free, 0 IndianAPI calls spent).
        /// </summary>
        /// <param name="limitPerSector">Number of leading stocks to seed per sector (default 3, range 1-10).</param>
        [HttpPost("seed-sector-leaders")]
        [ProducesResponseType(typeof(SeedingProgressDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> SeedSectorLeaders(
            [FromQuery] int limitPerSector = 3,
            CancellationToken cancellationToken = default)
        {
            if (limitPerSector < 1) limitPerSector = 1;
            if (limitPerSector > 10) limitPerSector = 10;

            var progress = await _seeder.SeedSectorLeadersAsync(limitPerSector, cancellationToken);
            return Ok(progress);
        }

        /// <summary>
        /// Dynamically seeds or refreshes full fundamentals and quarters for a single stock symbol.
        /// </summary>
        [HttpPost("seed-single/{symbol}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SeedSingleStock(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { message = "Stock symbol is required." });
            }

            var success = await _seeder.SeedSingleStockAsync(symbol.Trim(), cancellationToken);
            if (!success)
            {
                return BadRequest(new { message = $"Could not seed fundamentals for '{symbol}'. Verify symbol is listed on NSE/BSE." });
            }

            return Ok(new { message = $"Successfully seeded fundamentals for {symbol.ToUpperInvariant()}" });
        }

        /// <summary>
        /// Screener.in style Daily EOD Update:
        /// Fetches today's closing prices for all seeded stocks, recalculates MarketCap, P/E, P/B
        /// and updates StockFinancials & MarketTickerQuotes without touching quarterly filings.
        /// </summary>
        [HttpPost("run-daily-update")]
        [ProducesResponseType(typeof(DailyValuationUpdateResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> RunDailyUpdate(CancellationToken cancellationToken = default)
        {
            var result = await _dailyValuationBackground.RunNowAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Returns the status of the scheduled daily EOD valuation updater.
        /// </summary>
        [HttpGet("daily-update-status")]
        [ProducesResponseType(typeof(DailyValuationStatusDto), StatusCodes.Status200OK)]
        public IActionResult GetDailyUpdateStatus()
        {
            var status = _dailyValuationBackground.GetStatus();
            return Ok(status);
        }

        /// <summary>
        /// Starts the continuous autonomous background seeder worker (seeds ~15 companies/minute).
        /// </summary>
        [HttpPost("background-start")]
        public IActionResult StartBackgroundSeeder()
        {
            _backgroundSeeder.StartSeeding();
            return Ok(new { message = "Autonomous background stock seeder started." });
        }

        /// <summary>
        /// Pauses the continuous autonomous background seeder worker.
        /// </summary>
        [HttpPost("background-pause")]
        public IActionResult PauseBackgroundSeeder()
        {
            _backgroundSeeder.PauseSeeding();
            return Ok(new { message = "Autonomous background stock seeder paused." });
        }

        /// <summary>
        /// Returns the live progress status of the autonomous background seeder worker.
        /// </summary>
        [HttpGet("background-status")]
        [ProducesResponseType(typeof(BackgroundSeedingStatus), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetBackgroundSeederStatus(CancellationToken cancellationToken = default)
        {
            var status = await _backgroundSeeder.GetStatusAsync(cancellationToken);
            return Ok(status);
        }

        /// <summary>
        /// Reports current seeding statistics in the database.
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetSeederStatus(CancellationToken cancellationToken = default)
        {
            var companiesCount = await _dbContext.CompanyMaster.CountAsync(cancellationToken);
            var stocksCount = await _dbContext.Stocks.CountAsync(cancellationToken);
            var financialsCount = await _dbContext.StockFinancials.CountAsync(cancellationToken);
            var shareholdingsCount = await _dbContext.StockShareholdings.CountAsync(cancellationToken);

            var seededStocksCount = await _dbContext.StockFinancials
                .Select(f => f.StockId)
                .Distinct()
                .CountAsync(cancellationToken);

            var sectorsCount = await _dbContext.CompanyMaster
                .Where(c => !string.IsNullOrWhiteSpace(c.Sector))
                .Select(c => c.Sector)
                .Distinct()
                .CountAsync(cancellationToken);

            return Ok(new
            {
                totalCompaniesInMaster = companiesCount,
                distinctSectors = sectorsCount,
                stocksRegistered = stocksCount,
                stocksWithFinancialsSeeded = seededStocksCount,
                totalFinancialRecords = financialsCount,
                totalShareholdingRecords = shareholdingsCount
            });
        }
    }
}

