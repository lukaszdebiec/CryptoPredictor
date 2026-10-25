const { test } = require('node:test');
const assert = require('node:assert/strict');
const { select, statistics } = require('../wwwroot/js/journal-math.js');
const trade = (date, resolvedDate, outcomeStatus = 'TARGET_HIT') => ({ date, resolvedDate, outcomeStatus,
  signal: 'BULLISH_OPPORTUNITY', bullishProbability: .7, horizonDays: 7, elapsedDays: 7, realizedReturnPercent: .05 });
test('discrete selection uses exit date rather than signal age', () => {
  const selected = select([trade('2020-01-01', '2020-01-02'), trade('2020-01-02', '2020-01-04'), trade('2020-01-03', '2020-01-05')], .6, 'discrete');
  assert.deepEqual(selected.map(t => t.date), ['2020-01-01', '2020-01-02']);
});
test('a pending trade blocks overlapping entries', () => {
  const selected = select([trade('2020-01-01', null, 'PENDING'), trade('2020-01-02', '2020-01-03')], .6, 'discrete');
  assert.equal(selected.length, 1);
});
test('neutral holds and unavailable outcomes never count as wins', () => {
  const selected = select([{ ...trade('2020-01-01'), signal: 'NEUTRAL_HOLD', outcomeStatus: 'NO_TRADE' },
    trade('2020-01-02', null, 'DATA_UNAVAILABLE'), trade('2020-01-03', '2020-01-04')]);
  assert.equal(selected.length, 1);
  assert.equal(statistics(selected).resolvedPredictions, 1);
  assert.equal(statistics(selected).profitFactor, null);
});
test('net losses are counted even when a target was hit', () => {
  const result = statistics([{ ...trade('2020-01-01', '2020-01-02'), realizedReturnPercent: -.001 }]);
  assert.equal(result.winRatePercent, 0);
  assert.equal(result.profitFactor, 0);
});
