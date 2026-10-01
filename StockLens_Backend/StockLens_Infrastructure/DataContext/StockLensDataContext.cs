using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using System;

namespace StockLens_Infrastructure.DataContext
{
    public class StockLensDataContext : DbContext
    {
        public StockLensDataContext(DbContextOptions<StockLensDataContext> options)
            : base(options)
        {
        }

        public DbSet<Company> CompanyMaster => Set<Company>();
        public DbSet<Stock> Stocks => Set<Stock>();
        public DbSet<StockNews> StockNews => Set<StockNews>();
        public DbSet<StockShareholding> StockShareholdings => Set<StockShareholding>();
        public DbSet<StockFinancial> StockFinancials => Set<StockFinancial>();
        public DbSet<StockBalanceSheet> StockBalanceSheets => Set<StockBalanceSheet>();
        public DbSet<StockPriceHistory> StockPriceHistories => Set<StockPriceHistory>();
        public DbSet<StockDeal> StockDeals => Set<StockDeal>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Company configuration
            modelBuilder.Entity<Company>(entity =>
            {
                entity.ToTable("CompanyMaster");
                entity.HasKey(c => c.Id);

                entity.Property(c => c.CompanyName)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(c => c.Symbol)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(c => c.Industry)
                    .HasMaxLength(100);

                entity.Property(c => c.Sector)
                    .HasMaxLength(100);

                entity.Property(c => c.LogoUrl)
                    .HasMaxLength(1000);

                entity.Property(c => c.WebsiteUrl)
                    .HasMaxLength(500);

                entity.Property(c => c.About)
                    .HasColumnType("nvarchar(max)");

                entity.Property(c => c.KeyPointsJson)
                    .HasColumnType("nvarchar(max)");

                entity.Property(c => c.BseCode)
                    .HasMaxLength(50);

                entity.Property(c => c.NseCode)
                    .HasMaxLength(50);

                entity.HasIndex(c => c.Symbol)
                    .IsUnique()
                    .HasDatabaseName("IX_Company_Symbol");
            });

            // Stock configuration
            modelBuilder.Entity<Stock>(entity =>
            {
                entity.ToTable("Stocks");
                entity.HasKey(s => s.Id);

                entity.Property(s => s.Symbol)
                    .HasMaxLength(20)
                    .IsRequired();

                // entity.Property(s => s.CompanyName)
                //     .HasMaxLength(200)
                //     .IsRequired();

                entity.Property(s => s.Exchange)
                    .HasMaxLength(10)
                    .IsRequired();

                // entity.Property(s => s.Industry)
                //     .HasMaxLength(100);

                entity.HasIndex(s => new { s.Symbol, s.Exchange })
                    .IsUnique()
                    .HasDatabaseName("IX_Stocks_Symbol_Exchange");

                // Relationship: Stock -> Company
                entity.HasOne(s => s.Company)
                    .WithMany(c => c.Stocks)
                    .HasForeignKey(s => s.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Seed initial top Indian stocks for quick test/use
                //var seedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                //entity.HasData(
                //   new Stock { Id = 1, Symbol = "RELIANCE", CompanyName = "Reliance Industries Limited", Exchange = "NSE", Industry = "Oil & Gas / Conglomerate", CreatedAt = seedDate, UpdatedAt = seedDate },
                //   new Stock { Id = 2, Symbol = "TCS", CompanyName = "Tata Consultancy Services Limited", Exchange = "NSE", Industry = "Information Technology", CreatedAt = seedDate, UpdatedAt = seedDate },
                //   new Stock { Id = 3, Symbol = "INFY", CompanyName = "Infosys Limited", Exchange = "NSE", Industry = "Information Technology", CreatedAt = seedDate, UpdatedAt = seedDate },
                //   new Stock { Id = 4, Symbol = "TATAMOTORS", CompanyName = "Tata Motors Limited", Exchange = "NSE", Industry = "Automobile", CreatedAt = seedDate, UpdatedAt = seedDate },
                //   new Stock { Id = 5, Symbol = "HDFCBANK", CompanyName = "HDFC Bank Limited", Exchange = "NSE", Industry = "Banking / Financial Services", CreatedAt = seedDate, UpdatedAt = seedDate },
                //   new Stock { Id = 6, Symbol = "ICICIBANK", CompanyName = "ICICI Bank Limited", Exchange = "NSE", Industry = "Banking / Financial Services", CreatedAt = seedDate, UpdatedAt = seedDate },
                //   new Stock { Id = 7, Symbol = "RELIANCE", CompanyName = "Reliance Industries Limited", Exchange = "BSE", Industry = "Oil & Gas / Conglomerate", CreatedAt = seedDate, UpdatedAt = seedDate },
                //   new Stock { Id = 8, Symbol = "TCS", CompanyName = "Tata Consultancy Services Limited", Exchange = "BSE", Industry = "Information Technology", CreatedAt = seedDate, UpdatedAt = seedDate }
                //);
            });

            // StockNews configuration
            modelBuilder.Entity<StockNews>(entity =>
            {
                entity.ToTable("StockNews");
                entity.HasKey(sn => sn.Id);

                entity.Property(sn => sn.Title)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(sn => sn.Description)
                    .HasColumnType("nvarchar(max)");

                entity.Property(sn => sn.Content)
                    .HasColumnType("nvarchar(max)");

                entity.Property(sn => sn.SourceName)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(sn => sn.SourceUrl)
                    .HasMaxLength(1000)
                    .IsRequired();

                entity.Property(sn => sn.ImageUrl)
                    .HasMaxLength(1000);

                entity.Property(sn => sn.Category)
                    .HasMaxLength(100);

                entity.Property(sn => sn.ExternalNewsId)
                    .HasMaxLength(255);

                // Relationship
                entity.HasOne(sn => sn.Stock)
                    .WithMany(s => s.News)
                    .HasForeignKey(sn => sn.StockId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Composite index for fast sorting and retrieval by stock
                entity.HasIndex(sn => new { sn.StockId, sn.PublishedAt })
                    .HasDatabaseName("IX_StockNews_StockId_PublishedAt");

                // Index on published date
                entity.HasIndex(sn => sn.PublishedAt)
                    .HasDatabaseName("IX_StockNews_PublishedAt");

                // Unique constraint on ExternalNewsId when present
                //entity.HasIndex(sn => sn.ExternalNewsId)
                //    .IsUnique()
                //    .HasFilter("[ExternalNewsId] IS NOT NULL")
                //    .HasDatabaseName("IX_StockNews_ExternalNewsId");
                entity.HasIndex(sn => new { sn.StockId, sn.ExternalNewsId })
                    .IsUnique()
                    .HasFilter("[ExternalNewsId] IS NOT NULL")
                    .HasDatabaseName("IX_StockNews_StockId_ExternalNewsId");

                // Unique constraint on (StockId, SourceUrl) to prevent duplicate articles for the same stock
                entity.HasIndex(sn => new { sn.StockId, sn.SourceUrl })
                    .IsUnique()
                    .HasDatabaseName("IX_StockNews_StockId_SourceUrl");
            });

            // StockShareholding configuration
            modelBuilder.Entity<StockShareholding>(entity =>
            {
                entity.ToTable("StockShareholdings");
                entity.HasKey(sh => sh.Id);

                entity.Property(sh => sh.PeriodKey)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(sh => sh.Period)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(sh => sh.PeriodType)
                    .HasMaxLength(50);

                entity.Property(sh => sh.PromoterHolding)
                    .HasPrecision(6, 2)
                    .IsRequired(false);

                entity.Property(sh => sh.FiiHolding)
                    .HasPrecision(6, 2)
                    .IsRequired(false);

                entity.Property(sh => sh.DiiHolding)
                    .HasPrecision(6, 2)
                    .IsRequired(false);

                entity.Property(sh => sh.GovernmentHolding)
                    .HasPrecision(6, 2)
                    .IsRequired(false);

                entity.Property(sh => sh.PublicHolding)
                    .HasPrecision(6, 2)
                    .IsRequired(false);

                entity.Property(sh => sh.OtherHolding)
                    .HasPrecision(6, 2)
                    .IsRequired(false);

                entity.Property(sh => sh.ShareholdersCount)
                    .IsRequired(false);

                entity.Property(sh => sh.Source)
                    .HasMaxLength(100)
                    .IsRequired();

                // Relationship
                entity.HasOne(sh => sh.Stock)
                    .WithMany(s => s.Shareholdings)
                    .HasForeignKey(sh => sh.StockId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Deterministic unique constraint on (StockId, PeriodKey)
                entity.HasIndex(sh => new { sh.StockId, sh.PeriodKey })
                    .IsUnique()
                    .HasDatabaseName("IX_StockShareholdings_StockId_PeriodKey");

                // Index for chronological ordering and historical queries
                entity.HasIndex(sh => new { sh.StockId, sh.PeriodDate })
                    .HasDatabaseName("IX_StockShareholdings_StockId_PeriodDate");

                // Index for latest sync check
                entity.HasIndex(sh => new { sh.StockId, sh.LastSyncedAt })
                    .HasDatabaseName("IX_StockShareholdings_StockId_LastSyncedAt");
            });

            // StockFinancial configuration
            modelBuilder.Entity<StockFinancial>(entity =>
            {
                entity.ToTable("StockFinancials");
                entity.HasKey(f => f.Id);

                entity.Property(f => f.PeriodKey)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(f => f.PeriodType)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(f => f.FiscalYear)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(f => f.Revenue)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Expenses)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.OperatingProfit)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.OperatingProfitMargin)
                    .HasPrecision(8, 2)
                    .IsRequired(false);

                entity.Property(f => f.OtherIncome)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Interest)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Depreciation)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.ProfitBeforeTax)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Tax)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.TaxPercentage)
                    .HasPrecision(8, 2)
                    .IsRequired(false);

                entity.Property(f => f.NetProfit)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Eps)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.NetProfitAttributableToMinorityInterest)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.OtherEquity)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.OperatingCashFlow)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Capex)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.FreeCashFlow)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.NetCashFlow)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.ConsolidationType)
                    .HasMaxLength(50);

                entity.Property(f => f.Roe)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Roce)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.PeRatio)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.PegRatio)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.PbRatio)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.DividendYield)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Week52High)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Week52Low)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.CurrentPrice)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.RatiosAsOfDate)
                    .HasMaxLength(50)
                    .IsRequired(false);

                entity.Property(f => f.FaceValue)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.TotalEquity)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.BookValue)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.MarketCap)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.SectorPe)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(f => f.Source)
                    .HasMaxLength(100)
                    .IsRequired();

                // Relationship
                entity.HasOne(f => f.Stock)
                    .WithMany(s => s.Financials)
                    .HasForeignKey(f => f.StockId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Deterministic unique constraint on (StockId, PeriodKey)
                entity.HasIndex(f => new { f.StockId, f.PeriodKey })
                    .IsUnique()
                    .HasDatabaseName("IX_StockFinancials_StockId_PeriodKey");

                // Index for chronological ordering
                entity.HasIndex(f => new { f.StockId, f.PeriodEndDate })
                    .HasDatabaseName("IX_StockFinancials_StockId_PeriodEndDate");

                // Index for latest sync check
                entity.HasIndex(f => new { f.StockId, f.LastSyncedAt })
                    .HasDatabaseName("IX_StockFinancials_StockId_LastSyncedAt");
            });
            // StockPriceHistory configuration
            modelBuilder.Entity<StockPriceHistory>(entity =>
            {
                entity.ToTable("StockPriceHistories");
                entity.HasKey(p => p.Id);

                entity.Property(p => p.Open)
                    .HasPrecision(18, 4);

                entity.Property(p => p.High)
                    .HasPrecision(18, 4);

                entity.Property(p => p.Low)
                    .HasPrecision(18, 4);

                entity.Property(p => p.Close)
                    .HasPrecision(18, 4);

                entity.Property(p => p.Source)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasOne(p => p.Stock)
                    .WithMany()
                    .HasForeignKey(p => p.StockId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(p => new { p.StockId, p.Date })
                    .IsUnique()
                    .HasDatabaseName("IX_StockPriceHistories_StockId_Date");
            });

            // StockDeals configuration
            modelBuilder.Entity<StockDeal>(entity =>
            {
                entity.ToTable("StockDeals");
                entity.HasKey(d => d.Id);

                entity.Property(d => d.DealCategory).HasMaxLength(50).IsRequired();
                entity.Property(d => d.ClientName).HasMaxLength(200).IsRequired();
                entity.Property(d => d.Action).HasMaxLength(50).IsRequired();
                entity.Property(d => d.AveragePrice).HasColumnType("decimal(18,2)");
                entity.Property(d => d.TotalValue).HasColumnType("decimal(18,2)");
                entity.Property(d => d.PersonCategory).HasMaxLength(100);
                entity.Property(d => d.SharesAfterPct).HasColumnType("decimal(8,2)");
                entity.Property(d => d.Mode).HasMaxLength(100);
                entity.Property(d => d.Source).HasMaxLength(100).IsRequired();

                entity.HasOne(d => d.Stock)
                    .WithMany()
                    .HasForeignKey(d => d.StockId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(d => new { d.StockId, d.DealCategory, d.DealDate })
                    .HasDatabaseName("IX_StockDeals_StockId_Category_Date");
            });
        }
    }
}
