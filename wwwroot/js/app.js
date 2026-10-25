// CryptoPredictor Dashboard Client Application
// TradingView Lightweight Charts v5.2.1 Integration

(function () {
  'use strict';

  // State
  let currentDays = 180;
  let currentTimeframe = '1d'; // '1d' | '4h'
  let activeSubTab = 'funding'; // 'funding' | 'etf' | 'fng'
  let cachedChartData = null;

  // Helper to parse time for Lightweight Charts (supports both 'YYYY-MM-DD' and numeric unix seconds)
  function parseChartTime(timeVal) {
    if (typeof timeVal === 'number') return timeVal;
    if (!timeVal) return timeVal;
    const num = Number(timeVal);
    if (!isNaN(num) && num > 1000000) return num;
    return timeVal;
  }

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
  let liqUpperSeries = null;
  let liqLowerSeries = null;
  let activeFibLines = [];
  let activeLiqLines = [];
  let activeTargetLines = [];
  let predictionUpperSeries = null;
  let predictionLowerSeries = null;
  let predictionCenterSeries = null;
  let showForecastCone = true;
  let currentMainHeight = parseInt(localStorage.getItem('cp_main_chart_height') || '560', 10);
  let currentSubHeight = parseInt(localStorage.getItem('cp_sub_chart_height') || '220', 10);

  // Priority 15: Time Machine (Back to the Future) State
  let tmChart = null;
  let tmCandleSeries = null;
  let tmUpperConeSeries = null;
  let tmLowerConeSeries = null;
  let tmExpectedLineSeries = null;
  let tmTpLine = null;
  let tmSlLine = null;
  let currentTimeMachineData = null;
  let tmPlaybackInterval = null;
  let tmPlaybackSpeed = 600;
  let tmMinDate = '2026-04-14';
  let tmMaxDate = '2026-10-18';

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
        secondsVisible: false,
        rightOffset: 12
      }
    };

    // 1. Main Candlestick Chart
    mainChart = LightweightCharts.createChart(mainContainer, {
      ...commonOptions,
      height: currentMainHeight
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

    // 7-Day Forecast Volatility Cone Series
    predictionUpperSeries = mainChart.addSeries(LightweightCharts.LineSeries, {
      color: 'rgba(244, 63, 94, 0.35)',
      lineWidth: 1,
      lineStyle: LightweightCharts.LineStyle.Dashed,
      priceLineVisible: false,
      lastValueVisible: false,
      crosshairMarkerVisible: false
    });

    predictionLowerSeries = mainChart.addSeries(LightweightCharts.LineSeries, {
      color: 'rgba(244, 63, 94, 0.35)',
      lineWidth: 1,
      lineStyle: LightweightCharts.LineStyle.Dashed,
      priceLineVisible: false,
      lastValueVisible: false,
      crosshairMarkerVisible: false
    });

    predictionCenterSeries = mainChart.addSeries(LightweightCharts.LineSeries, {
      color: '#f43f5e',
      lineWidth: 2,
      lineStyle: LightweightCharts.LineStyle.Dashed,
      priceLineVisible: false,
      lastValueVisible: true,
      crosshairMarkerVisible: true
    });

    // 2. Subchart (Microstructure & Indicators)
    subChart = LightweightCharts.createChart(subContainer, {
      ...commonOptions,
      height: currentSubHeight
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
          mainChart.resize(mainContainer.clientWidth, currentMainHeight);
        } else if (entry.target === subContainer && subChart) {
          subChart.resize(subContainer.clientWidth, currentSubHeight);
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
    let displayTime = '--';
    const numTime = typeof time === 'number' ? time : (time && !isNaN(Number(time)) && Number(time) > 1000000 ? Number(time) : null);
    if (numTime != null) {
      const d = new Date(numTime * 1000);
      const yr = d.getUTCFullYear();
      const mo = String(d.getUTCMonth() + 1).padStart(2, '0');
      const da = String(d.getUTCDate()).padStart(2, '0');
      const hr = String(d.getUTCHours()).padStart(2, '0');
      const mn = String(d.getUTCMinutes()).padStart(2, '0');
      displayTime = `${yr}-${mo}-${da} ${hr}:${mn} UTC`;
    } else if (typeof time === 'string') {
      displayTime = time;
    } else if (time && time.year) {
      displayTime = `${time.year}-${String(time.month).padStart(2, '0')}-${String(time.day).padStart(2, '0')}`;
    }
    document.getElementById('leg-date').innerText = displayTime;
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
  async function loadChartData(days = currentDays, timeframe = currentTimeframe) {
    try {
      currentDays = days;
      currentTimeframe = timeframe;
      const url = `/api/marketdata/chart?days=${days}&timeframe=${timeframe}`;
      const res = await fetch(url);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      const data = await res.json();
      cachedChartData = data;
      renderChart(data, true);
    } catch (err) {
      console.error('Failed to load chart data:', err);
      showToast('Failed to load chart data: ' + err.message, 'error');
    }
  }

  // Render or Re-render Chart from cached/fetched data
  function renderChart(data, shouldFitContent = false) {
    if (!data || !mainChart) return;
    try {
      cachedChartData = data;

    // Update Subtitle description
    const subtitle = document.getElementById('chart-timeframe-desc');
    if (subtitle) {
      subtitle.innerText = currentTimeframe === '4h' 
        ? '4-Hour Candles + Intraday Microstructure & Sweeps' 
        : 'Daily Candles + ML Turning Points';
    }

    // Map candles to LightweightCharts format
      const candleData = data.candles.map(c => ({
        time: parseChartTime(c.time),
        open: Number(c.open),
        high: Number(c.high),
        low: Number(c.low),
        close: Number(c.close)
      }));

      candlestickSeries.setData(candleData);

      // Map volume data
      const volData = data.candles.map(c => ({
        time: parseChartTime(c.time),
        value: Number(c.volume),
        color: Number(c.close) >= Number(c.open) ? 'rgba(16, 185, 129, 0.2)' : 'rgba(244, 63, 94, 0.2)'
      }));
      volumeSeries.setData(volData);

      // Set markers (local bottoms and tops)
      if (markersPrimitive && data.markers) {
        markersPrimitive.setMarkers(data.markers.map(m => ({
          ...m,
          time: parseChartTime(m.time)
        })));
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

      // Update Liquidity Pools & Liquidation Walls on price chart
      if (activeLiqLines && activeLiqLines.length > 0) {
        for (const line of activeLiqLines) {
          candlestickSeries.removePriceLine(line);
        }
        activeLiqLines = [];
      }

      if (data.liquidity) {
        const liq = data.liquidity;

        // 1. Short Liquidation Walls (Upper)
        if (liq.upperWall25x > 0) {
          activeLiqLines.push(candlestickSeries.createPriceLine({
            price: Number(liq.upperWall25x),
            color: 'rgba(244, 63, 94, 0.75)',
            lineWidth: 1,
            lineStyle: LightweightCharts.LineStyle.Solid,
            axisLabelVisible: true,
            title: `Short Liq 25x ($${formatMoney(liq.upperWall25x)})`
          }));
        }

        if (liq.upperWall50x > 0) {
          activeLiqLines.push(candlestickSeries.createPriceLine({
            price: Number(liq.upperWall50x),
            color: 'rgba(244, 63, 94, 0.45)',
            lineWidth: 1,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            axisLabelVisible: true,
            title: `Short Liq 50x ($${formatMoney(liq.upperWall50x)})`
          }));
        }

        // 2. Long Liquidation Walls (Lower)
        if (liq.lowerWall50x > 0) {
          activeLiqLines.push(candlestickSeries.createPriceLine({
            price: Number(liq.lowerWall50x),
            color: 'rgba(16, 185, 129, 0.45)',
            lineWidth: 1,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            axisLabelVisible: true,
            title: `Long Liq 50x ($${formatMoney(liq.lowerWall50x)})`
          }));
        }

        if (liq.lowerWall25x > 0) {
          activeLiqLines.push(candlestickSeries.createPriceLine({
            price: Number(liq.lowerWall25x),
            color: 'rgba(16, 185, 129, 0.75)',
            lineWidth: 1,
            lineStyle: LightweightCharts.LineStyle.Solid,
            axisLabelVisible: true,
            title: `Long Liq 25x ($${formatMoney(liq.lowerWall25x)})`
          }));
        }

        // 3. Update Liquidity Badge in Chart Header
        const liqBadge = document.getElementById('badge-liq-magnet');
        if (liqBadge) {
          liqBadge.style.display = 'inline-flex';
          if (liq.recentLongSweep) {
            liqBadge.className = 'liq-badge sweep-alert';
            liqBadge.innerHTML = '⚡ LONG SWEEP DETECTED';
          } else if (liq.recentShortSweep) {
            liqBadge.className = 'liq-badge sweep-alert';
            liqBadge.innerHTML = '⚡ SHORT SWEEP DETECTED';
          } else if (liq.distToUpperPercent < liq.distToLowerPercent) {
            liqBadge.className = 'liq-badge magnet-short';
            liqBadge.innerHTML = `🧲 Short Liq Wall: $${formatMoney(liq.nearestUpperWall)} (+${liq.distToUpperPercent}%)`;
          } else {
            liqBadge.className = 'liq-badge magnet-long';
            liqBadge.innerHTML = `🧲 Long Liq Wall: $${formatMoney(liq.nearestLowerWall)} (-${liq.distToLowerPercent}%)`;
          }
        }
      }

      // Update Dynamic Targets & 7-Day Forecast Cone
      if (activeTargetLines && activeTargetLines.length > 0) {
        for (const line of activeTargetLines) {
          candlestickSeries.removePriceLine(line);
        }
        activeTargetLines = [];
      }

      if (data.prediction) {
        const pred = data.prediction;
        const isBearish = pred.signal === 'BEARISH_RISK';
        const isBullish = pred.signal === 'BULLISH_OPPORTUNITY';

        if (showForecastCone) {
          // 1. Dynamic Target Price (TP)
          if (pred.targetPrice > 0) {
            const targetColor = isBearish ? '#f43f5e' : (isBullish ? '#10b981' : '#38bdf8');
            const targetSign = pred.targetChangePercent > 0 ? '+' : '';
            activeTargetLines.push(candlestickSeries.createPriceLine({
              price: Number(pred.targetPrice),
              color: targetColor,
              lineWidth: 2,
              lineStyle: LightweightCharts.LineStyle.Dashed,
              axisLabelVisible: true,
              title: `🎯 7D Target: $${Number(pred.targetPrice).toLocaleString('en-US')} (${targetSign}${(Number(pred.targetChangePercent) * 100).toFixed(1)}%)`
            }));
          }

          // 2. Invalidation Level (SL)
          if (pred.invalidationPrice > 0) {
            const invSign = pred.invalidationChangePercent > 0 ? '+' : '';
            activeTargetLines.push(candlestickSeries.createPriceLine({
              price: Number(pred.invalidationPrice),
              color: 'rgba(244, 63, 94, 0.65)',
              lineWidth: 1,
              lineStyle: LightweightCharts.LineStyle.Dotted,
              axisLabelVisible: true,
              title: `🛑 Invalidation (SL): $${Number(pred.invalidationPrice).toLocaleString('en-US')} (${invSign}${(Number(pred.invalidationChangePercent) * 100).toFixed(1)}%)`
            }));
          }

          // 3. Render 7-Day Volatility Forecast Cone
          if (predictionCenterSeries && predictionUpperSeries && predictionLowerSeries) {
            if (pred.projectionCone && pred.projectionCone.length > 0) {
              const coneColor = isBearish ? '#f43f5e' : (isBullish ? '#10b981' : '#38bdf8');
              const bandColor = isBearish ? 'rgba(244, 63, 94, 0.4)' : (isBullish ? 'rgba(16, 185, 129, 0.4)' : 'rgba(56, 189, 248, 0.4)');

              predictionCenterSeries.applyOptions({
                visible: true,
                color: coneColor,
                title: isBearish ? '7D Bearish Trajectory' : (isBullish ? '7D Bullish Trajectory' : '7D Expected Trajectory')
              });
              predictionUpperSeries.applyOptions({ visible: true, color: bandColor });
              predictionLowerSeries.applyOptions({ visible: true, color: bandColor });

              predictionCenterSeries.setData(pred.projectionCone.map(p => ({ time: parseChartTime(p.time), value: Number(p.expectedPrice) })));
              predictionUpperSeries.setData(pred.projectionCone.map(p => ({ time: parseChartTime(p.time), value: Number(p.upperBand) })));
              predictionLowerSeries.setData(pred.projectionCone.map(p => ({ time: parseChartTime(p.time), value: Number(p.lowerBand) })));
            }
          }
        } else {
          // Hide forecast cone series and clear scale tags when toggled off
          if (predictionCenterSeries && predictionUpperSeries && predictionLowerSeries) {
            predictionCenterSeries.applyOptions({ visible: false });
            predictionUpperSeries.applyOptions({ visible: false });
            predictionLowerSeries.applyOptions({ visible: false });
            predictionCenterSeries.setData([]);
            predictionUpperSeries.setData([]);
            predictionLowerSeries.setData([]);
          }
        }

        // 4. Update Target Badge in Chart Header
        const targetBadge = document.getElementById('badge-forecast-target');
        if (targetBadge && pred.targetPrice > 0) {
          if (showForecastCone) {
            targetBadge.style.display = 'inline-flex';
            targetBadge.className = `target-badge ${isBearish ? 'bearish-target' : 'bullish-target'}`;
            const sign = pred.targetChangePercent > 0 ? '+' : '';
            targetBadge.innerHTML = `🎯 7D Target: $${Number(pred.targetPrice).toLocaleString('en-US')} (${sign}${(Number(pred.targetChangePercent) * 100).toFixed(1)}%)`;
          } else {
            targetBadge.style.display = 'none';
          }
        }
      }

      // Update Subchart
      renderSubchart();

      // Fit content only when requested
      if (shouldFitContent && mainChart && mainChart.timeScale) {
        mainChart.timeScale().fitContent();
      }

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

        const rangeElem = document.getElementById('stat-btc-range');
        if (rangeElem && data.candles && data.candles.length > 0) {
          const latestCandle = data.candles[data.candles.length - 1];
          rangeElem.innerText = `24h L: $${Number(latestCandle.low).toLocaleString('en-US', {maximumFractionDigits:0})} • H: $${Number(latestCandle.high).toLocaleString('en-US', {maximumFractionDigits:0})}`;
        }

        // Default legend to latest candle
        if (data.candles.length > 0) {
          const latest = data.candles[data.candles.length - 1];
          setLegendValues(latest.time, latest.open, latest.high, latest.low, latest.close, latest.volume);
        }
      }
    } catch (err) {
      console.error('Failed to render chart:', err);
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
    if (liqUpperSeries) { subChart.removeSeries(liqUpperSeries); liqUpperSeries = null; }
    if (liqLowerSeries) { subChart.removeSeries(liqLowerSeries); liqLowerSeries = null; }

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
        .map(i => ({ time: parseChartTime(i.time), value: Number(i.fundingZScore30d) }));

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
        title: 'Kaufman Wave Efficiency (KER)'
      });

      const seriesData = indicators
        .filter(i => i.kaufmanEfficiencyRatio10d != null)
        .map(i => ({
          time: parseChartTime(i.time),
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
            time: parseChartTime(i.time),
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
        .map(i => ({ time: parseChartTime(i.time), value: Number(i.fearGreedIndex) }));

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
        .map(i => ({ time: parseChartTime(i.time), value: Number(i.dxy) }));

      const spData = indicators
        .filter(i => i.sp500Close != null)
        .map(i => ({ time: parseChartTime(i.time), value: Number(i.sp500Close) }));

      macroDxySeries.setData(dxyData);
      macroSpSeries.setData(spData);
    } else if (activeSubTab === 'liquidity') {
      liqUpperSeries = subChart.addSeries(LightweightCharts.LineSeries, {
        color: '#f43f5e',
        lineWidth: 2,
        title: 'Distance to Short Liq Wall (%)'
      });

      liqLowerSeries = subChart.addSeries(LightweightCharts.LineSeries, {
        color: '#10b981',
        lineWidth: 2,
        title: 'Distance to Long Liq Wall (%)'
      });

      const upperData = indicators
        .filter(i => i.distToUpperLiquidityWall != null)
        .map(i => ({ time: parseChartTime(i.time), value: Number(i.distToUpperLiquidityWall) }));

      const lowerData = indicators
        .filter(i => i.distToLowerLiquidityWall != null)
        .map(i => ({ time: parseChartTime(i.time), value: -Number(i.distToLowerLiquidityWall) }));

      liqUpperSeries.setData(upperData);
      liqLowerSeries.setData(lowerData);

      liqUpperSeries.createPriceLine({
        price: 0,
        color: 'rgba(255, 255, 255, 0.35)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Solid,
        title: 'Current Price (Baseline)'
      });
      liqUpperSeries.createPriceLine({
        price: 2.0,
        color: 'rgba(244, 63, 94, 0.65)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dashed,
        title: 'Short Squeeze Magnet (+2%)'
      });
      liqUpperSeries.createPriceLine({
        price: -2.0,
        color: 'rgba(16, 185, 129, 0.65)',
        lineWidth: 1,
        lineStyle: LightweightCharts.LineStyle.Dashed,
        title: 'Long Flush Magnet (-2%)'
      });
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

      // Update Targets Row in Hero Card
      const targetsRow = document.getElementById('pred-targets-row');
      const targetVal = document.getElementById('pred-target-val');
      const invVal = document.getElementById('pred-invalidation-val');
      const rrVal = document.getElementById('pred-rr-val');

      if (targetsRow && pred.targetPrice > 0) {
        targetsRow.style.display = 'flex';
        const isBearish = pred.signal === 'BEARISH_RISK';
        const targetSign = pred.targetChangePercent > 0 ? '+' : '';
        const invSign = pred.invalidationChangePercent > 0 ? '+' : '';

        if (targetVal) {
          targetVal.innerText = `🎯 7D TP: $${Number(pred.targetPrice).toLocaleString('en-US')} (${targetSign}${(Number(pred.targetChangePercent) * 100).toFixed(1)}%)`;
          targetVal.className = `target-pill ${isBearish ? 'bearish-side' : ''}`;
        }
        if (invVal) {
          invVal.innerText = `🛑 SL: $${Number(pred.invalidationPrice).toLocaleString('en-US')} (${invSign}${(Number(pred.invalidationChangePercent) * 100).toFixed(1)}%)`;
          invVal.className = `invalidation-pill ${!isBearish ? 'bullish-side' : ''}`;
        }
        if (rrVal) {
          rrVal.innerText = `R:R ${Number(pred.riskRewardRatio).toFixed(1)}`;
        }
      }

      // Render Ensemble Council Breakdown
      const badgeConsensus = document.getElementById('badge-consensus');
      const ensembleBox = document.getElementById('ensemble-council-breakdown');
      const ensembleVerdictText = document.getElementById('ensemble-verdict-text');
      const ensembleVotesList = document.getElementById('ensemble-votes-list');

      if (pred.ensemble) {
        const ens = pred.ensemble;
        if (badgeConsensus) {
          badgeConsensus.style.display = 'inline-block';
          const agreement = Math.round(Number(ens.consensusAgreementPercent));
          badgeConsensus.innerText = `${agreement}% Agree`;

          let tagClass = 'consensus-tag';
          if (ens.consensusVerdict.includes('Unanimous')) tagClass += ' unanimous';
          else if (ens.consensusVerdict.includes('Strong')) tagClass += ' strong';
          else if (ens.consensusVerdict.includes('Moderate')) tagClass += ' moderate';
          else tagClass += ' divided';

          badgeConsensus.className = tagClass;
          badgeConsensus.title = `Consensus Verdict: ${ens.consensusVerdict} (${agreement}% Agreement across 4 models)`;
        }

        if (ensembleBox && ensembleVotesList) {
          ensembleBox.style.display = 'block';
          if (ensembleVerdictText) {
            ensembleVerdictText.innerText = ens.consensusVerdict;
          }

          if (ens.votes && ens.votes.length > 0) {
            ensembleVotesList.innerHTML = ens.votes.map(v => {
              const p = Math.round(Number(v.probability) * 100);
              const w = Math.round(Number(v.weight) * 100);
              const isBull = v.signal === 'BULLISH';
              const isBear = v.signal === 'BEARISH';
              const fillClass = isBull ? 'bullish' : (isBear ? 'bearish' : 'neutral');
              const probColor = isBull ? '#34d399' : (isBear ? '#fb7185' : '#94a3b8');

              return `
                <div class="ensemble-vote-row">
                  <span class="ens-model-badge" title="${escapeHtml(v.modelFamily)}">${escapeHtml(v.modelName)}</span>
                  <div class="ens-bar-track">
                    <div class="ens-bar-fill ${fillClass}" style="width: ${p}%;"></div>
                  </div>
                  <span class="ens-vote-prob" style="color: ${probColor};">${p}%</span>
                  <span class="ens-vote-weight" title="Dynamic Regime Weight">w:${w}%</span>
                </div>
              `;
            }).join('');
          }
        }
      } else {
        if (badgeConsensus) badgeConsensus.style.display = 'none';
        if (ensembleBox) ensembleBox.style.display = 'none';
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

  // Load Historical Predictions Journal & Live Accuracy Metrics into Table
  async function loadHistoricalPredictions() {
    try {
      const res = await fetch('/api/prediction/journal?limit=30');
      if (!res.ok) return;

      const data = await res.json();
      const stats = data.stats;
      const items = data.items;
      const tbody = document.getElementById('history-table-body');
      if (!tbody) return;

      // Populate Live KPI Stats Bar
      if (stats) {
        const elWinRate = document.getElementById('j-stat-winrate');
        const elTargetRate = document.getElementById('j-stat-targetrate');
        const elProfitFactor = document.getElementById('j-stat-profitfactor');
        const elCumPnl = document.getElementById('j-stat-cumpnl');
        const elCounts = document.getElementById('j-stat-counts');

        if (elWinRate) {
          elWinRate.innerText = stats.resolvedPredictions > 0 ? `${Number(stats.winRatePercent).toFixed(1)}%` : '--%';
          elWinRate.style.color = stats.winRatePercent >= 55 ? '#34d399' : (stats.winRatePercent > 0 ? '#fb7185' : 'var(--text-main)');
        }
        if (elTargetRate) {
          elTargetRate.innerText = stats.resolvedPredictions > 0 ? `${Number(stats.targetHitRatePercent).toFixed(1)}%` : '--%';
          elTargetRate.style.color = stats.targetHitRatePercent >= 40 ? '#34d399' : 'var(--text-main)';
        }
        if (elProfitFactor) {
          elProfitFactor.innerText = stats.resolvedPredictions > 0 ? Number(stats.profitFactor).toFixed(2) : '--';
          elProfitFactor.style.color = stats.profitFactor >= 1.5 ? '#34d399' : (stats.profitFactor < 1.0 ? '#fb7185' : 'var(--text-main)');
        }
        if (elCumPnl) {
          const pnlVal = Number(stats.cumulativeReturnPercent);
          elCumPnl.innerText = stats.resolvedPredictions > 0 ? `${pnlVal >= 0 ? '+' : ''}${pnlVal.toFixed(1)}%` : '--%';
          elCumPnl.style.color = pnlVal > 0 ? '#34d399' : (pnlVal < 0 ? '#fb7185' : 'var(--text-main)');
        }
        if (elCounts) {
          elCounts.innerText = `${stats.resolvedPredictions} / ${stats.pendingPredictions}`;
        }
      }

      if (!items || items.length === 0) {
        tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; padding: 2rem; color: var(--text-dim);">No recorded predictions yet. The first will be recorded after the daily 00:05 UTC cycle.</td></tr>`;
        return;
      }

      tbody.innerHTML = items.map(item => {
        let badgeClass = 'neutral';
        if (item.signal === 'BULLISH_OPPORTUNITY') badgeClass = 'bullish';
        if (item.signal === 'BEARISH_RISK') badgeClass = 'bearish';

        const priceStr = formatMoney(item.btcClose);

        // Target & Invalidation compact cell
        const tpSign = item.targetChangePercent >= 0 ? '+' : '';
        const slSign = item.invalidationChangePercent >= 0 ? '+' : '';
        const tpStr = `TP: $${Number(item.targetPrice).toLocaleString('en-US')} (${tpSign}${(item.targetChangePercent * 100).toFixed(1)}%)`;
        const slStr = `SL: $${Number(item.invalidationPrice).toLocaleString('en-US')} (${slSign}${(item.invalidationChangePercent * 100).toFixed(1)}%)`;
        const targetsHtml = `
          <div class="targets-cell">
            <span class="tp-text">${escapeHtml(tpStr)}</span>
            <span class="sl-text">${escapeHtml(slStr)}</span>
          </div>
        `;

        // Consensus summary
        let consensusHtml = '<span style="color:var(--text-dim); font-size:0.75rem;">--</span>';
        if (item.consensusVerdict) {
          const agreeStr = item.consensusAgreementPercent ? ` (${Math.round(item.consensusAgreementPercent)}%)` : '';
          consensusHtml = `<span style="font-family:var(--font-mono); font-size:0.72rem; color:#a5b4fc;">${escapeHtml(item.consensusVerdict)}${agreeStr}</span>`;
        }

        // Outcome Badge
        let outcomeBadgeHtml;
        let pnlHtml;

        switch (item.outcomeStatus) {
          case 'TARGET_HIT':
            outcomeBadgeHtml = `<span class="outcome-badge tp-hit">🎯 TP HIT</span>`;
            break;
          case 'INVALIDATED':
            outcomeBadgeHtml = `<span class="outcome-badge sl-hit">🛑 SL HIT</span>`;
            break;
          case 'EXPIRED_PROFIT':
            outcomeBadgeHtml = `<span class="outcome-badge exp-profit">⏱️ EXPIRED</span>`;
            break;
          case 'EXPIRED_LOSS':
            outcomeBadgeHtml = `<span class="outcome-badge exp-loss">⏱️ EXPIRED</span>`;
            break;
          default:
            outcomeBadgeHtml = `<span class="outcome-badge pending">⏳ DAY ${item.elapsedDays}/7</span>`;
            break;
        }

        // Realized or Unrealized PnL Cell
        if (item.outcomeStatus !== 'PENDING' && item.realizedReturnPercent !== null && item.realizedReturnPercent !== undefined) {
          const ret = Number(item.realizedReturnPercent) * 100;
          const isPos = ret > 0;
          const sign = isPos ? '+' : '';
          pnlHtml = `<span class="pnl-cell ${isPos ? 'positive' : 'negative'}">${sign}${ret.toFixed(1)}%</span>`;
        } else if (item.unrealizedReturnPercent !== null && item.unrealizedReturnPercent !== undefined) {
          const uRet = Number(item.unrealizedReturnPercent) * 100;
          const isPos = uRet >= 0;
          const sign = isPos ? '+' : '';
          pnlHtml = `<span class="pnl-cell unrealized" title="Unrealized floating return">${sign}${uRet.toFixed(1)}% (open)</span>`;
        } else {
          pnlHtml = `<span class="pnl-cell unrealized">--</span>`;
        }

        return `
          <tr>
            <td style="font-family: var(--font-mono); font-size: 0.75rem;">${item.date}</td>
            <td style="font-family: var(--font-mono); font-weight: 600;">${priceStr}</td>
            <td><span class="tag-badge ${badgeClass}">${escapeHtml(item.signal)}</span></td>
            <td>${targetsHtml}</td>
            <td>${consensusHtml}</td>
            <td>${outcomeBadgeHtml}</td>
            <td>${pnlHtml}</td>
          </tr>
        `;
      }).join('');

    } catch (err) {
      console.error('Failed to load live prediction journal:', err);
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
    // Resolution selector (1D vs 4H)
    document.querySelectorAll('.res-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        if (btn.classList.contains('active')) return;
        document.querySelectorAll('.res-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        currentTimeframe = btn.dataset.res || '1d';
        
        // Dynamically adjust range buttons for 4H or 1D
        const tfBtns = document.querySelectorAll('.timeframe-btn');
        if (currentTimeframe === '4h') {
          if (tfBtns.length >= 5) {
            tfBtns[0].innerText = '7D'; tfBtns[0].dataset.days = '7';
            tfBtns[1].innerText = '14D'; tfBtns[1].dataset.days = '14';
            tfBtns[2].innerText = '30D'; tfBtns[2].dataset.days = '30';
            tfBtns[3].innerText = '60D'; tfBtns[3].dataset.days = '60';
            tfBtns[4].innerText = 'ALL'; tfBtns[4].dataset.days = '0';
          }
          tfBtns.forEach(b => b.classList.remove('active'));
          if (tfBtns[2]) tfBtns[2].classList.add('active');
          currentDays = 30;
        } else {
          if (tfBtns.length >= 5) {
            tfBtns[0].innerText = '30D'; tfBtns[0].dataset.days = '30';
            tfBtns[1].innerText = '90D'; tfBtns[1].dataset.days = '90';
            tfBtns[2].innerText = '180D'; tfBtns[2].dataset.days = '180';
            tfBtns[3].innerText = '1Y'; tfBtns[3].dataset.days = '365';
            tfBtns[4].innerText = 'ALL'; tfBtns[4].dataset.days = '0';
          }
          tfBtns.forEach(b => b.classList.remove('active'));
          if (tfBtns[2]) tfBtns[2].classList.add('active');
          currentDays = 180;
        }

        loadChartData(currentDays, currentTimeframe);
      });
    });

    // Timeframe range buttons
    document.querySelectorAll('.timeframe-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.timeframe-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        const days = parseInt(btn.dataset.days, 10);
        currentDays = days;
        loadChartData(days, currentTimeframe);
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

    // Toggle 7D Forecast Cone Button
    const btnToggleCone = document.getElementById('btn-toggle-cone');
    if (btnToggleCone) {
      btnToggleCone.addEventListener('click', () => {
        showForecastCone = !showForecastCone;
        btnToggleCone.classList.toggle('active', showForecastCone);
        if (cachedChartData) {
          renderChart(cachedChartData, false);
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

    // Interactive Tooltips (Click / Tap toggle for mobile & desktop)
    document.querySelectorAll('.tooltip-trigger').forEach(trigger => {
      trigger.addEventListener('click', (e) => {
        e.stopPropagation();
        const isActive = trigger.classList.contains('active');
        document.querySelectorAll('.tooltip-trigger.active').forEach(t => t.classList.remove('active'));
        if (!isActive) trigger.classList.add('active');
      });
    });

    document.addEventListener('click', () => {
      document.querySelectorAll('.tooltip-trigger.active').forEach(t => t.classList.remove('active'));
    });
  }

  // Draggable Splitters for Resizing Charts Vertically
  function setupChartResizers() {
    const mainContainer = document.getElementById('main-chart');
    const subContainer = document.getElementById('sub-chart');
    const resizerMain = document.getElementById('resizer-main');
    const resizerSub = document.getElementById('resizer-sub');
    const btnExpand = document.getElementById('btn-chart-expand');
    const chartCard = document.querySelector('.chart-card');

    if (mainContainer) mainContainer.style.height = `${currentMainHeight}px`;
    if (subContainer) subContainer.style.height = `${currentSubHeight}px`;

    function makeDraggable(resizer, onDrag) {
      if (!resizer) return;
      let startY = 0;
      let isDragging = false;

      function onPointerDown(e) {
        isDragging = true;
        startY = e.clientY != null ? e.clientY : (e.touches && e.touches[0].clientY);
        resizer.classList.add('dragging');
        document.body.style.cursor = 'row-resize';
        document.body.style.userSelect = 'none';

        window.addEventListener('mousemove', onPointerMove);
        window.addEventListener('mouseup', onPointerUp);
        window.addEventListener('touchmove', onPointerMove, { passive: false });
        window.addEventListener('touchend', onPointerUp);
      }

      function onPointerMove(e) {
        if (!isDragging) return;
        const clientY = e.clientY != null ? e.clientY : (e.touches && e.touches[0].clientY);
        if (clientY == null) return;
        const deltaY = clientY - startY;
        startY = clientY;
        onDrag(deltaY);
      }

      function onPointerUp() {
        if (!isDragging) return;
        isDragging = false;
        resizer.classList.remove('dragging');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';

        window.removeEventListener('mousemove', onPointerMove);
        window.removeEventListener('mouseup', onPointerUp);
        window.removeEventListener('touchmove', onPointerMove);
        window.removeEventListener('touchend', onPointerUp);
      }

      resizer.addEventListener('mousedown', onPointerDown);
      resizer.addEventListener('touchstart', onPointerDown, { passive: true });
    }

    // Main Chart Resizer (Drag up/down)
    makeDraggable(resizerMain, (deltaY) => {
      currentMainHeight = Math.max(340, Math.min(1200, currentMainHeight + deltaY));
      if (mainContainer) mainContainer.style.height = `${currentMainHeight}px`;
      if (mainChart) mainChart.resize(mainContainer.clientWidth, currentMainHeight);
      localStorage.setItem('cp_main_chart_height', currentMainHeight);
    });

    // Subchart Resizer (Drag up/down)
    makeDraggable(resizerSub, (deltaY) => {
      currentSubHeight = Math.max(140, Math.min(600, currentSubHeight + deltaY));
      if (subContainer) subContainer.style.height = `${currentSubHeight}px`;
      if (subChart) subChart.resize(subContainer.clientWidth, currentSubHeight);
      localStorage.setItem('cp_sub_chart_height', currentSubHeight);
    });

    // Double click to reset to comfortable defaults
    if (resizerMain) {
      resizerMain.addEventListener('dblclick', () => {
        currentMainHeight = 560;
        if (mainContainer) mainContainer.style.height = '560px';
        if (mainChart) mainChart.resize(mainContainer.clientWidth, 560);
        localStorage.setItem('cp_main_chart_height', '560');
        showToast('Main chart height reset to 560px', 'info');
      });
    }

    if (resizerSub) {
      resizerSub.addEventListener('dblclick', () => {
        currentSubHeight = 220;
        if (subContainer) subContainer.style.height = '220px';
        if (subChart) subChart.resize(subContainer.clientWidth, 220);
        localStorage.setItem('cp_sub_chart_height', '220');
        showToast('Subchart height reset to 220px', 'info');
      });
    }

    // Fullscreen / Expanded Mode Toggle
    if (btnExpand && chartCard) {
      btnExpand.addEventListener('click', () => {
        chartCard.classList.toggle('fullscreen-mode');
        const isFull = chartCard.classList.contains('fullscreen-mode');
        btnExpand.innerHTML = isFull ? '✕' : '⛶';
        btnExpand.title = isFull ? 'Exit Expanded View' : 'Toggle Fullscreen / Expanded View';

        setTimeout(() => {
          if (mainChart && mainContainer) mainChart.resize(mainContainer.clientWidth, currentMainHeight);
          if (subChart && subContainer) subChart.resize(subContainer.clientWidth, currentSubHeight);
        }, 100);
      });
    }
  }

  // ==========================================================================
  // Priority 15: Back to the Future (Time-Machine Visual Backtest)
  // ==========================================================================

  function initTimeMachineChart() {
    if (tmChart) return tmChart;
    const container = document.getElementById('time-machine-chart');
    if (!container || !window.LightweightCharts) return null;

    tmChart = window.LightweightCharts.createChart(container, {
      width: container.clientWidth || 800,
      height: 440,
      layout: {
        background: { type: 'solid', color: '#090e1a' },
        textColor: '#94a3b8',
        fontSize: 11,
        fontFamily: 'system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
      },
      grid: {
        vertLines: { color: 'rgba(255, 255, 255, 0.03)' },
        horzLines: { color: 'rgba(255, 255, 255, 0.03)' }
      },
      crosshair: {
        mode: 1
      },
      rightPriceScale: {
        borderColor: 'rgba(255, 255, 255, 0.08)',
        scaleMargins: { top: 0.1, bottom: 0.1 }
      },
      timeScale: {
        borderColor: 'rgba(255, 255, 255, 0.08)',
        timeVisible: true
      }
    });

    tmCandleSeries = tmChart.addCandlestickSeries({
      upColor: '#10b981',
      downColor: '#f43f5e',
      borderVisible: false,
      wickUpColor: '#10b981',
      wickDownColor: '#f43f5e'
    });

    tmUpperConeSeries = tmChart.addLineSeries({
      color: 'rgba(56, 189, 248, 0.7)',
      lineWidth: 1,
      lineStyle: 2,
      priceLineVisible: false,
      title: 'Upper Band (+1σ)'
    });

    tmLowerConeSeries = tmChart.addLineSeries({
      color: 'rgba(56, 189, 248, 0.7)',
      lineWidth: 1,
      lineStyle: 2,
      priceLineVisible: false,
      title: 'Lower Band (-1σ)'
    });

    tmExpectedLineSeries = tmChart.addLineSeries({
      color: '#f59e0b',
      lineWidth: 2,
      lineStyle: 0,
      priceLineVisible: false,
      title: 'Expected Path'
    });

    // Responsive resize
    const resizeObserver = new ResizeObserver(entries => {
      if (!entries || !entries[0] || !tmChart) return;
      const { width } = entries[0].contentRect;
      if (width > 0) {
        tmChart.resize(width, 440);
      }
    });
    resizeObserver.observe(container);

    return tmChart;
  }

  async function initTimeMachine() {
    const dateInput = document.getElementById('tm-date-input');
    const btnTravel = document.getElementById('btn-tm-travel');
    const btnPlay = document.getElementById('btn-tm-play');
    const btnReset = document.getElementById('btn-tm-reset');
    const btnRandom = document.getElementById('btn-tm-random');
    const btnCloseOutcome = document.getElementById('btn-tm-close-outcome');

    try {
      const resp = await fetch('/api/prediction/time-machine/range');
      if (resp.ok) {
        const range = await resp.json();
        if (range.minDate && range.maxDate) {
          tmMinDate = range.minDate;
          tmMaxDate = range.maxDate;
          if (dateInput) {
            dateInput.min = tmMinDate;
            dateInput.max = tmMaxDate;
            dateInput.value = range.defaultDate || '2026-09-28';
          }
        }
      }
    } catch (e) {
      console.warn('Failed to fetch time-machine range:', e);
    }

    // Presets
    document.querySelectorAll('.tm-preset-btn[data-date]').forEach(btn => {
      btn.addEventListener('click', () => {
        const d = btn.getAttribute('data-date');
        if (d && dateInput) {
          dateInput.value = d;
          loadTimeMachineSimulation(d);
        }
      });
    });

    // Random day button
    if (btnRandom && dateInput) {
      btnRandom.addEventListener('click', () => {
        const minT = new Date(tmMinDate).getTime();
        const maxT = new Date(tmMaxDate).getTime();
        const randT = minT + Math.random() * (maxT - minT);
        const randDate = new Date(randT).toISOString().split('T')[0];
        dateInput.value = randDate;
        loadTimeMachineSimulation(randDate);
      });
    }

    if (btnTravel && dateInput) {
      btnTravel.addEventListener('click', () => {
        if (dateInput.value) {
          loadTimeMachineSimulation(dateInput.value);
        }
      });
    }

    if (btnPlay) {
      btnPlay.addEventListener('click', () => {
        playTimeMachineFuture();
      });
    }

    if (btnReset) {
      btnReset.addEventListener('click', () => {
        resetTimeMachine();
      });
    }

    if (btnCloseOutcome) {
      btnCloseOutcome.addEventListener('click', () => {
        const card = document.getElementById('tm-outcome-card');
        if (card) card.style.display = 'none';
      });
    }

    // Speed selector
    document.querySelectorAll('.tm-speed-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.tm-speed-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        tmPlaybackSpeed = parseInt(btn.getAttribute('data-speed') || '600', 10);
      });
    });

    // Auto-load initial simulation on boot
    const initialDate = (dateInput && dateInput.value) ? dateInput.value : '2026-09-28';
    loadTimeMachineSimulation(initialDate);
  }

  async function loadTimeMachineSimulation(dateStr) {
    const btnTravel = document.getElementById('btn-tm-travel');
    const btnPlay = document.getElementById('btn-tm-play');
    const btnReset = document.getElementById('btn-tm-reset');
    const outcomeCard = document.getElementById('tm-outcome-card');
    const banner = document.getElementById('tm-prediction-banner');
    const councilSec = document.getElementById('tm-council-section');
    const statusText = document.getElementById('tm-playback-status');

    if (tmPlaybackInterval) {
      clearInterval(tmPlaybackInterval);
      tmPlaybackInterval = null;
    }

    if (btnTravel) {
      btnTravel.disabled = true;
      btnTravel.innerHTML = '⏳ Cofam w czasie...';
    }
    if (btnPlay) btnPlay.disabled = true;
    if (btnReset) btnReset.disabled = true;
    if (outcomeCard) outcomeCard.style.display = 'none';
    if (statusText) statusText.textContent = 'Trenowanie modeli Out-of-Sample dla wybranego dnia...';

    try {
      const resp = await fetch(`/api/prediction/historical?date=${dateStr}&strictlyOutOfSample=true`);
      if (!resp.ok) {
        const errText = await resp.text();
        showToast(`Nie udało się cofnąć w czasie: ${errText}`, 'error');
        if (statusText) statusText.textContent = `Błąd: ${errText}`;
        return;
      }

      const data = await resp.json();
      currentTimeMachineData = data;

      initTimeMachineChart();

      // Clear previous price lines
      if (tmTpLine && tmCandleSeries) {
        tmCandleSeries.removePriceLine(tmTpLine);
        tmTpLine = null;
      }
      if (tmSlLine && tmCandleSeries) {
        tmCandleSeries.removePriceLine(tmSlLine);
        tmSlLine = null;
      }

      // Populate historical candles up to date T
      const histData = (data.historicalCandles || []).map(c => ({
        time: parseChartTime(c.time),
        open: Number(c.open),
        high: Number(c.high),
        low: Number(c.low),
        close: Number(c.close)
      }));

      tmCandleSeries.setData(histData);

      // Populate 7D Forecast Cone starting at date T
      if (data.prediction && data.prediction.projectionCone && data.prediction.projectionCone.length > 0) {
        const upper = data.prediction.projectionCone.map(p => ({
          time: parseChartTime(p.time),
          value: Number(p.upperBand)
        }));
        const lower = data.prediction.projectionCone.map(p => ({
          time: parseChartTime(p.time),
          value: Number(p.lowerBand)
        }));
        const exp = data.prediction.projectionCone.map(p => ({
          time: parseChartTime(p.time),
          value: Number(p.expectedPrice)
        }));

        tmUpperConeSeries.setData(upper);
        tmLowerConeSeries.setData(lower);
        tmExpectedLineSeries.setData(exp);
      } else {
        tmUpperConeSeries.setData([]);
        tmLowerConeSeries.setData([]);
        tmExpectedLineSeries.setData([]);
      }

      // Draw TP and SL price lines
      const tp = Number(data.prediction.targetPrice);
      const sl = Number(data.prediction.invalidationPrice);
      if (tp > 0) {
        tmTpLine = tmCandleSeries.createPriceLine({
          price: tp,
          color: '#10b981',
          lineWidth: 2,
          lineStyle: 1,
          axisLabelVisible: true,
          title: `🎯 TP: $${tp.toLocaleString()}`
        });
      }
      if (sl > 0) {
        tmSlLine = tmCandleSeries.createPriceLine({
          price: sl,
          color: '#f43f5e',
          lineWidth: 2,
          lineStyle: 1,
          axisLabelVisible: true,
          title: `🛑 SL: $${sl.toLocaleString()}`
        });
      }

      tmChart.timeScale().fitContent();

      // Update banner
      const dateEl = document.getElementById('tm-banner-date');
      const sigEl = document.getElementById('tm-banner-signal');
      const regEl = document.getElementById('tm-banner-regime');
      const probEl = document.getElementById('tm-banner-prob');
      const consEl = document.getElementById('tm-banner-consensus');
      const tpEl = document.getElementById('tm-banner-tp');
      const slEl = document.getElementById('tm-banner-sl');
      const rrEl = document.getElementById('tm-banner-rr');

      if (dateEl) dateEl.textContent = data.date;
      if (sigEl) {
        sigEl.textContent = data.prediction.signal.replace(/_/g, ' ');
        sigEl.className = 'signal-badge ' + (data.prediction.signal === 'BULLISH_OPPORTUNITY' ? 'bullish' : data.prediction.signal === 'BEARISH_RISK' ? 'bearish' : 'neutral');
      }
      if (regEl) regEl.textContent = data.prediction.marketRegime || 'Consolidation';
      if (probEl) probEl.textContent = `${(data.prediction.bullishProbability * 100).toFixed(1)}%`;
      if (consEl) {
        consEl.textContent = data.prediction.ensemble ? data.prediction.ensemble.consensusVerdict : 'Model Council';
      }
      if (tpEl) tpEl.textContent = `🎯 TP: $${tp.toLocaleString()} (${(data.prediction.targetChangePercent * 100).toFixed(1)}%)`;
      if (slEl) slEl.textContent = `🛑 SL: $${sl.toLocaleString()} (${(data.prediction.invalidationChangePercent * 100).toFixed(1)}%)`;
      if (rrEl) rrEl.textContent = `R:R ${data.prediction.riskRewardRatio || 2.0}`;

      const fCount = data.forwardCandles ? data.forwardCandles.length : 0;
      if (statusText) statusText.textContent = `Gotowy do odtworzenia. Czeka ${fCount} świec przyszłości.`;

      if (banner) banner.style.display = 'grid';

      // Render Model Council Votes
      renderTimeMachineVotes(data.prediction.ensemble, data.prediction.keyDrivers);
      if (councilSec) councilSec.style.display = 'block';

      if (btnPlay) btnPlay.disabled = fCount === 0;
      if (btnReset) btnReset.disabled = false;

      showToast(`Przeniesiono do dnia ${dateStr}! Predykcja gotowa.`, 'success');
    } catch (err) {
      console.error('Error loading time machine simulation:', err);
      showToast('Wystąpił błąd podczas ładowania symulacji.', 'error');
    } finally {
      if (btnTravel) {
        btnTravel.disabled = false;
        btnTravel.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg> Załaduj Dzień T';
      }
    }
  }

  function renderTimeMachineVotes(ensemble, drivers) {
    const votesContainer = document.getElementById('tm-votes-grid');
    const driversContainer = document.getElementById('tm-drivers-box');

    if (votesContainer) {
      if (!ensemble || !ensemble.votes || ensemble.votes.length === 0) {
        votesContainer.innerHTML = '<div style="color: var(--text-dim); font-size: 0.78rem;">Pojedynczy model heurystyczny (brak wag rady).</div>';
      } else {
        votesContainer.innerHTML = ensemble.votes.map(v => {
          const prob = (v.probability * 100).toFixed(0);
          const weight = (v.weight * 100).toFixed(0);
          const sigClass = v.signal === 'BULLISH' ? 'text-bullish' : v.signal === 'BEARISH' ? 'text-bearish' : '';
          return `
            <div class="tm-vote-card">
              <div style="display: flex; justify-content: space-between; align-items: baseline;">
                <strong style="color: var(--text-main); font-size: 0.78rem;">${escapeHtml(v.modelName)}</strong>
                <span class="${sigClass}" style="font-weight: 700; font-size: 0.72rem; font-family: var(--font-mono);">${v.signal}</span>
              </div>
              <div style="display: flex; justify-content: space-between; font-size: 0.70rem; color: var(--text-dim); margin-top: 0.2rem;">
                <span>Bullish Prob: <strong style="color: var(--text-main);">${prob}%</strong></span>
                <span>Waga: <strong style="color: #c084fc;">${weight}%</strong></span>
              </div>
              <div class="ens-bar-track" style="margin-top: 0.35rem;">
                <div class="ens-bar-fill" style="width: ${prob}%; background: ${v.signal === 'BULLISH' ? '#10b981' : v.signal === 'BEARISH' ? '#f43f5e' : '#3b82f6'};"></div>
              </div>
            </div>
          `;
        }).join('');
      }
    }

    if (driversContainer) {
      if (!drivers || drivers.length === 0) {
        driversContainer.textContent = 'Brak specyficznych czynników rynkowych w tym dniu.';
      } else {
        driversContainer.innerHTML = '<strong>Kluczowe czynniki w dniu symulacji:</strong><ul style="margin: 0.4rem 0 0 1.2rem; padding: 0;">' +
          drivers.map(d => `<li style="margin-bottom: 0.25rem;">${escapeHtml(d)}</li>`).join('') +
          '</ul>';
      }
    }
  }

  function playTimeMachineFuture() {
    if (!currentTimeMachineData || !currentTimeMachineData.forwardCandles) return;
    const forward = currentTimeMachineData.forwardCandles;
    if (forward.length === 0) return;

    const btnPlay = document.getElementById('btn-tm-play');
    const statusText = document.getElementById('tm-playback-status');
    const outcomeCard = document.getElementById('tm-outcome-card');

    if (btnPlay) btnPlay.disabled = true;
    if (outcomeCard) outcomeCard.style.display = 'none';

    if (tmPlaybackSpeed === 0) {
      // Instant reveal
      forward.forEach(c => {
        tmCandleSeries.update({
          time: parseChartTime(c.time),
          open: Number(c.open),
          high: Number(c.high),
          low: Number(c.low),
          close: Number(c.close)
        });
      });
      if (statusText) statusText.textContent = `Odsłonięto błyskawicznie wszystkie ${forward.length} dni przyszłości!`;
      showTimeMachineOutcome();
      if (btnPlay) btnPlay.disabled = false;
      return;
    }

    let idx = 0;
    if (statusText) statusText.textContent = `Odkrywanie przyszłości: dzień 1/${forward.length}...`;

    if (tmPlaybackInterval) clearInterval(tmPlaybackInterval);

    tmPlaybackInterval = setInterval(() => {
      if (idx < forward.length) {
        const c = forward[idx];
        tmCandleSeries.update({
          time: parseChartTime(c.time),
          open: Number(c.open),
          high: Number(c.high),
          low: Number(c.low),
          close: Number(c.close)
        });
        if (statusText) {
          statusText.textContent = `Odkryto dzień ${idx + 1}/${forward.length} (${c.time}) • Close: $${Number(c.close).toLocaleString()}`;
        }
        idx++;
      } else {
        clearInterval(tmPlaybackInterval);
        tmPlaybackInterval = null;
        if (statusText) statusText.textContent = `Odsłonięto komplet ${forward.length} dni przyszłości.`;
        showTimeMachineOutcome();
        if (btnPlay) btnPlay.disabled = false;
      }
    }, tmPlaybackSpeed);
  }

  function showTimeMachineOutcome() {
    if (!currentTimeMachineData) return;
    const data = currentTimeMachineData;
    const res = data.resolution || {};
    const outcome = res.outcomeStatus || 'PENDING';
    const retPct = res.realizedReturnPercent != null ? (res.realizedReturnPercent * 100).toFixed(1) : '--';
    const hitDay = res.hitDayIndex != null ? res.hitDayIndex + 1 : 7;

    const outcomeCard = document.getElementById('tm-outcome-card');
    const badgeEl = document.getElementById('tm-outcome-badge');
    const titleEl = document.getElementById('tm-outcome-title');
    const msgEl = document.getElementById('tm-outcome-message');
    const pnlEl = document.getElementById('tm-res-pnl');
    const btc7dEl = document.getElementById('tm-res-btc7d');
    const hitdayEl = document.getElementById('tm-res-hitday');
    const verdictEl = document.getElementById('tm-res-verdict');

    if (!outcomeCard) return;

    let badgeClass = 'pending';
    let badgeText = '⏳ PENDING';
    let titleText = 'Wynik weryfikacji 7D';
    let desc = '';

    if (outcome === 'TARGET_HIT') {
      badgeClass = 'tp-hit';
      badgeText = '🎯 TARGET HIT';
      titleText = 'Sukces Modelu: Osiągnięto Poziom Take-Profit!';
      desc = `Kurs Bitcoina osiągnął docelowy poziom TP ($${Number(res.resolvedPrice || 0).toLocaleString()}) w dniu ${hitDay}/7 od momentu wygenerowania sygnału.`;
    } else if (outcome === 'INVALIDATED') {
      badgeClass = 'sl-hit';
      badgeText = '🛑 SL HIT';
      titleText = 'Unieważnienie: Naruszenie Poziomu Stop-Loss';
      desc = `Rynek wybił przeciwny poziom unieważnienia ($${Number(res.resolvedPrice || 0).toLocaleString()}) w dniu ${hitDay}/7. Strata ograniczona do założonego buforu.`;
    } else if (outcome === 'EXPIRED_PROFIT') {
      badgeClass = 'exp-profit';
      badgeText = '⏱️ EXPIRED (+%)';
      titleText = 'Zamknięcie na plusie po upływie 7 dni';
      desc = `Po 7 dniach żaden skrajny poziom nie został wybity, ale pozycja zamknęła się z dodatnim zyskiem.`;
    } else if (outcome === 'EXPIRED_LOSS') {
      badgeClass = 'exp-loss';
      badgeText = '⏱️ EXPIRED (-%)';
      titleText = 'Zamknięcie na minusie po upływie 7 dni';
      desc = `Po 7 dniach kurs nie wybił TP ani SL, zamykając się z niewielkim ujemnym wynikiem.`;
    }

    if (badgeEl) {
      badgeEl.className = `outcome-badge ${badgeClass}`;
      badgeEl.textContent = badgeText;
    }
    if (titleEl) titleEl.textContent = titleText;
    if (msgEl) msgEl.textContent = desc;

    if (pnlEl) {
      const isPos = res.realizedReturnPercent != null && res.realizedReturnPercent > 0;
      const isNeg = res.realizedReturnPercent != null && res.realizedReturnPercent < 0;
      pnlEl.textContent = (isPos ? '+' : '') + retPct + '%';
      pnlEl.style.color = isPos ? '#10b981' : isNeg ? '#f43f5e' : 'var(--text-main)';
    }

    if (btc7dEl) {
      const btcRet = data.actualHistoricalOutcome && data.actualHistoricalOutcome.forwardReturn7d != null
        ? (data.actualHistoricalOutcome.forwardReturn7d * 100).toFixed(1)
        : '--';
      btc7dEl.textContent = (Number(btcRet) > 0 ? '+' : '') + btcRet + '%';
      btc7dEl.style.color = Number(btcRet) > 0 ? '#10b981' : Number(btcRet) < 0 ? '#f43f5e' : 'var(--text-main)';
    }

    if (hitdayEl) {
      hitdayEl.textContent = `Dzień ${hitDay} z 7 (${res.resolvedDate || '--'})`;
    }

    if (verdictEl) {
      const wasAcc = (data.prediction.signal === 'BULLISH_OPPORTUNITY' && res.outcomeStatus === 'TARGET_HIT') ||
                     (data.prediction.signal === 'BEARISH_RISK' && res.outcomeStatus === 'TARGET_HIT');
      verdictEl.textContent = wasAcc ? 'Trafny kierunek (Zwycięstwo)' : (outcome === 'INVALIDATED' ? 'Nietrafny (Zadziałał Stop-Loss)' : 'Ruch neutralny');
      verdictEl.style.color = wasAcc ? '#10b981' : (outcome === 'INVALIDATED' ? '#f43f5e' : '#fbbf24');
    }

    outcomeCard.style.display = 'block';
  }

  function resetTimeMachine() {
    if (!currentTimeMachineData || !currentTimeMachineData.historicalCandles) return;
    if (tmPlaybackInterval) {
      clearInterval(tmPlaybackInterval);
      tmPlaybackInterval = null;
    }

    const histData = currentTimeMachineData.historicalCandles.map(c => ({
      time: parseChartTime(c.time),
      open: Number(c.open),
      high: Number(c.high),
      low: Number(c.low),
      close: Number(c.close)
    }));

    if (tmCandleSeries) {
      tmCandleSeries.setData(histData);
    }
    if (tmChart) {
      tmChart.timeScale().fitContent();
    }

    const outcomeCard = document.getElementById('tm-outcome-card');
    if (outcomeCard) outcomeCard.style.display = 'none';

    const btnPlay = document.getElementById('btn-tm-play');
    if (btnPlay) btnPlay.disabled = false;

    const statusText = document.getElementById('tm-playback-status');
    if (statusText) statusText.textContent = 'Zresetowano do dnia T. Kliknij ▶ Odtwórz, aby ponownie odkryć przyszłość.';
  }

  // Application Entry Point
  window.addEventListener('DOMContentLoaded', async () => {
    initCharts();
    setupEvents();
    setupChartResizers();

    await loadChartData(currentDays);
    await loadTodayPrediction();
    await loadHistoricalPredictions();

    // Auto-run initial backtest metrics for display
    runBacktest();

    // Priority 15: Initialize Time Machine
    initTimeMachine();
  });

})();
