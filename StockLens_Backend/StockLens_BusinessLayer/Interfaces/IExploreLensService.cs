using StockLens_BusinessLayer.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IExploreLensService
    {
        Task<ExploreLensResponseDto> GetExploreLensDataAsync(string lens = "Growth", bool forceRefresh = false, CancellationToken cancellationToken = default);
    }
}
