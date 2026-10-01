using StockLens_DataLayer.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StockLens_DataLayer.Interfaces
{
    public interface ICompanyRepository
    {
        Task<IEnumerable<Company>> SearchCompaniesAsync(string query, int limit);
        Task<Company?> GetCompanyBySymbolAsync(string symbol);
        Task<Company> SaveOrUpdateCompanyAsync(Company company);
    }
}
