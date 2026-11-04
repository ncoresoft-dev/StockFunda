using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface ISectorAnalysisService
    {
        Task<IReadOnlyList<string>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default);
        Task<SectorAnalysisResponseDto> GetSectorAnalysisAsync(string? sectorName = null, CancellationToken cancellationToken = default);
    }
}

