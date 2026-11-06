using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StockLens_BusinessLayer.Services;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using StockLens_Infrastructure.Seeders;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StockLens_UnitTests
{
    public class LiveYahooFinanceTests
    {
        private readonly ITestOutputHelper _output;
        private const string TestDbConn = "Server=DESKTOP-J2CVHIQ;Database=StockLensTestDB;Trusted_Connection=True;Encrypt=False;";

        public LiveYahooFinanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Test_StockFundamentalsSeeder_Single_Stock_TCS()
        {
            var options = new DbContextOptionsBuilder<StockLensDataContext>()
                .UseSqlServer(TestDbConn)
                .Options;

            using var dbContext = new StockLensDataContext(options);
            using var httpClient = new HttpClient();
            var yahooClient = new YahooFinanceClient(httpClient, NullLogger<YahooFinanceClient>.Instance);
            var seeder = new StockFundamentalsSeeder(dbContext, yahooClient, NullLogger<StockFundamentalsSeeder>.Instance);

            _output.WriteLine("Testing SeedSingleStockAsync for TCS...");
            var success = await seeder.SeedSingleStockAsync("TCS");
            _output.WriteLine($"Seed result for TCS: {success}");
            success.Should().BeTrue();

            // Verify in DB
            var tcsStock = await dbContext.Stocks.Include(s => s.Financials).FirstOrDefaultAsync(s => s.Symbol == "TCS");
            tcsStock.Should().NotBeNull();
            _output.WriteLine($"TCS StockId={tcsStock!.Id}, Quarters Count in DB={tcsStock.Financials.Count}");

            foreach (var f in tcsStock.Financials.Where(f => f.Source == "YahooFinance"))
            {
                _output.WriteLine($"DB Record: {f.FiscalYear} | EndDate: {f.PeriodEndDate:yyyy-MM-dd} | Rev: ₹{f.Revenue}Cr | OpProfit: ₹{f.OperatingProfit}Cr | NetProfit: ₹{f.NetProfit}Cr | EPS: ₹{f.Eps} | Price: ₹{f.CurrentPrice} | MCap: ₹{f.MarketCap}Cr | PE: {f.PeRatio}");
            }
        }

        [Fact]
        public async Task Test_SectorAnalysisService_PureDB_Execution()
        {
            var options = new DbContextOptionsBuilder<StockLensDataContext>()
                .UseSqlServer(TestDbConn, opt => opt.CommandTimeout(60))
                .Options;

            using var dbContext = new StockLensDataContext(options);
            using var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
            var service = new SectorAnalysisService(dbContext, NullLogger<SectorAnalysisService>.Instance, memoryCache);

            _output.WriteLine("Testing SectorAnalysisService pure DB execution for 'Information Technology'...");
            
            // First call (cold)
            var sw = Stopwatch.StartNew();
            var result = await service.GetSectorAnalysisAsync("Information Technology");
            sw.Stop();
            _output.WriteLine($"Cold execution completed in {sw.ElapsedMilliseconds} ms!");

            // Second call (warm memory cache)
            var sw2 = Stopwatch.StartNew();
            var result2 = await service.GetSectorAnalysisAsync("Information Technology");
            sw2.Stop();
            _output.WriteLine($"Warm cached execution completed in {sw2.ElapsedMilliseconds} ms!");
            sw2.ElapsedMilliseconds.Should().BeLessThan(50, "Warm cached execution should be sub-50ms");

            result.Should().NotBeNull();
            result.Summary.Should().NotBeNull();
            result.Summary.SectorName.Should().Be("Information Technology");
            _output.WriteLine($"Sector: {result.Summary.SectorName} | Companies: {result.Summary.TotalCompanies} | Avg MCap: ₹{result.Summary.AverageMarketCapCr}Cr | Avg PE: {result.Summary.AveragePe} | Avg ROE: {result.Summary.AverageRoe}% | Score: {result.Summary.SectorScore}");

            result.Stocks.Should().NotBeEmpty();
            _output.WriteLine($"Total stocks in sector response: {result.Stocks.Count}");

            // Check top 5 stocks by Market Cap
            var topStocks = result.Stocks.Take(5).ToList();
            foreach (var s in topStocks)
            {
                _output.WriteLine($"Stock: {s.Symbol} ({s.CompanyName}) | Price: ₹{s.Price} | MCap: ₹{s.MarketCapCr}Cr | PE: {s.PeRatio} | ROE: {s.Roe}% | Badge: '{s.PrimaryBadge}' | Score: {s.Score}");
            }

            // Verify Sector Leader badge is assigned to the top stock by market cap
            var leader = result.Stocks.FirstOrDefault(s => s.PrimaryBadge != null && s.PrimaryBadge.Contains("Leader"));
            leader.Should().NotBeNull();
            _output.WriteLine($"Sector Leader identified: {leader!.Symbol} with MCap ₹{leader.MarketCapCr}Cr");
        }

        [Fact]
        public async Task Test_DailyValuationService_Execution()
        {
            var options = new DbContextOptionsBuilder<StockLensDataContext>()
                .UseSqlServer(TestDbConn)
                .Options;

            using var dbContext = new StockLensDataContext(options);
            using var httpClient = new HttpClient();
            var yahooClient = new YahooFinanceClient(httpClient, NullLogger<YahooFinanceClient>.Instance);
            var dailyService = new DailyValuationService(dbContext, yahooClient, NullLogger<DailyValuationService>.Instance);

            _output.WriteLine("Testing DailyValuationService.UpdateDailyValuationsAsync...");
            var sw = Stopwatch.StartNew();
            var updateResult = await dailyService.UpdateDailyValuationsAsync();
            sw.Stop();

            _output.WriteLine($"Daily update completed in {sw.ElapsedMilliseconds} ms: {updateResult.TotalUpdated}/{updateResult.TotalStocksFound} updated, {updateResult.TotalFailed} failed.");
            updateResult.TotalStocksFound.Should().BeGreaterThan(0, "DB should have seeded stocks to update");
            updateResult.TotalUpdated.Should().BeGreaterThan(0, "At least some stocks should have updated successfully");

            foreach (var sym in updateResult.UpdatedSymbols.Take(5))
            {
                _output.WriteLine($"Updated symbol: {sym}");
            }
        }

        [Fact]
        public async Task Test_BalanceSheetService_Shielded_Execution()
        {
            var options = new DbContextOptionsBuilder<StockLensDataContext>()
                .UseSqlServer(TestDbConn, opt => opt.CommandTimeout(60))
                .Options;

            using var dbContext = new StockLensDataContext(options);
            using var httpClient = new HttpClient();
            var yahooClient = new YahooFinanceClient(httpClient, NullLogger<YahooFinanceClient>.Instance);

            var stockRepo = new StockLens_Infrastructure.Repositories.StockRepository(dbContext);
            var bsRepo = new StockLens_Infrastructure.Repositories.StockBalanceSheetRepository(dbContext);
            var finRepo = new StockLens_Infrastructure.Repositories.StockFinancialRepository(dbContext);
            var compRepo = new StockLens_Infrastructure.Repositories.CompanyRepository(dbContext);
            var mockFinProv = new StockLens_Infrastructure.ExternalServices.BharatStock.MockFinancialProvider(NullLogger<StockLens_Infrastructure.ExternalServices.BharatStock.MockFinancialProvider>.Instance);

            // Note: apiClient is null, proving that BalanceSheetService serves from DB / Yahoo WITHOUT IndianAPI!
            var bsService = new StockBalanceSheetService(
                apiClient: null!,
                stockRepository: stockRepo,
                balanceSheetRepository: bsRepo,
                financialRepository: finRepo,
                financialProvider: mockFinProv,
                companyRepository: compRepo,
                yahooClient: yahooClient,
                logger: NullLogger<StockBalanceSheetService>.Instance);

            _output.WriteLine("Testing StockBalanceSheetService for TCS (Shielded mode with null IndianAPI)...");
            var result = await bsService.GetBalanceSheetAsync("TCS");

            result.Should().NotBeNull();
            result.Symbol.Should().Be("TCS");
            _output.WriteLine($"TCS Balance Sheet: {result.Periods.Count} periods returned. LineItems count={result.LineItems.Count}, Source={result.Source}");

            result.Periods.Should().NotBeEmpty();
            result.LineItems.Should().NotBeEmpty();
            foreach (var item in result.LineItems.Take(5))
            {
                _output.WriteLine($"  Line Item: {item.Name} | Values: {string.Join(", ", item.Values.Select(v => $"₹{v}Cr"))}");
            }
        }
    }
}
