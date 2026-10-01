using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockLens_DataLayer.Entities;
using StockLens_Infrastructure.DataContext;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Seeders
{
    public class CompanyJsonDto
    {
        [JsonPropertyName("SYMBOL")]
        public string? Symbol { get; set; }

        [JsonPropertyName("NAME OF COMPANY")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("ISIN NUMBER")]
        public string? IsinNumber { get; set; }
    }

    public class CompanyMasterSeeder
    {
        public const string LogoApiBaseUrl = "https://company-logo.shareperks.in/logo/";

        private readonly StockLensDataContext _dbContext;
        private readonly ILogger<CompanyMasterSeeder> _logger;

        public CompanyMasterSeeder(
            StockLensDataContext dbContext,
            ILogger<CompanyMasterSeeder> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public static string? BuildLogoUrl(string? isin)
        {
            if (string.IsNullOrWhiteSpace(isin)) return null;
            return $"{LogoApiBaseUrl}{isin.Trim()}/icon.svg";
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("CompanyMasterSeeder is starting...");

            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "Seeders", "Data", "companies.json");
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Seeder JSON file not found at path: {FilePath}", filePath);
                    return;
                }

                _logger.LogInformation("Reading NSE Stocks JSON from {FilePath}", filePath);
                var jsonText = await File.ReadAllTextAsync(filePath, cancellationToken);
                var jsonCompanies = JsonSerializer.Deserialize<List<CompanyJsonDto>>(jsonText);

                if (jsonCompanies == null || jsonCompanies.Count == 0)
                {
                    _logger.LogWarning("No companies found in the JSON file.");
                    return;
                }

                // 1. Build fast dictionary for Symbol -> ISIN
                var isinMap = jsonCompanies
                    .Where(j => !string.IsNullOrWhiteSpace(j.Symbol) && !string.IsNullOrWhiteSpace(j.IsinNumber))
                    .GroupBy(j => j.Symbol!.Trim().ToUpperInvariant())
                    .ToDictionary(g => g.Key, g => g.First().IsinNumber!.Trim(), StringComparer.OrdinalIgnoreCase);

                // 2. Fetch existing companies from DB
                var existingCompanies = await _dbContext.CompanyMaster.ToListAsync(cancellationToken);
                var existingSymbols = new HashSet<string>(existingCompanies.Select(c => c.Symbol.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);

                // 3. Update existing records if LogoUrl is missing
                int updatedCount = 0;
                foreach (var company in existingCompanies)
                {
                    var cleanSym = company.Symbol.Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(company.LogoUrl))
                    {
                        if (isinMap.TryGetValue(cleanSym, out var isin) && !string.IsNullOrWhiteSpace(isin))
                        {
                            company.LogoUrl = BuildLogoUrl(isin);
                            company.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                }

                if (updatedCount > 0)
                {
                    _logger.LogInformation("Updated LogoUrl for {Count} existing Companies in CompanyMaster.", updatedCount);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                // 4. Add new companies that don't exist yet
                var newCompaniesToAdd = new List<Company>();
                foreach (var item in jsonCompanies)
                {
                    if (string.IsNullOrWhiteSpace(item.Symbol) || string.IsNullOrWhiteSpace(item.CompanyName))
                        continue;

                    var symbol = item.Symbol.Trim();
                    var companyName = item.CompanyName.Trim();
                    var upperSymbol = symbol.ToUpperInvariant();

                    if (!existingSymbols.Contains(upperSymbol))
                    {
                        var logoUrl = !string.IsNullOrWhiteSpace(item.IsinNumber)
                            ? BuildLogoUrl(item.IsinNumber)
                            : null;

                        newCompaniesToAdd.Add(new Company
                        {
                            Symbol = symbol,
                            CompanyName = companyName,
                            LogoUrl = logoUrl,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        existingSymbols.Add(upperSymbol);
                    }
                }

                if (newCompaniesToAdd.Count > 0)
                {
                    _logger.LogInformation("Adding {Count} new Companies with LogoUrl to the database...", newCompaniesToAdd.Count);
                    await _dbContext.CompanyMaster.AddRangeAsync(newCompaniesToAdd, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("CompanyMasterSeeder completed successfully. Added {Count} companies.", newCompaniesToAdd.Count);
                }
                else
                {
                    _logger.LogInformation("No new companies needed insertion. Database is fully synced.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while seeding CompanyMaster.");
            }
        }
    }
}
