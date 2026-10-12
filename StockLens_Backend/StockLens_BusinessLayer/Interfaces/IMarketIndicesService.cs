using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IMarketIndicesService
    {
        Task<IEnumerable<MarketIndexCompanyDto>> GetTop50CompaniesAsync(CancellationToken cancellationToken = default);
    }
}
