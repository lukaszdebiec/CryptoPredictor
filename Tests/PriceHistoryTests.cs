using System.Net;
using System.Text;
using System.Text.Json;
using CryptoPredictor.Data.Ingestion;
using Xunit;

namespace CryptoPredictor.Tests;

public class PriceHistoryTests
{
    internal static readonly DateOnly Start = new(2020, 1, 1);
    internal static object[] Candle(DateOnly date, decimal close = 101m)
    {
        long open = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        return [open, "100", "102", "98", close.ToString(System.Globalization.CultureInfo.InvariantCulture), "1000", open + 86400000 - 1];
    }
    internal static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task DownloadsMoreThan1000CandlesWithOneMarketAndAdvancingCursor()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("api.binance.us", request.RequestUri!.Host);
            Assert.Contains("limit=1000", request.RequestUri.Query);
            long time = long.Parse(System.Text.RegularExpressions.Regex.Match(request.RequestUri.Query, "startTime=(\\d+)").Groups[1].Value);
            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime);
            int remaining = Start.AddDays(2001).DayNumber - date.DayNumber;
            return Json(Enumerable.Range(0, Math.Min(1000, remaining)).Select(i => Candle(date.AddDays(i))).ToArray());
        });
        var result = await new PriceHistoryService(new HttpClient(handler)).DownloadAsync(PriceHistorySource.BinanceUS, Start, Start.AddDays(2001));
        Assert.Equal(3, handler.Calls);
        Assert.Equal(2001, result.Candles.Count);
        Assert.True(result.IsComplete);
        Assert.Equal(Start.AddDays(2000), result.Candles[^1].Date);
    }

    [Fact]
    public async Task ReportsInteriorGapsWithoutInventingPrices()
    {
        var handler = new Handler(_ => Json(new[] { Candle(Start), Candle(Start.AddDays(2)) }));
        var result = await new PriceHistoryService(new HttpClient(handler)).DownloadAsync(PriceHistorySource.BinanceUS, Start, Start.AddDays(3));
        Assert.False(result.IsComplete);
        Assert.Equal(new[] { Start.AddDays(1) }, result.MissingDates);
        Assert.Equal(2, result.Candles.Count);
    }

    [Fact]
    public async Task ForbiddenSourceIsNotRetriedOrReplacedWithAnotherExchange()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new PriceHistoryService(new HttpClient(handler)).DownloadAsync(PriceHistorySource.BinanceUS, Start, Start.AddDays(1)));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RetriesTransientFailureAndHonorsLongRetryAfter()
    {
        var handler = new Handler(_ => Json(new[] { Candle(Start) }));
        handler.OverrideFirst = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        handler.OverrideFirst.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        var service = new PriceHistoryService(new HttpClient(handler));
        Assert.True((await service.DownloadAsync(PriceHistorySource.BinanceUS, Start, Start.AddDays(1))).IsComplete);
        Assert.Equal(2, handler.Calls);
        var limited = new Handler(_ => throw new InvalidOperationException("Must not retry before the server permits it."));
        limited.OverrideFirst = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.OverrideFirst.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<HttpRequestException>(() => new PriceHistoryService(new HttpClient(limited)).DownloadAsync(PriceHistorySource.BinanceUS, Start, Start.AddDays(1)));
        Assert.Equal(1, limited.Calls);
    }

    [Fact]
    public async Task ConflictingDuplicatesAndNonAdvancingPaginationFail()
    {
        var duplicate = new Handler(_ => Json(new[] { Candle(Start), Candle(Start, 100m) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PriceHistoryService(new HttpClient(duplicate)).DownloadAsync(PriceHistorySource.BinanceUS, Start, Start.AddDays(1)));
        var stuck = new Handler(_ => Json(new[] { Candle(Start.AddDays(-1)) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PriceHistoryService(new HttpClient(stuck)).DownloadAsync(PriceHistorySource.BinanceUS, Start, Start.AddDays(1)));
    }

    [Fact]
    public async Task DoesNotAcceptCurrentUtcDayOrOpenCandles()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var handler = new Handler(_ => Json(new[] { Candle(today.AddDays(-1)), Candle(today) }));
        var service = new PriceHistoryService(new HttpClient(handler));
        var result = await service.DownloadAsync(PriceHistorySource.BinanceUS, today.AddDays(-1), today);
        Assert.Single(result.Candles);
        Assert.True(result.Candles[0].Date < today);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DownloadAsync(PriceHistorySource.BinanceUS, today, today.AddDays(1)));
    }

    internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpResponseMessage? OverrideFirst { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(Calls == 1 && OverrideFirst != null ? OverrideFirst : action(request)); }
    }
}
