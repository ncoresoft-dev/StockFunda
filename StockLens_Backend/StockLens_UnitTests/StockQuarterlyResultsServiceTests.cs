using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.MapperProfile;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StockLens_UnitTests
{
    public class StockQuarterlyResultsServiceTests
    {
        private readonly IMapper _mapper;

        public StockQuarterlyResultsServiceTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAutoMapper(cfg => cfg.AddProfile<MapperProfile>());
            var serviceProvider = services.BuildServiceProvider();
            _mapper = serviceProvider.GetRequiredService<IMapper>();
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_WhenDbHasFreshData_ShouldReturnFromDbWithoutCallingExternalApis()
        {
            // Arrange
            var stock = new Stock { Id = 1, Symbol = "TCS", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("TCS", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            var dbQuarters = new List<StockFinancial>
            {
                new StockFinancial
                {
                    Id = 101,
                    StockId = 1,
                    PeriodKey = "quarterly-2024-12-31",
                    PeriodType = "quarterly",
                    FiscalYear = "Dec 2024",
                    PeriodEndDate = new DateTime(2024, 12, 31),
                    Revenue = 64259m,
                    OperatingProfit = 15900m,
                    Depreciation = 1350m,
                    ProfitBeforeTax = 16200m,
                    Tax = 4100m,
                    NetProfit = 12050m,
                    Interest = 200m,
                    Eps = 33.2m,
                    Source = "IndianAPI",
                    LastSyncedAt = DateTime.UtcNow
                }
            };

            var financialRepo = new Mock<IStockFinancialRepository>();
            financialRepo.Setup(r => r.GetFinancialsByStockIdAsync(1, "quarterly", 12))
                .ReturnsAsync(dbQuarters);

            var companyRepo = new Mock<ICompanyRepository>();
            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            var yahooMock = new Mock<IYahooFinanceClient>();

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("TCS", "NSE", forceRefresh: false);

            // Assert
            result.Should().NotBeNull();
            result.Symbol.Should().Be("TCS");
            result.Summary.Sales.Should().Be(64259m);
            result.Summary.Depreciation.Should().Be(1350m);
            result.Summary.Tax.Should().Be(4100m);
            result.Summary.NetProfit.Should().Be(12050m);
            result.Summary.Eps.Should().Be(33.2m);

            indianApiMock.Verify(p => p.GetStockFinancialsAndOverviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            yahooMock.Verify(p => p.GetQuarterlyIncomeStatementsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_WhenDbEmpty_ShouldFetchFromIndianApiAndUpsert()
        {
            // Arrange
            var stock = new Stock { Id = 2, Symbol = "RELIANCE", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("RELIANCE", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            var financialRepo = new Mock<IStockFinancialRepository>();
            var savedEntities = new List<StockFinancial>();

            // First call returns empty, second call returns saved entities
            financialRepo.SetupSequence(r => r.GetFinancialsByStockIdAsync(2, "quarterly", 12))
                .ReturnsAsync(new List<StockFinancial>())
                .ReturnsAsync(savedEntities);

            financialRepo.Setup(r => r.GetByStockIdAndPeriodKeyAsync(2, It.IsAny<string>()))
                .ReturnsAsync((StockFinancial?)null);

            financialRepo.Setup(r => r.AddAsync(It.IsAny<StockFinancial>()))
                .Callback<StockFinancial>(e => savedEntities.Add(e))
                .ReturnsAsync((StockFinancial e) => e);

            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            var mockOverview = new IndianApiStockOverviewDto
            {
                Symbol = "RELIANCE",
                Financials = new List<IndianApiFinancialPeriodDto>
                {
                    new IndianApiFinancialPeriodDto
                    {
                        FiscalYear = "Dec 2024",
                        PeriodEndDate = new DateTime(2024, 12, 31),
                        PeriodType = "quarterly",
                        Revenue = 235480m,
                        OperatingProfit = 40500m,
                        Depreciation = 12500m,
                        ProfitBeforeTax = 28000m,
                        Tax = 7000m,
                        NetProfit = 21000m,
                        Eps = 31.05m
                    }
                }
            };

            indianApiMock.Setup(p => p.GetStockFinancialsAndOverviewAsync("RELIANCE", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockOverview);

            var companyRepo = new Mock<ICompanyRepository>();
            var yahooMock = new Mock<IYahooFinanceClient>();

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("RELIANCE", "NSE", forceRefresh: true);

            // Assert
            result.Should().NotBeNull();
            result.Symbol.Should().Be("RELIANCE");
            savedEntities.Should().HaveCount(1);
            savedEntities[0].Revenue.Should().Be(235480m);
            savedEntities[0].Depreciation.Should().Be(12500m);
            savedEntities[0].Tax.Should().Be(7000m);
            savedEntities[0].NetProfit.Should().Be(21000m);
            savedEntities[0].Eps.Should().Be(31.05m);
            savedEntities[0].Source.Should().Be("IndianAPI");
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_WhenIndianApiFails_ShouldFallbackToYahooFinance()
        {
            // Arrange
            var stock = new Stock { Id = 3, Symbol = "INFY", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("INFY", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            var financialRepo = new Mock<IStockFinancialRepository>();
            var savedEntities = new List<StockFinancial>();

            financialRepo.SetupSequence(r => r.GetFinancialsByStockIdAsync(3, "quarterly", 12))
                .ReturnsAsync(new List<StockFinancial>())
                .ReturnsAsync(savedEntities);

            financialRepo.Setup(r => r.GetByStockIdAndPeriodKeyAsync(3, It.IsAny<string>()))
                .ReturnsAsync((StockFinancial?)null);

            financialRepo.Setup(r => r.AddAsync(It.IsAny<StockFinancial>()))
                .Callback<StockFinancial>(e => savedEntities.Add(e))
                .ReturnsAsync((StockFinancial e) => e);

            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            // IndianAPI throws 429 Rate limit or error
            indianApiMock.Setup(p => p.GetStockFinancialsAndOverviewAsync("INFY", "NSE", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("429 Too Many Requests"));

            var yahooMock = new Mock<IYahooFinanceClient>();
            var yfQuarters = new List<IndianApiFinancialPeriodDto>
            {
                new IndianApiFinancialPeriodDto
                {
                    FiscalYear = "Dec 2024",
                    PeriodEndDate = new DateTime(2024, 12, 31),
                    PeriodType = "quarterly",
                    Revenue = 40986m,
                    OperatingProfit = 8400m,
                    Depreciation = 1100m,
                    ProfitBeforeTax = 8800m,
                    Tax = 2200m,
                    NetProfit = 6600m,
                    Eps = 15.8m
                }
            };

            yahooMock.Setup(y => y.GetQuarterlyIncomeStatementsAsync("INFY", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(yfQuarters);

            var companyRepo = new Mock<ICompanyRepository>();

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("INFY", "NSE", forceRefresh: true);

            // Assert
            result.Should().NotBeNull();
            result.Symbol.Should().Be("INFY");
            savedEntities.Should().HaveCount(1);
            savedEntities[0].Revenue.Should().Be(40986m);
            savedEntities[0].Depreciation.Should().Be(1100m);
            savedEntities[0].Tax.Should().Be(2200m);
            savedEntities[0].NetProfit.Should().Be(6600m);
            savedEntities[0].Source.Should().Be("YahooFinance");
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_ShouldCalculateCorrectQoQAndYoYGrowthMetrics()
        {
            // Arrange
            var stock = new Stock { Id = 4, Symbol = "HDFCBANK", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("HDFCBANK", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            var quarters = new List<StockFinancial>
            {
                // Latest: Q3 Dec 2024
                new StockFinancial { StockId = 4, PeriodEndDate = new DateTime(2024, 12, 31), FiscalYear = "Dec 2024", Revenue = 100000m, OperatingProfit = 25000m, Depreciation = 3000m, Tax = 5000m, NetProfit = 17000m, Eps = 22m, LastSyncedAt = DateTime.UtcNow },
                // Previous: Q2 Sep 2024
                new StockFinancial { StockId = 4, PeriodEndDate = new DateTime(2024, 9, 30), FiscalYear = "Sep 2024", Revenue = 90000m, OperatingProfit = 22000m, Depreciation = 2800m, Tax = 4500m, NetProfit = 14700m, Eps = 19m, LastSyncedAt = DateTime.UtcNow },
                // Q1 Jun 2024
                new StockFinancial { StockId = 4, PeriodEndDate = new DateTime(2024, 6, 30), FiscalYear = "Jun 2024", Revenue = 85000m, OperatingProfit = 21000m, Depreciation = 2700m, Tax = 4200m, NetProfit = 14100m, Eps = 18m, LastSyncedAt = DateTime.UtcNow },
                // Q4 Mar 2024
                new StockFinancial { StockId = 4, PeriodEndDate = new DateTime(2024, 3, 31), FiscalYear = "Mar 2024", Revenue = 82000m, OperatingProfit = 20000m, Depreciation = 2600m, Tax = 4000m, NetProfit = 13400m, Eps = 17m, LastSyncedAt = DateTime.UtcNow },
                // YoY Same Quarter: Q3 Dec 2023
                new StockFinancial { StockId = 4, PeriodEndDate = new DateTime(2023, 12, 31), FiscalYear = "Dec 2023", Revenue = 80000m, OperatingProfit = 19000m, Depreciation = 2500m, Tax = 3800m, NetProfit = 12700m, Eps = 16m, LastSyncedAt = DateTime.UtcNow }
            };

            var financialRepo = new Mock<IStockFinancialRepository>();
            financialRepo.Setup(r => r.GetFinancialsByStockIdAsync(4, "quarterly", 12))
                .ReturnsAsync(quarters);

            var companyRepo = new Mock<ICompanyRepository>();
            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            var yahooMock = new Mock<IYahooFinanceClient>();

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("HDFCBANK", "NSE", forceRefresh: false);

            // Assert
            result.Should().NotBeNull();
            result.History.Should().HaveCount(5);

            // QoQ growth: (100000 - 90000) / 90000 * 100 = 11.11%
            result.QoQGrowth.SalesGrowthPercent.Should().Be(11.11m);
            // QoQ Net Profit: (17000 - 14700) / 14700 * 100 = 15.65%
            result.QoQGrowth.NetProfitGrowthPercent.Should().Be(15.65m);

            // YoY growth: (100000 - 80000) / 80000 * 100 = 25.00%
            result.YoYGrowth.SalesGrowthPercent.Should().Be(25.00m);
            // YoY Net Profit: (17000 - 12700) / 12700 * 100 = 33.86%
            result.YoYGrowth.NetProfitGrowthPercent.Should().Be(33.86m);
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_WhenApisReturnNoData_ShouldReturnEmptyResponseWithDashesAndNotSeedMockData()
        {
            // Arrange
            var stock = new Stock { Id = 5, Symbol = "UNKNOWN", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("UNKNOWN", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            var financialRepo = new Mock<IStockFinancialRepository>();
            financialRepo.Setup(r => r.GetFinancialsByStockIdAsync(5, "quarterly", 12))
                .ReturnsAsync(new List<StockFinancial>());

            var companyRepo = new Mock<ICompanyRepository>();
            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            indianApiMock.Setup(p => p.GetStockFinancialsAndOverviewAsync("UNKNOWN", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync((IndianApiStockOverviewDto?)null);

            var yahooMock = new Mock<IYahooFinanceClient>();
            yahooMock.Setup(y => y.GetQuarterlyIncomeStatementsAsync("UNKNOWN", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<IndianApiFinancialPeriodDto>());

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("UNKNOWN", "NSE", forceRefresh: true);

            // Assert
            result.Should().NotBeNull();
            result.Symbol.Should().Be("UNKNOWN");
            result.LatestQuarter.Should().Be("—");
            result.Source.Should().Be("—");
            result.History.Should().BeEmpty();
            result.Summary.Period.Should().Be("—");
            result.Summary.Sales.Should().BeNull();

            // Should NEVER add dummy fallback records to DB
            financialRepo.Verify(f => f.AddAsync(It.IsAny<StockFinancial>()), Times.Never);
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_WhenDuplicateQuarterRecordsExistInDb_ShouldMergeAndDeduplicateDistinctQuarters()
        {
            // Arrange
            var stock = new Stock { Id = 6, Symbol = "RELIANCE", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("RELIANCE", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            // Simulate DB having 2 duplicate entries for Jun 2026 and 2 duplicate entries for Mar 2026 (one with tax, one without)
            var duplicateDbQuarters = new List<StockFinancial>
            {
                new StockFinancial
                {
                    Id = 201,
                    StockId = 6,
                    PeriodKey = "quarterly-2026-06-30",
                    PeriodEndDate = new DateTime(2026, 6, 30),
                    FiscalYear = "Jun 2026",
                    Revenue = 311850m,
                    OperatingProfit = 32417m,
                    Depreciation = 15100m,
                    Tax = 7629m,
                    NetProfit = 20946m,
                    Eps = 15.48m,
                    LastSyncedAt = DateTime.UtcNow
                },
                new StockFinancial
                {
                    Id = 202,
                    StockId = 6,
                    PeriodKey = "quarterly-jun-2026",
                    PeriodEndDate = new DateTime(2026, 6, 30),
                    FiscalYear = "Jun 2026",
                    Revenue = 311850m,
                    OperatingProfit = 32417m,
                    Depreciation = null,
                    Tax = null,
                    NetProfit = 20946m,
                    Eps = 15.48m,
                    LastSyncedAt = DateTime.UtcNow.AddMinutes(-10)
                },
                new StockFinancial
                {
                    Id = 203,
                    StockId = 6,
                    PeriodKey = "quarterly-2026-03-31",
                    PeriodEndDate = new DateTime(2026, 3, 31),
                    FiscalYear = "Mar 2026",
                    Revenue = 298621m,
                    OperatingProfit = 30000m,
                    Depreciation = 14500m,
                    Tax = 6579m,
                    NetProfit = 18971m,
                    Eps = 12.54m,
                    LastSyncedAt = DateTime.UtcNow
                },
                new StockFinancial
                {
                    Id = 204,
                    StockId = 6,
                    PeriodKey = "quarterly-mar-2026",
                    PeriodEndDate = new DateTime(2026, 3, 31),
                    FiscalYear = "Mar 2026",
                    Revenue = 298621m,
                    OperatingProfit = 30000m,
                    Depreciation = null,
                    Tax = null,
                    NetProfit = 18971m,
                    Eps = 12.54m,
                    LastSyncedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            };

            var financialRepo = new Mock<IStockFinancialRepository>();
            financialRepo.Setup(r => r.GetFinancialsByStockIdAsync(6, "quarterly", 12))
                .ReturnsAsync(duplicateDbQuarters);

            var companyRepo = new Mock<ICompanyRepository>();
            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            var yahooMock = new Mock<IYahooFinanceClient>();

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("RELIANCE", "NSE", forceRefresh: false);

            // Assert
            result.Should().NotBeNull();
            // Exactly 2 unique quarters (Jun 2026, Mar 2026) must be returned, not 4
            result.History.Should().HaveCount(2);
            result.History[0].Period.Should().Be("Jun 2026");
            result.History[0].Sales.Should().Be(311850m);
            result.History[0].Depreciation.Should().Be(15100m);
            result.History[0].Tax.Should().Be(7629m);
            result.History[0].NetProfit.Should().Be(20946m);

            result.History[1].Period.Should().Be("Mar 2026");
            result.History[1].Sales.Should().Be(298621m);
            result.History[1].Depreciation.Should().Be(14500m);
            result.History[1].Tax.Should().Be(6579m);
            result.History[1].NetProfit.Should().Be(18971m);
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_WhenTaxMissingInPayload_ShouldComputeFromProfitBeforeTaxAndNetProfitAndPersist()
        {
            // Arrange
            var stock = new Stock { Id = 7, Symbol = "INFY", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("INFY", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            var financialRepo = new Mock<IStockFinancialRepository>();
            var savedEntities = new List<StockFinancial>();

            financialRepo.SetupSequence(r => r.GetFinancialsByStockIdAsync(7, "quarterly", 12))
                .ReturnsAsync(new List<StockFinancial>())
                .ReturnsAsync(savedEntities);

            financialRepo.Setup(r => r.GetByStockIdAndPeriodKeyAsync(7, It.IsAny<string>()))
                .ReturnsAsync((StockFinancial?)null);

            financialRepo.Setup(r => r.AddAsync(It.IsAny<StockFinancial>()))
                .Callback<StockFinancial>(e => savedEntities.Add(e))
                .ReturnsAsync((StockFinancial e) => e);

            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            var mockOverview = new IndianApiStockOverviewDto
            {
                Symbol = "INFY",
                Financials = new List<IndianApiFinancialPeriodDto>
                {
                    new IndianApiFinancialPeriodDto
                    {
                        FiscalYear = "Dec 2024",
                        PeriodEndDate = new DateTime(2024, 12, 31),
                        PeriodType = "quarterly",
                        Revenue = 40000m,
                        OperatingProfit = 10000m,
                        Depreciation = 1200m,
                        ProfitBeforeTax = 9000m,
                        Tax = null, // Missing tax in payload
                        NetProfit = 6800m,
                        Eps = 16.5m
                    }
                }
            };

            indianApiMock.Setup(p => p.GetStockFinancialsAndOverviewAsync("INFY", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockOverview);

            var companyRepo = new Mock<ICompanyRepository>();
            var yahooMock = new Mock<IYahooFinanceClient>();

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("INFY", "NSE", forceRefresh: true);

            // Assert
            result.Should().NotBeNull();
            savedEntities.Should().HaveCount(1);
            // 9000 PBT - 6800 NetProfit = 2200 Tax
            savedEntities[0].Tax.Should().Be(2200m);
            // 2200 / 9000 * 100 = 24.44%
            savedEntities[0].TaxPercentage.Should().Be(24.44m);
            result.Summary.Tax.Should().Be(2200m);
            result.Summary.TaxPercentage.Should().Be(24.44m);
        }

        [Fact]
        public async Task GetQuarterlyResultsBySymbolAsync_WhenDbHasMissingTax_ShouldBackfillTaxIntoDb()
        {
            // Arrange
            var stock = new Stock { Id = 8, Symbol = "WIPRO", Exchange = "NSE" };
            var stockRepo = new Mock<IStockRepository>();
            stockRepo.Setup(r => r.GetOrCreateStockAsync("WIPRO", "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            var dbQuarters = new List<StockFinancial>
            {
                new StockFinancial
                {
                    Id = 301,
                    StockId = 8,
                    PeriodKey = "quarterly-2024-12-31",
                    PeriodType = "quarterly",
                    FiscalYear = "Dec 2024",
                    PeriodEndDate = new DateTime(2024, 12, 31),
                    Revenue = 22000m,
                    OperatingProfit = 4500m,
                    Depreciation = 800m,
                    Interest = 150m,
                    ProfitBeforeTax = 3800m,
                    Tax = null, // Missing tax in DB
                    NetProfit = 2900m,
                    Eps = 5.5m,
                    Source = "IndianAPI",
                    LastSyncedAt = DateTime.UtcNow
                }
            };

            var financialRepo = new Mock<IStockFinancialRepository>();
            financialRepo.Setup(r => r.GetFinancialsByStockIdAsync(8, "quarterly", 12))
                .ReturnsAsync(dbQuarters);

            var updatedEntities = new List<StockFinancial>();
            financialRepo.Setup(r => r.UpdateAsync(It.IsAny<StockFinancial>()))
                .Callback<StockFinancial>(e => updatedEntities.Add(e))
                .Returns(Task.CompletedTask);

            var companyRepo = new Mock<ICompanyRepository>();
            var indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            var yahooMock = new Mock<IYahooFinanceClient>();

            var service = new StockQuarterlyResultsService(
                stockRepo.Object,
                financialRepo.Object,
                companyRepo.Object,
                _mapper,
                NullLogger<StockQuarterlyResultsService>.Instance,
                indianApiMock.Object,
                yahooMock.Object);

            // Act
            var result = await service.GetQuarterlyResultsBySymbolAsync("WIPRO", "NSE", forceRefresh: false);

            // Assert
            result.Should().NotBeNull();
            // 3800 PBT - 2900 PAT = 900 Tax
            result.Summary.Tax.Should().Be(900m);
            // 900 / 3800 * 100 = 23.68%
            result.Summary.TaxPercentage.Should().Be(23.68m);
            // Verify DB update was triggered
            financialRepo.Verify(r => r.UpdateAsync(It.Is<StockFinancial>(f => f.Tax == 900m)), Times.AtLeastOnce);
            financialRepo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
        }

        [Fact]
        public void GetLatestCompletedQuarterEndDate_ShouldReturnCorrectDatesAcrossYear()
        {
            // January -> Dec 31 of prev year
            StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(new DateTime(2026, 1, 15)).Should().Be(new DateTime(2025, 12, 31));
            // March 30 -> Dec 31 of prev year
            StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(new DateTime(2026, 3, 30)).Should().Be(new DateTime(2025, 12, 31));
            // April 10 -> March 31
            StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(new DateTime(2026, 4, 10)).Should().Be(new DateTime(2026, 3, 31));
            // July 20 -> June 30
            StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(new DateTime(2026, 7, 20)).Should().Be(new DateTime(2026, 6, 30));
            // October 30 -> September 30
            StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(new DateTime(2026, 10, 30)).Should().Be(new DateTime(2026, 9, 30));
        }

        [Fact]
        public void IsSyncNeeded_WhenDbAlreadyHasLatestCompletedQuarter_ShouldLockAndReturnFalse()
        {
            var quarters = new List<StockFinancial>
            {
                new StockFinancial
                {
                    PeriodEndDate = StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(DateTime.UtcNow),
                    FiscalYear = "Current",
                    OperatingProfit = 15000m,
                    Eps = 25m,
                    Tax = 3000m,
                    ProfitBeforeTax = 12000m,
                    Source = "IndianAPI",
                    LastSyncedAt = DateTime.UtcNow.AddDays(-10) // Even if synced 10 days ago, it's locked!
                }
            };

            var syncNeeded = StockQuarterlyResultsService.IsSyncNeeded(quarters, forceRefresh: false, out var reason);

            syncNeeded.Should().BeFalse();
            reason.Should().Contain("Locked: Database already holds latest completed quarter");
        }

        [Fact]
        public void IsSyncNeeded_WhenLatestQuarterIsIncompleteYahooFinance_ShouldTriggerSelfHealing()
        {
            var quarters = new List<StockFinancial>
            {
                new StockFinancial
                {
                    PeriodEndDate = StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(DateTime.UtcNow),
                    FiscalYear = "Current",
                    Revenue = 50000m,
                    OperatingProfit = null, // Incomplete!
                    Eps = null,             // Incomplete!
                    Source = "YahooFinance",
                    LastSyncedAt = DateTime.UtcNow.AddHours(-2) // Checked 2 hours ago
                }
            };

            var syncNeeded = StockQuarterlyResultsService.IsSyncNeeded(quarters, forceRefresh: false, out var reason);

            syncNeeded.Should().BeTrue();
            reason.Should().Contain("incomplete fallback data");
        }

        [Fact]
        public void IsSyncNeeded_WhenInDeclarationWindowAndCheckedRecently_ShouldWaitAndReturnFalse()
        {
            var completedQtr = StockQuarterlyResultsService.GetLatestCompletedQuarterEndDate(DateTime.UtcNow);
            var quarters = new List<StockFinancial>
            {
                new StockFinancial
                {
                    PeriodEndDate = completedQtr.AddMonths(-3), // Still on previous quarter
                    FiscalYear = "Previous",
                    OperatingProfit = 12000m,
                    Eps = 20m,
                    Source = "IndianAPI",
                    LastSyncedAt = DateTime.UtcNow.AddDays(-1) // Checked only 1 day ago
                }
            };

            var syncNeeded = StockQuarterlyResultsService.IsSyncNeeded(quarters, forceRefresh: false, out var reason);

            syncNeeded.Should().BeFalse();
            reason.Should().Contain("Waiting");
        }
    }
}
