const { test } = require('node:test');
const assert = require('node:assert/strict');
const { series } = require('../wwwroot/js/confluence-view.js');

function makeConfluence(horizonDays = 7, bias = 'BULLISH', withCycle = false) {
  const origin = Date.parse('2026-06-01T00:00:00Z');
  const points = Array.from({ length: horizonDays + 1 }, (_, i) => {
    const iso = new Date(origin + i * 86400000).toISOString().slice(0, 10);
    return {
      horizonDays: i,
      time: iso,
      projectedPrice: 70000 + i * 250,
      lowerBandPrice: 70000 + i * 250 - i * 400,
      upperBandPrice: 70000 + i * 250 + i * 400,
      projectedReturnPercent: Number(((i * 250) / 700).toFixed(2))
    };
  });

  const cycleAnalog = withCycle
    ? {
        available: true,
        cyclePhaseLabel: 'Rok Wyborów Połówkowych USA (Midterms / +2 po Halvingu)',
        analogYears: [2020, 2021, 2022, 2023, 2024, 2025],
        matchedCycleYears: [2022],
        expectedReturnPercent: -3.4,
        message: 'Analog historyczny 30D',
        points: points.map(p => ({
          horizonDays: p.horizonDays,
          time: p.time,
          analogPrice: 70000 - p.horizonDays * 80,
          analogReturnPercent: -0.1 * p.horizonDays
        }))
      }
    : {
        available: false,
        cyclePhaseLabel: 'Rok Wyborów Połówkowych USA',
        analogYears: [],
        matchedCycleYears: [],
        expectedReturnPercent: 0,
        message: 'Brak 5-letniej historii',
        points: []
      };

  return {
    status: 'READY',
    asOfDate: '2026-06-01',
    horizonDays,
    referenceClose: 70000,
    confluenceScore: bias === 'BULLISH' ? 42.5 : -38.0,
    bias,
    biasLabel: bias === 'BULLISH' ? 'SILNA KONFLUENCJA BYCZA' : 'SILNA KONFLUENCJA NIEDŹWIEDZIA',
    message: `Konfluencja ${horizonDays}D gotowa`,
    points,
    cycleAnalog
  };
}

test('renders 7D confluence curve with bias color and no cycle analog line', () => {
  const chartData = {
    confluence7d: makeConfluence(7, 'BULLISH', true),
    confluence30d: makeConfluence(30, 'BEARISH', true)
  };
  const res = series(chartData, 'confluence7d', '1d', true);
  assert.equal(res.hasPoints, true);
  assert.equal(res.center.length, 8);
  assert.equal(res.color, '#10b981');
  assert.equal(res.hasCycleAnalog, false);
  assert.equal(res.cycleAnalog.length, 0);
});

test('renders 30D confluence curve together with historical cycle analog path', () => {
  const chartData = {
    confluence7d: makeConfluence(7, 'BULLISH', false),
    confluence30d: makeConfluence(30, 'BEARISH', true)
  };
  const res = series(chartData, 'confluence30d', '1d', true);
  assert.equal(res.hasPoints, true);
  assert.equal(res.center.length, 31);
  assert.equal(res.color, '#f43f5e');
  assert.equal(res.hasCycleAnalog, true);
  assert.equal(res.cycleAnalog.length, 31);
  assert.match(res.message, /Analog cyklu/);
});

test('4H timeframe aligns both confluence points and cycle analog to daily UTC closes', () => {
  const chartData = { confluence30d: makeConfluence(30, 'BULLISH', true) };
  const res = series(chartData, 'confluence30d', '4h', true);
  assert.equal(res.center[0].time, Date.parse('2026-06-02T00:00:00Z') / 1000);
  assert.equal(res.cycleAnalog[1].time - res.cycleAnalog[0].time, 86400);
});

test('clears all curves including cycle analog when hidden or invalid', () => {
  const chartData = { confluence30d: makeConfluence(30, 'BULLISH', true) };
  const hidden = series(chartData, 'confluence30d', '1d', false);
  assert.equal(hidden.hasPoints, false);
  assert.equal(hidden.center.length, 0);
  assert.equal(hidden.cycleAnalog.length, 0);
  assert.ok(hidden.forecast); // Keeps forecast payload so pillar breakdown panel still renders

  const broken = makeConfluence(7, 'BULLISH', false);
  broken.points[2].lowerBandPrice = 999999;
  assert.equal(series({ confluence7d: broken }, 'confluence7d', '1d', true).hasPoints, false);
});

