(function (root) {
  'use strict';
  const resolvedStatuses = new Set(['TARGET_HIT', 'INVALIDATED', 'EXPIRED_PROFIT', 'EXPIRED_LOSS']);
  function select(items, threshold = 0.5, execution = 'rolling') {
    const candidates = items.filter(it => it.signal !== 'NEUTRAL_HOLD' &&
      it.outcomeStatus !== 'DATA_UNAVAILABLE' &&
      (Number(it.bullishProbability) >= threshold || Number(it.bullishProbability) <= 1 - threshold));
    if (execution !== 'discrete') return candidates;
    const chosen = [];
    let blockedUntil = '';
    for (const item of [...candidates].sort((a, b) => a.date.localeCompare(b.date))) {
      if (item.date < blockedUntil) continue;
      chosen.push(item);
      // A pending position stays open; elapsedDays is its age, not its eventual holding time.
      const expiry = new Date(item.date + 'T00:00:00Z');
      expiry.setUTCDate(expiry.getUTCDate() + (item.horizonDays || 7));
      blockedUntil = item.resolvedDate || expiry.toISOString().slice(0, 10);
    }
    return chosen;
  }
  function statistics(items) {
    const resolved = items.filter(it => resolvedStatuses.has(it.outcomeStatus));
    const returns = resolved.map(it => Number(it.realizedReturnPercent));
    const gains = returns.reduce((s, r) => s + Math.max(0, r), 0);
    const losses = -returns.reduce((s, r) => s + Math.min(0, r), 0);
    return {
      resolvedPredictions: resolved.length,
      pendingPredictions: items.filter(it => it.outcomeStatus === 'PENDING').length,
      winRatePercent: resolved.length ? 100 * returns.filter(r => r > 0).length / resolved.length : 0,
      targetHitRatePercent: resolved.length ? 100 * resolved.filter(it => it.outcomeStatus === 'TARGET_HIT').length / resolved.length : 0,
      profitFactor: losses > 0 ? gains / losses : null,
      cumulativeReturnPercent: 100 * returns.reduce((s, r) => s + r, 0)
    };
  }
  const api = { select, statistics };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.CryptoJournal = api;
})(globalThis);
