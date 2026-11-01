(function (root) {
  'use strict';
  function series(forecast, timeframe = '1d', visible = true) {
    const empty = { median: [], lower: [], upper: [], validated: false, hasPoints: false, title: 'Mediana modelu 1–7D',
      message: forecast?.message || 'Brak modelu trajektorii. Pobierz wieloletnią historię cen BTC.' };
    const points = forecast?.points || [];
    const origin = Date.parse(forecast?.asOfDate + 'T00:00:00Z');
    if (!visible) return { ...empty, message: 'Trajektoria ukryta.' };
    if (!['MODEL_ESTIMATE', 'NO_VALIDATION_ADVANTAGE'].includes(forecast?.status) || points.length !== 8 || !Number.isFinite(origin) ||
        !points.every((p, i) => p.horizonDays === i && Number.isFinite(Number(p.medianPrice)) &&
          Date.parse(p.time + 'T00:00:00Z') === origin + i * 86400000 &&
          Number(p.lowerPrice) > 0 && Number(p.lowerPrice) <= Number(p.medianPrice) &&
          Number(p.medianPrice) <= Number(p.upperPrice) && Number.isFinite(Number(p.upperPrice)))) return empty;
    function time(p) {
      // A daily candle dated D closes at the start of D+1 UTC. Intraday axes use that real close time.
      return timeframe === '4h' ? Date.parse(p.time + 'T00:00:00Z') / 1000 + 86400 : p.time;
    }
    const map = name => points.map(p => ({ time: time(p), value: Number(p[name]) }));
    const evaluated = forecast.validation?.find(v => v.horizonDays === 7);
    const coverage = evaluated ? ` Pokrycie walidacyjne 7D: ${(100 * evaluated.intervalCoverage).toFixed(1)}%.` : '';
    const warning = forecast.status === 'MODEL_ESTIMATE' ? '' : ' ESTYMATA EKSPERYMENTALNA — brak potwierdzonej przewagi.';
    const stale = forecast.dataAgeDays > 0 ? ` Dane starsze o ${forecast.dataAgeDays} dni.` : '';
    const change = Number(forecast.referenceClose) > 0 ? 100 * (Number(points[7].medianPrice) / Number(forecast.referenceClose) - 1) : null;
    const median = change != null ? ` Mediana 7D: ${change >= 0 ? '+' : ''}${change.toFixed(1)}%.` : '';
    return { median: map('medianPrice'), lower: map('lowerPrice'), upper: map('upperPrice'), hasPoints: true,
      validated: forecast.status === 'MODEL_ESTIMATE', title: 'Mediana modelu 1–7D',
      message: `${forecast.source} ${forecast.symbol}; ostatnie zamknięcie: ${forecast.asOfDate}.${median}${warning}${stale} Pasma 10–90% dotyczą osobnych dni, nie całej ścieżki.${coverage}` };
  }
  const api = { series };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.TrajectoryView = api;
})(globalThis);
