(function (root) {
  'use strict';

  const TrajectoryHelper = root.TrajectoryView ||
    (typeof require === 'function' ? require('./trajectory-view.js') : null);

  function mapAxisTime(dateStr, timeframe) {
    // A daily candle dated D closes at the start of D+1 UTC. Intraday 4H axes use that exact UTC close timestamp.
    return timeframe === '4h' ? Date.parse(dateStr + 'T00:00:00Z') / 1000 + 86400 : dateStr;
  }

  function biasColor(bias) {
    if (bias === 'BULLISH') return '#10b981';
    if (bias === 'BEARISH') return '#f43f5e';
    return '#38bdf8';
  }

  function series(chartData, mode = 'confluence7d', timeframe = '1d', visible = true) {
    if (mode === 'median7d') {
      const traj = TrajectoryHelper
        ? TrajectoryHelper.series(chartData?.trajectory, timeframe, visible)
        : { median: [], lower: [], upper: [], validated: false, hasPoints: false, title: 'Mediana modelu 1–7D', message: 'Brak modułu trajektorii.' };

      return {
        mode,
        center: traj.median,
        lower: traj.lower,
        upper: traj.upper,
        cycleAnalog: [],
        hasPoints: traj.hasPoints,
        hasCycleAnalog: false,
        color: traj.validated ? '#38bdf8' : '#f59e0b',
        solidCenter: Boolean(traj.validated),
        title: traj.title,
        upperTitle: 'Q90 (dzień)',
        lowerTitle: 'Q10 (dzień)',
        cycleTitle: '',
        message: traj.message,
        forecast: chartData?.confluence7d || null
      };
    }

    const forecast = mode === 'confluence30d' ? chartData?.confluence30d : chartData?.confluence7d;
    const horizonLabel = mode === 'confluence30d' ? '30D' : '7D';
    const empty = {
      mode,
      center: [],
      lower: [],
      upper: [],
      cycleAnalog: [],
      hasPoints: false,
      hasCycleAnalog: false,
      color: '#38bdf8',
      solidCenter: true,
      title: `Konfluencja ${horizonLabel}`,
      upperTitle: `Pasmo +80% (${horizonLabel})`,
      lowerTitle: `Pasmo -80% (${horizonLabel})`,
      cycleTitle: 'Analog Cyklu (30D)',
      message: forecast?.message || `Brak danych wskaźnika konfluencji ${horizonLabel}.`,
      forecast: forecast || null
    };

    if (!visible) {
      return { ...empty, message: 'Projekcja na wykresie ukryta.' };
    }

    const points = forecast?.points || [];
    const expectedLen = Number(forecast?.horizonDays) + 1;
    const origin = Date.parse(forecast?.asOfDate + 'T00:00:00Z');

    if (
      forecast?.status !== 'READY' ||
      !Number.isFinite(origin) ||
      !Number.isFinite(expectedLen) ||
      expectedLen < 2 ||
      points.length !== expectedLen ||
      !points.every((p, i) =>
        p.horizonDays === i &&
        Date.parse(p.time + 'T00:00:00Z') === origin + i * 86400000 &&
        Number(p.lowerBandPrice) > 0 &&
        Number(p.lowerBandPrice) <= Number(p.projectedPrice) &&
        Number(p.projectedPrice) <= Number(p.upperBandPrice) &&
        Number.isFinite(Number(p.upperBandPrice))
      )
    ) {
      return empty;
    }

    const center = points.map(p => ({ time: mapAxisTime(p.time, timeframe), value: Number(p.projectedPrice) }));
    const lower = points.map(p => ({ time: mapAxisTime(p.time, timeframe), value: Number(p.lowerBandPrice) }));
    const upper = points.map(p => ({ time: mapAxisTime(p.time, timeframe), value: Number(p.upperBandPrice) }));

    let cycleAnalog = [];
    let hasCycleAnalog = false;
    let cycleSuffix = '';

    if (mode === 'confluence30d' && forecast.cycleAnalog) {
      const analogPts = forecast.cycleAnalog.points || [];
      if (
        forecast.cycleAnalog.available &&
        analogPts.length === expectedLen &&
        analogPts.every((p, i) =>
          p.horizonDays === i &&
          Date.parse(p.time + 'T00:00:00Z') === origin + i * 86400000 &&
          Number(p.analogPrice) > 0 &&
          Number.isFinite(Number(p.analogPrice))
        )
      ) {
        cycleAnalog = analogPts.map(p => ({ time: mapAxisTime(p.time, timeframe), value: Number(p.analogPrice) }));
        hasCycleAnalog = true;
        const ret = Number(forecast.cycleAnalog.expectedReturnPercent) || 0;
        const sign = ret >= 0 ? '+' : '';
        cycleSuffix = ` · Analog cyklu (${(forecast.cycleAnalog.analogYears || []).join(', ')}): ${sign}${ret.toFixed(1)}%`;
      } else if (forecast.cycleAnalog.message) {
        cycleSuffix = ` · ${forecast.cycleAnalog.message}`;
      }
    }

    return {
      mode,
      center,
      lower,
      upper,
      cycleAnalog,
      hasPoints: true,
      hasCycleAnalog,
      color: biasColor(forecast.bias),
      solidCenter: true,
      title: `Konfluencja ${horizonLabel}`,
      upperTitle: `Pasmo +80% (${horizonLabel})`,
      lowerTitle: `Pasmo -80% (${horizonLabel})`,
      cycleTitle: 'Analog Cyklu (30D)',
      message: `${forecast.message}${cycleSuffix}`,
      forecast
    };
  }

  const api = { series, biasColor, mapAxisTime };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ConfluenceView = api;
})(globalThis);

