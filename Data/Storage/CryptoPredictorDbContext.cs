using CryptoPredictor.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoPredictor.Data.Storage;

public class CryptoPredictorDbContext : DbContext
{
    public DbSet<DailyMarketData> MarketData => Set<DailyMarketData>();
    public DbSet<MarketPredictionRecord> Predictions => Set<MarketPredictionRecord>();

    public CryptoPredictorDbContext(DbContextOptions<CryptoPredictorDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DailyMarketData>(entity =>
        {
            entity.ToTable("DailyMarketData");
            entity.HasKey(e => e.Date);
            entity.Property(e => e.Date).IsRequired();
        });

        modelBuilder.Entity<MarketPredictionRecord>(entity =>
        {
            entity.ToTable("MarketPredictions");
            entity.HasKey(e => e.Date);
            entity.Property(e => e.Date).IsRequired();
        });
    }

}
