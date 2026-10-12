using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.BharatStock
{
    public interface IIndicesProvider
    {
        Task<System.Text.Json.JsonElement> GetTop50CompaniesAsync(CancellationToken cancellationToken = default);
    }
}
