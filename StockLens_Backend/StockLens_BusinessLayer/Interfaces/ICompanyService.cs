using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface ICompanyService
    {
        Task<IEnumerable<CompanyDto>> SearchCompaniesAsync(string query, int limit = 10);
        Task<CompanyOverviewDto?> GetCompanyOverviewAsync(string symbol, string? exchange = "NSE", bool forceRefresh = false, System.Threading.CancellationToken cancellationToken = default);
    }
}
