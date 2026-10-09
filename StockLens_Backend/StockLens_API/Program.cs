using Microsoft.EntityFrameworkCore;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_BusinessLayer.MapperProfile;
using StockLens_Infrastructure.ExternalServices.GoogleNews;
using StockLens_Infrastructure.Repositories;
using StockLens_Infrastructure.Seeders;
using System.Net;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddNewtonsoftJson(options => options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore);

builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Database Context
builder.Services.AddDbContext<StockLensDataContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("StockLensConnString"));
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

// Configure Backup In-Memory Providers (Strictly safe in-memory fallbacks to prevent 429 Rate Limits)
builder.Services.Configure<BharatStockSettings>(builder.Configuration.GetSection(BharatStockSettings.SectionName));
builder.Services.AddScoped<IShareholdingProvider, MockShareholdingProvider>();
builder.Services.AddScoped<IFinancialProvider, MockFinancialProvider>();

// Configure BharatStock Settings & Reusable Client Configuration
var bharatStockConfig = builder.Configuration.GetSection(BharatStockSettings.SectionName).Get<BharatStockSettings>() ?? new BharatStockSettings();
var bharatStockBaseUrl = !string.IsNullOrWhiteSpace(bharatStockConfig.BaseUrl) ? bharatStockConfig.BaseUrl.TrimEnd('/') + "/" : "https://bharatstockapi.com/";
var bharatStockTimeout = TimeSpan.FromSeconds(bharatStockConfig.TimeoutSeconds > 0 ? bharatStockConfig.TimeoutSeconds : 15);

void ConfigureBharatStockClient(HttpClient client)
{
    client.BaseAddress = new Uri(bharatStockBaseUrl);
    client.Timeout = bharatStockTimeout;
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}

// Configure BharatStock HTTP Clients
builder.Services.AddHttpClient<IDealsProvider, BharatStockDealsProvider>(ConfigureBharatStockClient);
builder.Services.AddHttpClient<ICompareProvider, BharatStockCompareProvider>(ConfigureBharatStockClient);

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
builder.Services.AddScoped<IStockNewsRepository, StockNewsRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IStockShareholdingRepository, StockShareholdingRepository>();
builder.Services.AddScoped<IStockFinancialRepository, StockFinancialRepository>();
builder.Services.AddScoped<IStockBalanceSheetRepository, StockBalanceSheetRepository>();
builder.Services.AddScoped<IStockPriceHistoryRepository, StockPriceHistoryRepository>();
builder.Services.AddScoped<IStockDealsRepository, StockDealsRepository>();
builder.Services.AddScoped<IStockPeerRepository, StockPeerRepository>();

// Register Seeders
builder.Services.AddTransient<CompanyMasterSeeder>();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Execute Seeders on Startup
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<CompanyMasterSeeder>();
    await seeder.SeedAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
