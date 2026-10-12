using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class CompanyRepository : ICompanyRepository
    {
        private readonly StockLensDataContext _context;

        public CompanyRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Company>> SearchCompaniesAsync(string query, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<Company>();

            var lowerQuery = query.ToLower();
            
            return await _context.CompanyMaster
                .AsNoTracking()
                .Where(c => c.Symbol.ToLower().Contains(lowerQuery) || c.CompanyName.ToLower().Contains(lowerQuery))
                .OrderBy(c => c.Symbol.ToLower().StartsWith(lowerQuery) ? 0 : 1)
                .ThenBy(c => c.Symbol)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<Company?> GetCompanyBySymbolAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            return await _context.CompanyMaster
                .FirstOrDefaultAsync(c => c.Symbol.ToLower() == symbol.ToLower());
        }

        public async Task<Company> SaveOrUpdateCompanyAsync(Company company)
        {
            var existing = await _context.CompanyMaster
                .FirstOrDefaultAsync(c => c.Symbol.ToLower() == company.Symbol.ToLower());

            if (existing == null)
            {
                company.CreatedAt = System.DateTime.UtcNow;
                company.UpdatedAt = System.DateTime.UtcNow;
                _context.CompanyMaster.Add(company);
                await _context.SaveChangesAsync();
                return company;
            }

            if (!string.IsNullOrWhiteSpace(company.CompanyName)) existing.CompanyName = company.CompanyName;
            if (!string.IsNullOrWhiteSpace(company.Industry)) existing.Industry = company.Industry;
            if (!string.IsNullOrWhiteSpace(company.Sector)) existing.Sector = company.Sector;
            if (!string.IsNullOrWhiteSpace(company.LogoUrl)) existing.LogoUrl = company.LogoUrl;
            if (!string.IsNullOrWhiteSpace(company.WebsiteUrl)) existing.WebsiteUrl = company.WebsiteUrl;
            if (!string.IsNullOrWhiteSpace(company.About)) existing.About = company.About;
            if (!string.IsNullOrWhiteSpace(company.KeyPointsJson)) existing.KeyPointsJson = company.KeyPointsJson;
            if (company.EmployeesCount.HasValue) existing.EmployeesCount = company.EmployeesCount;
            if (!string.IsNullOrWhiteSpace(company.BseCode)) existing.BseCode = company.BseCode;
            if (!string.IsNullOrWhiteSpace(company.NseCode)) existing.NseCode = company.NseCode;
            existing.UpdatedAt = System.DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }
        public async Task EnsureCompaniesExistAsync(IEnumerable<Company> companies, System.Threading.CancellationToken cancellationToken = default)
        {
            var companyList = companies.ToList();
            if (!companyList.Any()) return;

            var symbols = companyList.Select(c => c.Symbol.ToLower()).Distinct().ToList();
            var existingCompanies = await _context.CompanyMaster
                .Where(c => symbols.Contains(c.Symbol.ToLower()))
                .ToListAsync(cancellationToken);

            var existingMap = existingCompanies.ToDictionary(c => c.Symbol.ToLower());

            foreach (var incoming in companyList)
            {
                var lower = incoming.Symbol.ToLower();
                if (existingMap.TryGetValue(lower, out var existing))
                {
                    bool modified = false;
                    if (!string.IsNullOrWhiteSpace(incoming.Sector) && string.IsNullOrWhiteSpace(existing.Sector))
                    {
                        existing.Sector = incoming.Sector.Trim();
                        modified = true;
                    }
                    if (!string.IsNullOrWhiteSpace(incoming.CompanyName) && (string.IsNullOrWhiteSpace(existing.CompanyName) || existing.CompanyName.Equals(existing.Symbol, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.CompanyName = incoming.CompanyName.Trim();
                        modified = true;
                    }
                    if (modified)
                    {
                        existing.UpdatedAt = System.DateTime.UtcNow;
                    }
                }
                else
                {
                    incoming.CreatedAt = System.DateTime.UtcNow;
                    incoming.UpdatedAt = System.DateTime.UtcNow;
                    _context.CompanyMaster.Add(incoming);
                    existingMap[lower] = incoming;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
