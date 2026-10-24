// CryptoPredictor Dashboard Client Application
// TradingView Lightweight Charts v5.2.1 Integration

(function () {
  'use strict';

  // State
  let currentDays = 180;
  let activeSubTab = 'funding'; // 'funding' | 'etf' | 'fng'
  let cachedChartData = null;

  // Chart instances
  let mainChart = null;
  let subChart = null;
  let candlestickSeries = null;
  let volumeSeries = null;
  let markersPrimitive = null;

  // Subchart series
  let fundingSeries = null;
  let waveSeries = null;
  let etfSeries = null;
  let fngSeries = null;
  let macroDxySeries = null;
  let macroSpSeries = null;
  let activeFibLines = [];

  // Color constants
  const COLORS = {
    bg: '#070a13',
    cardBg: '#0d1322',
    border: 'rgba(255, 255, 255, 0.08)',
    grid: 'rgba(255, 255, 255, 0.03)',
    textMuted: '#94a3b8',
    textDim: '#64748b',
    bullish: '#10b981',
    bearish: '#f43f5e',
    cyan: '#38bdf8',
    indigo: '#6366f1',
    amber: '#f59e0b'
  };

  // Toast Notification System
  function showToast(message, type = 'info') {
    const container = document.getElementById('toast-container');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    
    let icon = 'ℹ️';
    if (type === 'success') icon = '✅';
    if (type === 'error') icon = '⚠️';

    toast.innerHTML = `<span>${icon}</span><span>${escapeHtml(message)}</span>`;
    container.appendChild(toast);

    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(100%)';
      toast.style.transition = 'all 0.3s ease';
      setTimeout(() => toast.remove(), 300);
    }, 4500);
  }

  function escapeHtml(text) {
    if (!text) return '';
    return String(text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
  }

  function formatMoney(num) {
    if (num == null || isNaN(num)) return '$--';
    return '$' + Number(num).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  function formatMillions(num) {
    if (num == null || isNaN(num)) return '$--M';
    const val = Number(num) / 1_000_000;
    const sign = val > 0 ? '+' : '';
    return `${sign}$${val.toFixed(1)}M`;
  }

  // Initialize TradingView Lightweight Charts
  function initCharts() {
    const mainContainer = document.getElementById('main-chart');
    const subContainer = document.getElementById('sub-chart');
    if (!mainContainer || !subContainer) return;

    // Common Chart Options
    const commonOptions = {
      layout: {
        background: { type: LightweightCharts.ColorType.Solid, color: 'transparent' },
        textColor: COLORS.textMuted,
        fontFamily: "'JetBrains Mono', monospace",
        fontSize: 11
      },
      grid: {
        vertLines: { color: COLORS.grid },
        horzLines: { color: COLORS.grid }
      },
      crosshair: {
        mode: LightweightCharts.CrosshairMode.Normal,
        vertLine: { color: 'rgba(255, 255, 255, 0.2)', width: 1, style: LightweightCharts.LineStyle.Dashed },
        horzLine: { color: 'rgba(255, 255, 255, 0.2)', width: 1, style: LightweightCharts.LineStyle.Dashed }
      },
      rightPriceScale: {
        borderColor: COLORS.border,
        autoScale: true
      },
      timeScale: {
        borderColor: COLORS.border,
        timeVisible: true,
        secondsVisible: false
      }
    };

    // 1. Main Candlestick Chart
    mainChart = LightweightCharts.createChart(mainContainer, {
      ...commonOptions,
      height: 480
    });

    candlestickSeries = mainChart.addSeries(LightweightCharts.CandlestickSeries, {
      upColor: COLORS.bullish,
      downColor: COLORS.bearish,
      borderVisible: false,
      wickUpColor: COLORS.bullish,
      wickDownColor: COLORS.bearish
    });

    // Volume overlay
    volumeSeries = mainChart.addSeries(LightweightCharts.HistogramSeries, {
      priceFormat: { type: 'volume' },
      priceScaleId: '', // Overlay on price pane with custom margins
      scaleMargins: { top: 0.82, bottom: 0 }
    });

    markersPrimitive = LightweightCharts.createSeriesMarkers(candlestickSeries, []);

    // 2. Subchart (Microstructure & Indicators)
    subChart = LightweightCharts.createChart(subContainer, {
      ...commonOptions,
      height: 160
    });

    // Timeframe synchronization between mainChart and subChart
    let isSyncing = false;
    mainChart.timeScale().subscribeVisibleLogicalRangeChange(range => {
      if (isSyncing || !range) return;
      isSyncing = true;
      subChart.timeScale().setVisibleLogicalRange(range);
      isSyncing = false;
    });

    subChart.timeScale().subscribeVisibleLogicalRangeChange(range => {
      if (isSyncing || !range) return;
      isSyncing = true;
      mainChart.timeScale().setVisibleLogicalRange(range);
      isSyncing = false;
    });

    // Crosshair Hover Legend
    mainChart.subscribeCrosshairMove(param => {
      updateLegend(param);
    });

    // Responsive Auto-resize
    const resizeObserver = new ResizeObserver(entries => {
      for (const entry of entries) {
        if (entry.target === mainContainer && mainChart) {
          mainChart.resize(mainContainer.clientWidth, 480);
        } else if (entry.target === subContainer && subChart) {
          subChart.resize(subContainer.clientWidth, 160);
        }
      }
    });

    resizeObserver.observe(mainContainer);
    resizeObserver.observe(subContainer);
  }

  // Update hover legend
  function updateLegend(param) {
    const legDate = document.getElementById('leg-date');
    const legOpen = document.getElementById('leg-open');
    const legHigh = document.getElementById('leg-high');
    const legLow = document.getElementById('leg-low');
    const legClose = document.getElementById('leg-close');
    const legVol = document.getElementById('leg-vol');

    if (!param || !param.time || !param.seriesData.get(candlestickSeries)) {
      if (cachedChartData && cachedChartData.candles.length > 0) {
        const last = cachedChartData.candles[cachedChartData.candles.length - 1];
        setLegendValues(last.time, last.open, last.high, last.low, last.close, last.volume);
      }
      return;
    }

    const candle = param.seriesData.get(candlestickSeries);
    const vol = param.seriesData.get(volumeSeries);
    setLegendValues(param.time, candle.open, candle.high, candle.low, candle.close, vol ? vol.value : 0);
  }

  function setLegendValues(time, o, h, l, c, v) {
    document.getElementById('leg-date').innerText = time || '--';
    document.getElementById('leg-open').innerText = o ? formatMoney(o) : '--';
    document.getElementById('leg-high').innerText = h ? formatMoney(h) : '--';
    document.getElementById('leg-low').innerText = l ? formatMoney(l) : '--';
    document.getElementById('leg-close').innerText = c ? formatMoney(c) : '--';
    document.getElementById('leg-vol').innerText = v ? (Number(v) / 1000).toFixed(1) + 'k BTC' : '--';

    const closeElem = document.getElementById('leg-close');
    if (c >= o) {
      closeElem.style.color = COLORS.bullish;
    } else {
      closeElem.style.color = COLORS.bearish;
    }
  }

  // Fetch Chart Data from API
  async function loadChartData(days = currentDays) {
    try {
      const url = days > 0 ? `/api/marketdata/chart?days=${days}` : `/api/marketdata/chart`;
      const res = await fetch(url);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      const data = await res.json();
      cachedChartData = data;

      // Map candles to LightweightCharts format
      const candleData = data.candles.map(c => ({
        time: c.time,
        open: Number(c.open),
        high: Number(c.high),
        low: Number(c.low),
        close: Number(c.close)
      }));

      candlestickSeries.setData(candleData);

      // Map volume data
      const volData = data.candles.map(c => ({
        time: c.time,
        value: Number(c.volume),
        color: Number(c.close) >= Number(c.open) ? 'rgba(16, 185, 129, 0.2)' : 'rgba(244, 63, 94, 0.2)'
      }));
      volumeSeries.setData(volData);

      // Set markers (local bottoms and tops)
      if (markersPrimitive && data.markers) {
        markersPrimitive.setMarkers(data.markers);
      }

      // Update Fibonacci levels & Golden Pocket on price chart
      if (activeFibLines && activeFibLines.length > 0) {
        for (const line of activeFibLines) {
          candlestickSeries.removePriceLine(line);
        }
        activeFibLines = [];
      }

      if (data.fibonacci) {
        const fib = data.fibonacci;

        // Golden Pocket levels (0.618 - 0.650)
        activeFibLines.push(candlestickSeries.createPriceLine({
          price: Number(fib.level618),
          color: '#f59e0b',
          lineWidth: 2,
          lineStyle: LightweightCharts.LineStyle.Solid,
          axisLabelVisible: true,
          title: `Fib 0.618 Pocket ($${Number(fib.level618).toLocaleString('en-US')})`
        }));

        activeFibLines.push(candlestickSeries.createPriceLine({
          price: Number(fib.level650),
          color: '#d97706',
          lineWidth: 2,
          lineStyle: LightweightCharts.LineStyle.Solid,
          axisLabelVisible: true,
          title: `Fib 0.650 Pocket ($${Number(fib.level650).toLocaleString('en-US')})`
        }));

        activeFibLines.push(candlestickSeries.createPriceLine({
          price: Number(fib.level500),
          color: 'rgba(148, 163, 184, 0.45)',
          lineWidth: 1,
          lineStyle: LightweightCharts.LineStyle.Dashed,
          axisLabelVisible: true,
          title: `Fib 0.500 ($${Number(fib.level500).toLocaleString('en-US')})`
        }));

        activeFibLines.push(candlestickSeries.createPriceLine({
          price: Number(fib.level382),
          color: 'rgba(56, 189, 248, 0.4)',
          lineWidth: 1,
          lineStyle: LightweightCharts.LineStyle.Dotted,
          axisLabelVisible: true,
          title: `Fib 0.382 ($${Number(fib.level382).toLocaleString('en-US')})`
        }));

        activeFibLines.push(candlestickSeries.createPriceLine({
          price: Number(fib.level786),
          color: 'rgba(244, 63, 94, 0.4)',
          lineWidth: 1,
          lineStyle: LightweightCharts.LineStyle.Dotted,
          axisLabelVisible: true,
          title: `Fib 0.786 ($${Number(fib.level786).toLocaleString('en-US')})`
        }));

        // Update Fibonacci & Wave Stat Cards
        const fibRatioElem = document.getElementById('stat-fib-ratio');
        const fibDescElem = document.getElementById('stat-fib-desc');
        if (fibRatioElem) {
          const ratioPercent = (Number(fib.currentRatio) * 100).toFixed(1);
          fibRatioElem.innerText = `${ratioPercent}%`;
          if (fib.inGoldenPocket) {
            fibRatioElem.className = 'stat-value text-amber';
            if (fibDescElem) fibDescElem.innerHTML = `<span class="badge-golden-pocket">GOLDEN POCKET 🎯</span>`;
          } else if (fib.currentRatio >= 0.786) {
            fibRatioElem.className = 'stat-value text-bearish';
            if (fibDescElem) fibDescElem.innerText = `Deep retracement (>0.786)`;
          } else if (fib.currentRatio <= 0.382) {
            fibRatioElem.className = 'stat-value text-bullish';
            if (fibDescElem) fibDescElem.innerText = `Shallow retracement (<0.382)`;
          } else {
            fibRatioElem.className = 'stat-value';
            if (fibDescElem) fibDescElem.innerText = `Pocket: $${Number(fib.level650).toLocaleString('en-US', {maximumFractionDigits:0})} - $${Number(fib.level618).toLocaleString('en-US', {maximumFractionDigits:0})}`;
          }
        }

        const kerElem = document.getElementById('stat-ker-val');
        const kerDescElem = document.getElementById('stat-ker-desc');
        if (kerElem) {
          const kerVal = Number(fib.kaufmanEfficiencyRatio);
          kerElem.innerText = kerVal.toFixed(2);
          if (kerVal > 0.55) {
            kerElem.className = 'stat-value text-bullish';
            if (kerDescElem) kerDescElem.innerHTML = `<span class="text-bullish">Impulse Wave (3/5)</span>`;
          } else if (kerVal < 0.25) {
            kerElem.className = 'stat-value text-amber';
            if (kerDescElem) kerDescElem.innerHTML = `<span class="text-amber">ABC Correction / Chop</span>`;
          } else {
            kerElem.className = 'stat-value';
            if (kerDescElem) kerDescElem.innerText = `Transitory Regime`;
          }
        }
      }

      // Update Subchart
      renderSubchart();

      // Fit content
      mainChart.timeScale().fitContent();

      // Update Summary stat cards
      if (data.summary) {
        const priceElem = document.getElementById('stat-btc-price');
        const changeElem = document.getElementById('stat-btc-change');
        if (priceElem) priceElem.innerText = formatMoney(data.summary.latestClose);

        if (changeElem) {
          const chg = data.summary.priceChange24hPercent;
          const sign = chg > 0 ? '+' : '';
          const isUp = chg >= 0;
          changeElem.innerHTML = `<span class="${isUp ? 'text-bullish' : 'text-bearish'}">${sign}${chg}% (24h)</span>`;
        }

        // Default legend to latest candle
        if (data.candles.length > 0) {
          const latest = data.candles[data.candles.length - 1];
          setLegendValues(latest.time, latest.open, latest.high, latest.low, latest.close, latest.volume);
        }
      }

    } catch (err) {
      console.error('Failed to load chart data:', err);
      showToast('Failed to load chart data: ' + err.message, 'error');
    }
  }

  // Render Subchart according to active tab
  function renderSubchart() {
    if (!subChart || !cachedChartData || !cachedChartData.indicators) return;

    // Remove previous series if any
    if (fundingSeries) { subChart.removeSeries(fundingSeries); fundingSeries = null; }
    if (waveSeries) { subChart.removeSeries(waveSeries); waveSeries = null; }
    if (etfSeries) { subChart.removeSeries(etfSeries); etfSeries = null; }
    if (fngSeries) { subChart.removeSeries(fngSeries); fngSeries = null; }
    if (macroDxySeries) { subChart.removeSeries(macroDxySeries); macroDxySeries = null; }
    if (macroSpSeries) { subChart.removeSeries(macroSpSeries); macroSpSeries = null; }

    // Toggle dual price scale visibility (left scale for S&P 500 equities, right scale for DXY)
    if (activeSubTab === 'macro') {
      subChart.priceScale('left').applyOptions({ visible: true });
    } else {
      subChart.priceScale('left').applyOptions({ visible: false });
    }

    const indicators = cachedChartData.indicators;

    if (activeSubTab === 'funding') {
      fundingSeries = subChart.addSeries(LightweightCharts.LineSeries, {
        color: COLORS.cyan,
        lineWidth: 2,
        title: 'Funding Z-Score (30d)'
      });

      const seriesData = indicators
        .filter(i => i.fundingZScore30d != null)
        .map(i => ({ time: i.time, value: Number(i.fundingZScore30d) }));

      fundingSeries.setData(seriesData);

      // Add baseline zero and overbought/oversold levels
      fundingSeries.createPriceLine({
        price: 0,
        color: 'rgba(255, 255, 255, 0.25)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dotted,
        title: 'Neutral'
      });
      fundingSeries.createPriceLine({
        price: 2.0,
        color: 'rgba(244, 63, 94, 0.65)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dashed,
        title: 'Overheated (+2.0)'
      });
      fundingSeries.createPriceLine({
        price: -2.0,
        color: 'rgba(16, 185, 129, 0.65)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dashed,
        title: 'Oversold (-2.0)'
      });

    } else if (activeSubTab === 'wave') {
      waveSeries = subChart.addSeries(LightweightCharts.AreaSeries, {
        topColor: 'rgba(245, 158, 11, 0.35)',
        bottomColor: 'rgba(245, 158, 11, 0.02)',
        lineColor: '#f59e0b',
        lineWidth: 2,
        title: 'Kaufman Wave Efficiency (KER 10d)'
      });

      const seriesData = indicators
        .filter(i => i.kaufmanEfficiencyRatio10d != null)
        .map(i => ({
          time: i.time,
          value: Number(i.kaufmanEfficiencyRatio10d)
        }));

      waveSeries.setData(seriesData);

      waveSeries.createPriceLine({
        price: 0.55,
        color: 'rgba(16, 185, 129, 0.75)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dashed,
        title: 'Impulse Momentum (>0.55)'
      });

      waveSeries.createPriceLine({
        price: 0.25,
        color: 'rgba(244, 63, 94, 0.75)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dashed,
        title: 'Corrective Chop (<0.25)'
      });

    } else if (activeSubTab === 'etf') {
      etfSeries = subChart.addSeries(LightweightCharts.HistogramSeries, {
        title: 'ETF Net Inflow ($USD)'
      });

      const seriesData = indicators
        .filter(i => i.etfFlowDailyUsd != null)
        .map(i => {
          const val = Number(i.etfFlowDailyUsd);
          return {
            time: i.time,
            value: val,
            color: val >= 0 ? 'rgba(16, 185, 129, 0.85)' : 'rgba(244, 63, 94, 0.85)'
          };
        });

      etfSeries.setData(seriesData);

    } else if (activeSubTab === 'fng') {
      fngSeries = subChart.addSeries(LightweightCharts.AreaSeries, {
        topColor: 'rgba(99, 102, 241, 0.4)',
        bottomColor: 'rgba(99, 102, 241, 0.02)',
        lineColor: COLORS.indigo,
        lineWidth: 2,
        title: 'Fear & Greed Index'
      });

      const seriesData = indicators
        .filter(i => i.fearGreedIndex != null)
        .map(i => ({ time: i.time, value: Number(i.fearGreedIndex) }));

      fngSeries.setData(seriesData);

      fngSeries.createPriceLine({
        price: 75,
        color: 'rgba(244, 63, 94, 0.5)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dotted,
        title: 'Extreme Greed (75)'
      });
      fngSeries.createPriceLine({
        price: 25,
        color: 'rgba(16, 185, 129, 0.5)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dotted,
        title: 'Extreme Fear (25)'
      });
    } else if (activeSubTab === 'macro') {
      macroDxySeries = subChart.addSeries(LightweightCharts.LineSeries, {
        priceScaleId: 'right',
        color: COLORS.cyan,
        lineWidth: 2,
        title: 'DXY (Dollar Index)'
      });

      macroSpSeries = subChart.addSeries(LightweightCharts.LineSeries, {
        priceScaleId: 'left',
        color: '#a855f7',
        lineWidth: 2,
        title: 'S&P 500 Index'
      });

      const dxyData = indicators
        .filter(i => i.dxy != null)
        .map(i => ({ time: i.time, value: Number(i.dxy) }));

      const spData = indicators
        .filter(i => i.sp500Close != null)
        .map(i => ({ time: i.time, value: Number(i.sp500Close) }));

      macroDxySeries.setData(dxyData);
      macroSpSeries.setData(spData);
    }

    subChart.timeScale().fitContent();
  }

  // Load Today's Prediction from ML Engine
  async function loadTodayPrediction() {
    try {
      const res = await fetch('/api/prediction/today');
      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      const pred = await res.json();

      // Signal card & badge
      const heroCard = document.getElementById('hero-signal-card');
      const badgeSignal = document.getElementById('badge-signal');
      const textRegime = document.getElementById('text-regime');
      const textProb = document.getElementById('text-bullish-prob');
      const barFill = document.getElementById('prob-bar-fill');
      const labelDate = document.getElementById('label-prediction-date');

      if (labelDate) labelDate.innerText = pred.date || '--';

      const probPercent = (pred.bullishProbability * 100).toFixed(1);
      if (textProb) textProb.innerText = `${probPercent}%`;
      if (barFill) barFill.style.width = `${Math.min(100, Math.max(0, probPercent))}%`;

      if (heroCard && badgeSignal) {
        heroCard.classList.remove('signal-bullish', 'signal-bearish', 'signal-neutral');
        badgeSignal.classList.remove('bullish', 'bearish', 'neutral');

        if (pred.signal === 'BULLISH_OPPORTUNITY') {
          heroCard.classList.add('signal-bullish');
          badgeSignal.classList.add('bullish');
          badgeSignal.innerText = '🟢 BULLISH OPPORTUNITY';
          if (barFill) barFill.style.background = COLORS.bullish;
        } else if (pred.signal === 'BEARISH_RISK') {
          heroCard.classList.add('signal-bearish');
          badgeSignal.classList.add('bearish');
          badgeSignal.innerText = '🔴 BEARISH RISK';
          if (barFill) barFill.style.background = COLORS.bearish;
        } else {
          heroCard.classList.add('signal-neutral');
          badgeSignal.classList.add('neutral');
          badgeSignal.innerText = '⚪ NEUTRAL';
          if (barFill) barFill.style.background = COLORS.indigo;
        }
      }

      if (textRegime) {
        textRegime.innerHTML = `<strong>${escapeHtml(pred.marketRegime)}</strong> <span style="font-size:0.75rem; color:var(--text-dim);">(${escapeHtml(pred.regimeConfidence)} Confidence)</span>`;
      }

      // Render Key Drivers
      const driversContainer = document.getElementById('drivers-container');
      if (driversContainer) {
        if (pred.keyDrivers && pred.keyDrivers.length > 0) {
          driversContainer.innerHTML = pred.keyDrivers.map(d => `
            <div class="driver-item">
              <span class="driver-icon">💡</span>
              <span>${escapeHtml(d)}</span>
            </div>
          `).join('');
        } else {
          driversContainer.innerHTML = `
            <div class="driver-item">
              <span class="driver-icon">ℹ️</span>
              <span>Market indicators within stationary baseline.</span>
            </div>
          `;
        }
      }

      // Macro cards
      loadMacroCards();

    } catch (err) {
      console.error('Failed to load today prediction:', err);
    }
  }

  // Load Macro Stats for Hero Cards
  async function loadMacroCards() {
    try {
      const res = await fetch('/api/marketdata/history?days=2');
      if (!res.ok) return;
      const history = await res.json();
      if (!history || history.length === 0) return;

      const latest = history[history.length - 1];

      // Fear & Greed
      const fngVal = document.getElementById('stat-fng-val');
      const fngDesc = document.getElementById('stat-fng-desc');
      if (fngVal && latest.fearGreedIndex != null) {
        fngVal.innerText = latest.fearGreedIndex;
        let sentiment = 'Neutral';
        let colorClass = 'text-neutral';
        if (latest.fearGreedIndex >= 75) { sentiment = 'Extreme Greed'; colorClass = 'text-bearish'; }
        else if (latest.fearGreedIndex >= 55) { sentiment = 'Greed'; colorClass = 'text-amber'; }
        else if (latest.fearGreedIndex <= 25) { sentiment = 'Extreme Fear'; colorClass = 'text-bullish'; }
        else if (latest.fearGreedIndex <= 45) { sentiment = 'Fear'; colorClass = 'text-cyan'; }

        if (fngDesc) fngDesc.innerHTML = `<span class="${colorClass}">${sentiment}</span>`;
      }

      // Global Macro Card (DXY & US10Y / S&P 500)
      const macroVal = document.getElementById('stat-macro-val');
      const macroDesc = document.getElementById('stat-macro-desc');
      if (macroVal && latest.dxy != null) {
        const dxyNum = Number(latest.dxy);
        macroVal.innerText = dxyNum > 0 ? dxyNum.toFixed(2) : '--';
      }
      if (macroDesc) {
        const yld = latest.us10YearYield ? Number(latest.us10YearYield).toFixed(2) + '%' : '--%';
        const sp = latest.sp500Close ? Number(latest.sp500Close).toLocaleString('en-US', { maximumFractionDigits: 0 }) : '--';
        macroDesc.innerText = `US10Y: ${yld} | S&P: ${sp}`;
      }

      // Funding & ETF
      const featuresRes = await fetch('/api/marketdata/features?days=1');
      if (featuresRes.ok) {
        const feats = await featuresRes.json();
        if (feats && feats.length > 0) {
          const f = feats[feats.length - 1];

          // Funding Z-Score
          const fundVal = document.getElementById('stat-funding-z');
          const fundDesc = document.getElementById('stat-funding-desc');
          if (fundVal && f.fundingZScore30d != null) {
            const z = Number(f.fundingZScore30d);
            fundVal.innerText = (z > 0 ? '+' : '') + z.toFixed(2);
            if (fundDesc) {
              if (z > 1.5) fundDesc.innerHTML = '<span class="text-bearish">Overheated Long Leverage</span>';
              else if (z < -1.5) fundDesc.innerHTML = '<span class="text-bullish">Short Squeeze Pressure / Oversold</span>';
              else fundDesc.innerHTML = '<span class="text-neutral">Balanced Leverage</span>';
            }
          }

          // ETF 7d Net Flow
          const etf7d = document.getElementById('stat-etf-7d');
          const etfDaily = document.getElementById('stat-etf-daily');
          if (etf7d && f.etfFlow7d != null) {
            etf7d.innerText = formatMillions(f.etfFlow7d);
            if (Number(f.etfFlow7d) >= 0) etf7d.className = 'stat-value text-bullish';
            else etf7d.className = 'stat-value text-bearish';
          }
          if (etfDaily && latest.bitcoinEtfNetFlowUsd != null) {
            etfDaily.innerText = `Session: ${formatMillions(latest.bitcoinEtfNetFlowUsd)}`;
          }

          // Global Macro dynamics
          if (macroVal && f.dxyReturn7d != null) {
            const dxyRet = Number(f.dxyReturn7d);
            if (dxyRet < -0.005) macroVal.className = 'stat-value text-bullish';
            else if (dxyRet > 0.005) macroVal.className = 'stat-value text-bearish';
            else macroVal.className = 'stat-value';
          }
        }
      }

    } catch (e) {
      console.warn('Macro cards update failed:', e);
    }
  }

  // Load Historical Predictions into Table
  async function loadHistoricalPredictions() {
    try {
      const res = await fetch('/api/prediction/history?limit=10');
      if (!res.ok) return;

      const items = await res.json();
      const tbody = document.getElementById('history-table-body');
      if (!tbody) return;

      if (!items || items.length === 0) {
        tbody.innerHTML = `<tr><td colspan="6" style="text-align: center; padding: 1.5rem; color: var(--text-dim);">No recorded predictions yet. The first will be saved after the daily 00:05 UTC cycle.</td></tr>`;
        return;
      }

      tbody.innerHTML = items.map(item => {
        let badgeClass = 'neutral';
        if (item.signal === 'BULLISH_OPPORTUNITY') badgeClass = 'bullish';
        if (item.signal === 'BEARISH_RISK') badgeClass = 'bearish';

        const probPercent = (item.bullishProbability * 100).toFixed(1) + '%';
        const priceStr = formatMoney(item.btcClose ?? item.btcPrice);

        return `
          <tr>
            <td>${item.date}</td>
            <td>${priceStr}</td>
            <td><span class="tag-badge ${badgeClass}">${escapeHtml(item.signal)}</span></td>
            <td><strong>${probPercent}</strong></td>
            <td>${escapeHtml(item.marketRegime || '--')}</td>
            <td><span style="color: var(--text-dim);">${escapeHtml(item.confidence || '--')}</span></td>
          </tr>
        `;
      }).join('');

    } catch (err) {
      console.error('Failed to load historical predictions:', err);
    }
  }

  // Run Quick Walk-Forward Backtest
  async function runBacktest() {
    const btn = document.getElementById('btn-run-backtest');
    if (btn) {
      btn.disabled = true;
      btn.innerHTML = `<span class="pulse-dot"></span> Simulating...`;
    }

    try {
      const res = await fetch('/api/backtest/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          holdingPeriodDays: 7,
          stopLossPercent: 0.04,
          takeProfitPercent: 0.08,
          probabilityThreshold: 0.65
        })
      });

      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      const report = await res.json();

      const winRate = report.winRatePercent != null ? report.winRatePercent.toFixed(1) + '%' : (report.winRate ? (report.winRate * 100).toFixed(1) + '%' : 'N/A');
      const maxDd = report.strategyMaxDrawdownPercent != null ? report.strategyMaxDrawdownPercent.toFixed(1) + '%' : (report.maxDrawdown ? (report.maxDrawdown * 100).toFixed(1) + '%' : 'N/A');

      document.getElementById('bt-winrate').innerText = winRate;
      document.getElementById('bt-profitfactor').innerText = report.profitFactor != null ? report.profitFactor.toFixed(2) : 'N/A';
      document.getElementById('bt-sharpe').innerText = report.sharpeRatio != null ? report.sharpeRatio.toFixed(2) : 'N/A';
      document.getElementById('bt-drawdown').innerText = maxDd;

      showToast(`Backtest completed! Trades: ${report.trades.length}, Win Rate: ${winRate}`, 'success');

    } catch (err) {
      console.error('Backtest error:', err);
      showToast('Backtest error: ' + err.message, 'error');
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg> Run Backtest`;
      }
    }
  }

  // Setup UI Event Listeners
  function setupEvents() {
    // Timeframe range buttons
    document.querySelectorAll('.timeframe-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.timeframe-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        const days = parseInt(btn.dataset.days, 10);
        currentDays = days;
        loadChartData(days);
      });
    });

    // Subchart tabs
    document.querySelectorAll('.tab-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        activeSubTab = btn.dataset.tab;
        renderSubchart();
      });
    });

    // Sync button
    const btnSync = document.getElementById('btn-sync');
    if (btnSync) {
      btnSync.addEventListener('click', async () => {
        btnSync.disabled = true;
        btnSync.innerHTML = `<span class="pulse-dot"></span> Syncing...`;
        showToast('Syncing market data from Binance, Farside ETF, and Deribit...', 'info');

        try {
          const res = await fetch('/api/marketdata/sync?days=180', { method: 'POST' });
          if (!res.ok) throw new Error(`HTTP ${res.status}`);

          showToast('Market data synchronized successfully!', 'success');
          await loadChartData(currentDays);
          await loadTodayPrediction();
          await loadHistoricalPredictions();
        } catch (e) {
          showToast('Sync error: ' + e.message, 'error');
        } finally {
          btnSync.disabled = false;
          btnSync.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Sync Markets`;
        }
      });
    }

    // Telegram button
    const btnTelegram = document.getElementById('btn-telegram-test');
    if (btnTelegram) {
      btnTelegram.addEventListener('click', async () => {
        btnTelegram.disabled = true;
        btnTelegram.innerText = 'Sending...';

        try {
          const res = await fetch('/api/alerts/send-today', { method: 'POST' });
          const json = await res.json();

          if (json.sent) {
            showToast('Telegram prediction report delivered successfully!', 'success');
          } else {
            showToast(json.message || 'Telegram Bot is not configured yet in appsettings.json', 'info');
          }
        } catch (e) {
          showToast('Failed to reach alerts API: ' + e.message, 'error');
        } finally {
          btnTelegram.disabled = false;
          btnTelegram.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="22" y1="2" x2="11" y2="13"></line><polygon points="22 2 15 22 11 13 2 9 22 2"></polygon></svg> Telegram Alert`;
        }
      });
    }

    // Backtest button
    const btnBacktest = document.getElementById('btn-run-backtest');
    if (btnBacktest) {
      btnBacktest.addEventListener('click', runBacktest);
    }
  }

  // Application Entry Point
  window.addEventListener('DOMContentLoaded', async () => {
    initCharts();
    setupEvents();

    await loadChartData(currentDays);
    await loadTodayPrediction();
    await loadHistoricalPredictions();

    // Auto-run initial backtest metrics for display
    runBacktest();
  });

})();
