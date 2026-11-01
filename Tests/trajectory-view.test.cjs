const { test } = require('node:test');
const assert = require('node:assert/strict');
const { series } = require('../wwwroot/js/trajectory-view.js');
const forecast = () => ({ status: 'NO_VALIDATION_ADVANTAGE', asOfDate: '2020-01-01', source: 'UserFile', symbol: 'BTCUSDT',
  points: Array.from({ length: 8 }, (_, i) => ({ horizonDays: i, time: `2020-01-0${i+1}`, medianPrice: 100+i, lowerPrice: 99+i, upperPrice: 101+i })),
  validation: [{ horizonDays: 7, intervalCoverage: .75 }] });
test('plots learned medians, not reference prices or trade-direction drift', () => {
  const result = series(forecast());
  assert.deepEqual(result.median.map(p => p.value), [100,101,102,103,104,105,106,107]);
  assert.match(result.title, /Mediana/);
  assert.match(result.message, /EKSPERYMENTALNA/);
  assert.match(result.message, /75.0%/);
  assert.equal(result.validated, false);
});
test('clears an old curve when history is missing or overlay hidden', () => {
  assert.equal(series({ status: 'INSUFFICIENT_DATA', message: 'Need history', points: [] }).median.length, 0);
  assert.equal(series(forecast(), '1d', false).hasPoints, false);
});
test('4H axes place daily forecasts at their real UTC candle closes', () => {
  const result = series(forecast(), '4h');
  assert.equal(result.median[0].time, Date.parse('2020-01-02T00:00:00Z') / 1000);
  assert.equal(result.median[1].time - result.median[0].time, 86400);
});
test('invalid quantiles cannot create a misleading curve', () => {
  const invalid = forecast(); invalid.points[3].lowerPrice = 10000;
  assert.equal(series(invalid).hasPoints, false);
});
test('invalid date ordering cannot be rendered on the chart', () => {
  const invalid = forecast(); invalid.points[3].time = 'not-a-date';
  assert.equal(series(invalid).hasPoints, false);
});
test('distinguishes demonstrated holdout advantage from nominal coverage', () => {
  const value = forecast(); value.status = 'MODEL_ESTIMATE';
  assert.equal(series(value).validated, true);
  assert.match(series(value).message, /nie całej ścieżki/);
});
