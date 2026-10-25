using CryptoPredictor.Alerts;
using CryptoPredictor.Backtest;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Intraday;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Features;
using CryptoPredictor.Features.Intraday;
using CryptoPredictor.Prediction;
using CryptoPredictor.Prediction.Intraday;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Data Ingestion & Storage services
builder.Services.AddHttpClient<IBinanceDataService, BinanceDataService>();
builder.Services.AddHttpClient<ISentimentDataService, SentimentDataService>();
builder.Services.AddHttpClient<ICoinbaseDataService, CoinbaseDataService>();
builder.Services.AddHttpClient<IDeribitDataService, DeribitDataService>();
builder.Services.AddHttpClient<IEtfDataService, FarsideEtfService>();
builder.Services.AddHttpClient<IMacroDataService, MacroDataService>();
builder.Services.AddHttpClient<ITelegramAlertService, TelegramAlertService>();
builder.Services.AddHttpClient<IIntradayMarketDataService, IntradayMarketDataService>();

// SQLite Database configuration
var storageDir = Path.Combine(builder.Environment.ContentRootPath, "Data", "storage");
if (!Directory.Exists(storageDir))
{
    Directory.CreateDirectory(storageDir);
}
var dbPath = Path.Combine(storageDir, "market_data.db");
builder.Services.AddDbContextFactory<CryptoPredictorDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});

builder.Services.AddSingleton<IMarketDataStorage, SqliteMarketDataStorage>();
builder.Services.AddScoped<IMarketDataAggregator, MarketDataAggregator>();
builder.Services.AddSingleton<ILiquidityEngine, LiquidityEngine>();
builder.Services.AddSingleton<IFeatureCalculator, FeatureCalculator>();
builder.Services.AddSingleton<ITurningPointDetector, TurningPointDetector>();
builder.Services.AddSingleton<IPredictorService, PredictorService>();
builder.Services.AddSingleton<IBacktestEngine, BacktestEngine>();
builder.Services.AddSingleton<IIntradayFeatureCalculator, IntradayFeatureCalculator>();
builder.Services.AddSingleton<IIntradayPredictorService, IntradayPredictorService>();

// Background Worker for automated daily 00:05 UTC cycle
builder.Services.AddHostedService<CryptoPredictor.Background.DailyMarketDataWorker>();

var app = builder.Build();


// Ensure SQLite database is created and auto-migrated from legacy JSON if available
using (var scope = app.Services.CreateScope())
{
    var storage = scope.ServiceProvider.GetRequiredService<IMarketDataStorage>();
    await storage.LoadRecentAsync(1);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CryptoPredictor API v1");
        c.RoutePrefix = "swagger"; // Dostępny pod adresem /swagger
    });
}

app.UseHttpsRedirection();

// Enable serving lightweight frontend dashboard from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();

