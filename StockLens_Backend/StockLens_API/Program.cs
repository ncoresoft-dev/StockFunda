using Microsoft.EntityFrameworkCore;
using StockLens_API.BackgroundServices;
using StockLens_API.Middleware;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.MapperProfile;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.GoogleNews;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.Repositories;
using StockLens_Infrastructure.Seeders;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Add services to the container.
builder.Services.AddControllers()
    .AddNewtonsoftJson(options => options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore);

// Global Exception Handler & ProblemDetails
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Database Context
builder.Services.AddDbContext<StockLensDataContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("StockLensConnString"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(60);
    });
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// Configure IndianAPI Settings
builder.Services.Configure<IndianApiSettings>(builder.Configuration.GetSection(IndianApiSettings.SectionName));

var indianApiConfig = builder.Configuration.GetSection(IndianApiSettings.SectionName).Get<IndianApiSettings>() ?? new IndianApiSettings();
var indianApiBaseUrl = !string.IsNullOrWhiteSpace(indianApiConfig.BaseUrl) ? indianApiConfig.BaseUrl.TrimEnd('/') + "/" : "https://stock.indianapi.in/";
var indianApiTimeout = TimeSpan.FromSeconds(indianApiConfig.TimeoutSeconds > 0 ? indianApiConfig.TimeoutSeconds : 20);

void ConfigureIndianApiClient(HttpClient client)
{
    client.BaseAddress = new Uri(indianApiBaseUrl);
    client.Timeout = indianApiTimeout;
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}

// Configure IndianAPI HTTP Clients
builder.Services.AddHttpClient<IIndianApiNewsClient, IndianApiNewsClient>(ConfigureIndianApiClient);
builder.Services.AddHttpClient<IIndianApiShareholdingClient, IndianApiShareholdingClient>(ConfigureIndianApiClient);
builder.Services.AddHttpClient<IIndianApiBalanceSheetClient, IndianApiBalanceSheetClient>(ConfigureIndianApiClient);
builder.Services.AddHttpClient<IIndianApiHistoricalDataClient, IndianApiHistoricalDataClient>(ConfigureIndianApiClient);
builder.Services.AddHttpClient<IIndianApiRatiosClient, IndianApiRatiosClient>(ConfigureIndianApiClient);

// Configure BharatStock Settings & Reusable Client Configuration
builder.Services.Configure<BharatStockSettings>(builder.Configuration.GetSection(BharatStockSettings.SectionName));

var bharatStockConfig = builder.Configuration.GetSection(BharatStockSettings.SectionName).Get<BharatStockSettings>() ?? new BharatStockSettings();
var bharatStockBaseUrl = !string.IsNullOrWhiteSpace(bharatStockConfig.BaseUrl) ? bharatStockConfig.BaseUrl.TrimEnd('/') + "/" : "https://bharatstockapi.com/";
var bharatStockTimeout = TimeSpan.FromSeconds(bharatStockConfig.TimeoutSeconds > 0 ? bharatStockConfig.TimeoutSeconds : 15);

void ConfigureBharatStockClient(HttpClient client)
{
    client.BaseAddress = new Uri(bharatStockBaseUrl);
    client.Timeout = bharatStockTimeout;
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}

// Configure BharatStock HTTP Clients (Real providers with fallback to mock only on HTTP 429 rate limit)
builder.Services.AddHttpClient<IShareholdingProvider, BharatStockShareholdingProvider>(ConfigureBharatStockClient);
builder.Services.AddHttpClient<IFinancialProvider, BharatStockFinancialProvider>(ConfigureBharatStockClient);
builder.Services.AddHttpClient<IDealsProvider, BharatStockDealsProvider>(ConfigureBharatStockClient);
builder.Services.AddHttpClient<ICompareProvider, BharatStockCompareProvider>(ConfigureBharatStockClient);
builder.Services.AddHttpClient<IIndicesProvider, BharatStockIndicesProvider>(ConfigureBharatStockClient);

// Register Yahoo Finance HTTP Client
builder.Services.AddHttpClient<StockLens_Infrastructure.ExternalServices.YahooFinanceApi.IYahooFinanceClient, StockLens_Infrastructure.ExternalServices.YahooFinanceApi.YahooFinanceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Register Google News HTTP Client
builder.Services.AddHttpClient<IGoogleNewsClient, GoogleNewsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Register Repositories
builder.Services.AddScoped<IStockRepository, StockRepository>();
builder.Services.AddScoped<IMarketIndexConstituentRepository, MarketIndexConstituentRepository>();
builder.Services.AddScoped<IStockNewsRepository, StockNewsRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IStockShareholdingRepository, StockShareholdingRepository>();
builder.Services.AddScoped<IStockFinancialRepository, StockFinancialRepository>();
builder.Services.AddScoped<IStockBalanceSheetRepository, StockBalanceSheetRepository>();
builder.Services.AddScoped<IStockPriceHistoryRepository, StockPriceHistoryRepository>();
builder.Services.AddScoped<IStockDealsRepository, StockDealsRepository>();
builder.Services.AddScoped<IStockPeerRepository, StockPeerRepository>();
builder.Services.AddScoped<IStockAnnualReportRepository, StockAnnualReportRepository>();
builder.Services.AddScoped<IMarketTickerRepository, MarketTickerRepository>();

// Register Seeders & Daily Valuation
builder.Services.AddTransient<CompanyMasterSeeder>();
builder.Services.AddTransient<StockFundamentalsSeeder>();
builder.Services.AddScoped<IDailyValuationService, DailyValuationService>();

// Register Screener-style Background Services
builder.Services.AddSingleton<StockSeedingBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StockSeedingBackgroundService>());

builder.Services.AddSingleton<DailyValuationBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DailyValuationBackgroundService>());

// Register Screener HTTP Client
builder.Services.Configure<StockLens_Infrastructure.ExternalServices.Screener.ScreenerSettings>(builder.Configuration.GetSection(StockLens_Infrastructure.ExternalServices.Screener.ScreenerSettings.SectionName));
builder.Services.AddHttpClient<StockLens_Infrastructure.ExternalServices.Screener.IScreenerClient, StockLens_Infrastructure.ExternalServices.Screener.ScreenerClient>(client =>
{
    client.BaseAddress = new Uri("https://www.screener.in/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Register Gemini & Annual Report AI Extractor
builder.Services.Configure<StockLens_Infrastructure.ExternalServices.AnnualReportInsights.GeminiSettings>(
    builder.Configuration.GetSection(StockLens_Infrastructure.ExternalServices.AnnualReportInsights.GeminiSettings.SectionName));
builder.Services.AddHttpClient<StockLens_Infrastructure.ExternalServices.AnnualReportInsights.IAnnualReportInsightExtractor, StockLens_Infrastructure.ExternalServices.AnnualReportInsights.AnnualReportInsightExtractor>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});

// Register Business Services
builder.Services.AddScoped<IStockNewsService, StockNewsService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IPeerService, PeerService>();
builder.Services.AddScoped<IStockShareholdingService, StockShareholdingService>();
builder.Services.AddScoped<ISectorValuationService, SectorValuationService>();
builder.Services.AddScoped<IStockCashflowService, StockCashflowService>();
builder.Services.AddScoped<IStockBalanceSheetService, StockBalanceSheetService>();
builder.Services.AddScoped<IStockPriceHistoryService, StockPriceHistoryService>();
builder.Services.AddScoped<IStockQuarterlyResultsService, StockQuarterlyResultsService>();
builder.Services.AddScoped<IStockEvaluationService, StockEvaluationService>();
builder.Services.AddScoped<IStockDealsService, StockDealsService>();
builder.Services.AddScoped<IMarketIndicesService, MarketIndicesService>();
builder.Services.AddScoped<IStockAnnualReportService, StockAnnualReportService>();
builder.Services.AddScoped<IMarketTickerService, MarketTickerService>();
builder.Services.AddScoped<IExploreLensService, ExploreLensService>();
builder.Services.AddScoped<IStockComparisonService, StockComparisonService>();
builder.Services.AddScoped<ISectorAnalysisService, SectorAnalysisService>();

// Register AutoMapper
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MapperProfile>());

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin();
    });
});

var app = builder.Build();

// Configure Global Exception Handling Middleware
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Execute Seeders on Startup
using (var scope = app.Services.CreateScope())
{
    var companySeeder = scope.ServiceProvider.GetRequiredService<CompanyMasterSeeder>();
    await companySeeder.SeedAsync();

    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var seedOnStartup = args.Contains("--seed-sector-leaders") || 
                        configuration.GetValue<bool>("SeederSettings:SeedSectorLeadersOnStartup", false);

    if (seedOnStartup)
    {
        var limitPerSector = configuration.GetValue<int>("SeederSettings:LimitPerSector", 3);
        var fundamentalsSeeder = scope.ServiceProvider.GetRequiredService<StockFundamentalsSeeder>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("===============================================================");
        logger.LogInformation("🚀 [Application Flow] Seeding Top Sector Leaders (limit: {Limit} per sector)...", limitPerSector);
        logger.LogInformation("===============================================================");
        
        var progress = await fundamentalsSeeder.SeedSectorLeadersAsync(limitPerSector);
        
        logger.LogInformation("✅ [Application Flow] Seeding Complete! {Succeeded}/{Total} stocks succeeded across {Sectors} sectors in {Elapsed:g}.",
            progress.TotalStocksSucceeded, progress.TotalStocksProcessed, progress.TotalSectorsDiscovered, progress.ElapsedTime);
    }

    var runDailyUpdate = args.Contains("--run-daily-update") || 
                         configuration.GetValue<bool>("SeederSettings:RunDailyUpdateOnStartup", false);

    if (runDailyUpdate)
    {
        var dailyValuation = scope.ServiceProvider.GetRequiredService<IDailyValuationService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("🔄 [Application Flow] Running Daily Valuation Update...");
        var valResult = await dailyValuation.UpdateDailyValuationsAsync();
        logger.LogInformation("✅ [Application Flow] Daily Valuation Complete! {Updated}/{Total} updated in {Elapsed:g}.",
            valResult.TotalUpdated, valResult.TotalStocksFound, valResult.ElapsedTime);
    }

    if (args.Contains("--report-status"))
    {
        var db = scope.ServiceProvider.GetRequiredService<StockLensDataContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var totalCompanies = await db.CompanyMaster.CountAsync();
        var totalFinancials = await db.StockFinancials.CountAsync();
        var seededStocks = await db.StockFinancials.Select(f => f.StockId).Distinct().CountAsync();
        var totalQuotes = await db.MarketTickerQuotes.CountAsync();
        logger.LogInformation("📊 [Database Summary] Companies: {Companies}, Seeded Stocks: {SeededStocks}, Financial Records: {Financials}, Live Ticker Quotes: {Quotes}",
            totalCompanies, seededStocks, totalFinancials, totalQuotes);
    }

    if (args.Contains("--test-sector-analysis"))
    {
        var sectorService = scope.ServiceProvider.GetRequiredService<ISectorAnalysisService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var sectorsToTest = new[] { "Information Technology", "Financial Services", "Oil, Gas & Consumable Fuels" };
        foreach (var sec in sectorsToTest)
        {
            sw.Restart();
            var res = await sectorService.GetSectorAnalysisAsync(sec);
            sw.Stop();
            logger.LogInformation("===============================================================");
            logger.LogInformation("🎯 [Sector Analysis Live] {Sector} (Query Time: {Elapsed}ms)", sec, sw.ElapsedMilliseconds);
            logger.LogInformation("   Metrics -> Companies: {Total} | Breadth: +{Adv} / -{Dec} | Avg PE: {Pe} | Avg MCap: ₹{MCap} Cr | Sector Change: {Chg}%",
                res.Summary.TotalCompanies, res.Summary.AdvanceCount, res.Summary.DeclineCount,
                res.Summary.AveragePe, res.Summary.AverageMarketCapCr, res.Summary.SectorChangePercent);
            
            logger.LogInformation("   Top Seeded Constituents & Dynamic Badges:");
            foreach (var st in res.Stocks.Take(5))
            {
                var badges = st.Badges != null && st.Badges.Count > 0 ? string.Join(" | ", st.Badges) : (st.PrimaryBadge ?? "None");

                logger.LogInformation("   -> {Symbol,-12} | Price: ₹{Price,8:F2} | MCap: ₹{MCap,10:F2} Cr | PE: {Pe,6:F1} | ROE: {Roe,5:F1}% | Badges: [{Badges}]",
                    st.Symbol, st.Price, st.MarketCapCr, st.PeRatio ?? 0, st.Roe ?? 0, badges);
            }
        }
    }

    if (args.Contains("--seed-and-exit"))
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Exit flag detected. Terminating process cleanly.");
        return;
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
