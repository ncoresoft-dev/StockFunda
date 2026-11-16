using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class StockFlowDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal TradedValueCr { get; set; }
        public decimal FlowCr { get; set; }
        public decimal Weight { get; set; }
        public decimal Clv { get; set; }
        public decimal RelativeVolume { get; set; }
        public decimal Speed { get; set; }
        public bool IsSurge { get; set; }
        public bool IsFo { get; set; }
        public string? Buildup { get; set; }
        public string? LogoUrl { get; set; }
    }

    public class SectorFlowDto
    {
        public string Sector { get; set; } = string.Empty;
        public decimal InflowCr { get; set; }
        public decimal OutflowCr { get; set; }
        public decimal NetFlowCr { get; set; }
        public decimal TradedValueCr { get; set; }
        public int StockCount { get; set; }
        public int Advances { get; set; }
        public int Declines { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal Speed { get; set; }
        public bool IsSurge { get; set; }
        public List<StockFlowDto> Stocks { get; set; } = new();
    }

    public class MoneyFlowResponseDto
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string FormattedTimestamp { get; set; } = string.Empty;
        public decimal TotalInflowCr { get; set; }
        public decimal TotalOutflowCr { get; set; }
        public decimal NetFlowCr { get; set; }
        public int TotalStocks { get; set; }
        public int TotalSectors { get; set; }
        public int Advances { get; set; }
        public int Declines { get; set; }
        public int Unchanged { get; set; }
        public List<SectorFlowDto> Sectors { get; set; } = new();
        public List<StockFlowDto> TopInflowStocks { get; set; } = new();
        public List<StockFlowDto> TopOutflowStocks { get; set; } = new();
    }
}
