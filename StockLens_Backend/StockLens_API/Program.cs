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
});

// Configure IndianAPI Settings
builder.Services.Configure<IndianApiSettings>(builder.Configuration.GetSection(IndianApiSettings.SectionName));

// Configure IndianAPI HTTP Clients
builder.Services.AddHttpClient<IIndianApiNewsClient, IndianApiNewsClient>((serviceProvider, client) =>
{
    var config = builder.Configuration.GetSection(IndianApiSettings.SectionName).Get<IndianApiSettings>() ?? new IndianApiSettings();
    var baseUrl = !string.IsNullOrWhiteSpace(config.BaseUrl) ? config.BaseUrl.TrimEnd('/') + "/" : "https://stock.indianapi.in/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 20);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddHttpClient<IIndianApiShareholdingClient, IndianApiShareholdingClient>((serviceProvider, client) =>
{
    var config = builder.Configuration.GetSection(IndianApiSettings.SectionName).Get<IndianApiSettings>() ?? new IndianApiSettings();
    var baseUrl = !string.IsNullOrWhiteSpace(config.BaseUrl) ? config.BaseUrl.TrimEnd('/') + "/" : "https://stock.indianapi.in/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 20);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddHttpClient<IIndianApiBalanceSheetClient, IndianApiBalanceSheetClient>((serviceProvider, client) =>
{
    var config = builder.Configuration.GetSection(IndianApiSettings.SectionName).Get<IndianApiSettings>() ?? new IndianApiSettings();
    var baseUrl = !string.IsNullOrWhiteSpace(config.BaseUrl) ? config.BaseUrl.TrimEnd('/') + "/" : "https://stock.indianapi.in/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 20);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddHttpClient<IIndianApiHistoricalDataClient, IndianApiHistoricalDataClient>((serviceProvider, client) =>
{
    var config = builder.Configuration.GetSection(IndianApiSettings.SectionName).Get<IndianApiSettings>() ?? new IndianApiSettings();
    var baseUrl = !string.IsNullOrWhiteSpace(config.BaseUrl) ? config.BaseUrl.TrimEnd('/') + "/" : "https://stock.indianapi.in/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 20);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddHttpClient<IIndianApiRatiosClient, IndianApiRatiosClient>((serviceProvider, client) =>
{
    var config = builder.Configuration.GetSection(IndianApiSettings.SectionName).Get<IndianApiSettings>() ?? new IndianApiSettings();
    var baseUrl = !string.IsNullOrWhiteSpace(config.BaseUrl) ? config.BaseUrl.TrimEnd('/') + "/" : "https://stock.indianapi.in/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 20);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Configure Backup In-Memory Providers (Strictly safe in-memory fallbacks to prevent 429 Rate Limits)
builder.Services.Configure<BharatStockSettings>(builder.Configuration.GetSection(BharatStockSettings.SectionName));
builder.Services.AddScoped<IShareholdingProvider, MockShareholdingProvider>();
builder.Services.AddScoped<IFinancialProvider, MockFinancialProvider>();

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

// Register Seeders
builder.Services.AddTransient<CompanyMasterSeeder>();

// Register Business Services
builder.Services.AddScoped<IStockNewsService, StockNewsService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IStockShareholdingService, StockShareholdingService>();
builder.Services.AddScoped<ISectorValuationService, SectorValuationService>();
builder.Services.AddScoped<IStockCashflowService, StockCashflowService>();
builder.Services.AddScoped<IStockBalanceSheetService, StockBalanceSheetService>();
builder.Services.AddScoped<IStockPriceHistoryService, StockPriceHistoryService>();
builder.Services.AddScoped<IStockQuarterlyResultsService, StockQuarterlyResultsService>();
builder.Services.AddScoped<IStockEvaluationService, StockEvaluationService>();


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
