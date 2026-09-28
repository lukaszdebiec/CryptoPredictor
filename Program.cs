using CryptoPredictor.Backtest;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;

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

builder.Services.AddSingleton<IMarketDataStorage, JsonMarketDataStorage>();
builder.Services.AddScoped<IMarketDataAggregator, MarketDataAggregator>();
builder.Services.AddSingleton<IFeatureCalculator, FeatureCalculator>();
builder.Services.AddSingleton<ITurningPointDetector, TurningPointDetector>();
builder.Services.AddSingleton<IPredictorService, PredictorService>();
builder.Services.AddSingleton<IBacktestEngine, BacktestEngine>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CryptoPredictor API v1");
        c.RoutePrefix = string.Empty; // Serwuj Swagger UI bezposrednio pod glownym adresem '/'
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
