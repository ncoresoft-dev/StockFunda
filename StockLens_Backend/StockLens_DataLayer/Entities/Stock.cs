using System;
using System.Collections.Generic;

namespace StockLens_DataLayer.Entities
{
    public class Stock
    {
        public int Id { get; set; }
        
        // Foreign Key
        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Symbol { get; set; } = string.Empty;
        public string Exchange { get; set; } = "NSE"; // NSE or BSE
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
       
        public ICollection<StockNews> News { get; set; } = new List<StockNews>();
        public ICollection<StockShareholding> Shareholdings { get; set; } = new List<StockShareholding>();
        public ICollection<StockFinancial> Financials { get; set; } = new List<StockFinancial>();
        public ICollection<StockPeer> Peers { get; set; } = new List<StockPeer>();
    }
}
