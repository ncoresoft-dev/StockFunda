using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class SectorSummaryDto
    {
        public string SectorName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TotalCompanies { get; set; }
        public decimal AverageMarketCapCr { get; set; }
        public int AdvanceCount { get; set; }
        public int DeclineCount { get; set; }
        public int NeutralCount { get; set; }
        public decimal AdvanceDeclineRatio { get; set; }
        public decimal SectorChangePercent { get; set; }
        public decimal AveragePe { get; set; }
        public decimal AveragePb { get; set; }
        public decimal AverageRoe { get; set; }
        public decimal AverageRoce { get; set; }
        public decimal SectorScore { get; set; }
        public int SectorRank { get; set; }
        public int TotalSectors { get; set; }
    }

    public class SectorStockDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal MarketCapCr { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? Roe { get; set; }
        public decimal? Roce { get; set; }
        public decimal Score { get; set; }
        public string? PrimaryBadge { get; set; }
        public List<string> Badges { get; set; } = new();
        public string? LogoUrl { get; set; }
    }

    public class SectorAnalysisResponseDto
    {
        public SectorSummaryDto Summary { get; set; } = new();
        public List<SectorStockDto> Stocks { get; set; } = new();
        public List<SectorStockDto> TopGainers { get; set; } = new();
        public List<SectorStockDto> TopLosers { get; set; } = new();
        public SectorStockDto? BestOverallStock { get; set; }
        public List<string> AvailableSectors { get; set; } = new();
    }
}

