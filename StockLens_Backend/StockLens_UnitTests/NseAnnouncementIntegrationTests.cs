using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.MapperProfile;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.Nse;
using StockLens_Infrastructure.Repositories;
using System;
using System.Threading.Tasks;
using Xunit;

namespace StockLens_UnitTests
{
    public class NseAnnouncementIntegrationTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task Live_FetchFromNseAndPersistToDb()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<StockLensDataContext>(opt =>
                opt.UseSqlServer("Server=DESKTOP-J2CVHIQ;Database=StockLensTestDB;Trusted_Connection=True;Encrypt=False;"));
            services.AddScoped<IStockAnnouncementRepository, StockAnnouncementRepository>();
            services.AddSingleton<INseAnnouncementClient, NseAnnouncementClient>();
            services.AddAutoMapper(cfg => cfg.AddProfile<MapperProfile>());
            services.AddScoped<IStockAnnouncementService, StockAnnouncementService>();

            var sp = services.BuildServiceProvider();
            var service = sp.GetRequiredService<IStockAnnouncementService>();

            // Sync from NSE
            var newCount = await service.SyncLatestAnnouncementsAsync();
            Console.WriteLine($"Persisted {newCount} new records into DB.");

            // Read from DB
            var response = await service.GetAnnouncementsAsync(limit: 10);
            Console.WriteLine($"DB TotalCount: {response.TotalCount}, Items in response: {response.Announcements.Count}");
            Assert.True(response.TotalCount > 0);
        }
    }
}
