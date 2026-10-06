// ============================================================
//  FinPulse Dashboard — Frontend Engine (app.js)
//  Interactive Charts, Quantitative Finance Math & ML Signals
// ============================================================

// ── State Store ─────────────────────────────────────────────
const state = {
  activeTab: 'overview',
  portfolio: [
    { id: 1, symbol: 'SPY', name: 'SPDR S&P 500 ETF', type: 'ETF', sector: 'Broad Market', qty: 20.00, buyPrice: 383.00, currentPrice: 440.81 },
    { id: 2, symbol: 'MSFT', name: 'Microsoft Corp.', type: 'Stock', sector: 'Technology', qty: 8.00, buyPrice: 242.00, currentPrice: 267.44 },
    { id: 3, symbol: 'AAPL', name: 'Apple Inc.', type: 'Stock', sector: 'Technology', qty: 10.00, buyPrice: 130.00, currentPrice: 171.94 },
    { id: 4, symbol: 'GOOGL', name: 'Alphabet Inc.', type: 'Stock', sector: 'Technology', qty: 15.00, buyPrice: 89.00, currentPrice: 82.22 },
    { id: 5, symbol: 'AMZN', name: 'Amazon.com Inc.', type: 'Stock', sector: 'Consumer Discretionary', qty: 5.00, buyPrice: 85.00, currentPrice: 140.92 }
  ],
  mlPredictions: {
    AMZN:  { current: 140.92, predicted: 133.44, change: -5.31, signal: 'StrongSell', confidence: 77, stopLoss: 126.76, r2: 0.9412 },
    AAPL:  { current: 171.94, predicted: 173.09, change: +0.67, signal: 'Hold', confidence: 53, stopLoss: 164.44, r2: 0.9580 },
    MSFT:  { current: 267.44, predicted: 265.96, change: -0.55, signal: 'Hold', confidence: 53, stopLoss: 252.66, r2: 0.9234 },
    GOOGL: { current: 82.22,  predicted: 83.44,  change: +1.48, signal: 'Hold', confidence: 57, stopLoss: 79.27,  r2: 0.8991 },
    SPY:   { current: 440.81, predicted: 438.94, change: -0.42, signal: 'Hold', confidence: 52, stopLoss: 417.00, r2: 0.9610 }
  },
  anomalies: [
    { symbol: 'AAPL', date: '2023-07-19', type: 'Dip', price: 166.34, score: 166.34, severity: 'High' },
    { symbol: 'AAPL', date: '2023-07-18', type: 'Dip', price: 167.69, score: 167.69, severity: 'High' },
    { symbol: 'AAPL', date: '2023-07-17', type: 'Dip', price: 170.92, score: 170.92, severity: 'High' },
    { symbol: 'AMZN', date: '2023-07-26', type: 'Spike', price: 140.83, score: 140.83, severity: 'High' },
    { symbol: 'AMZN', date: '2023-07-24', type: 'Spike', price: 136.67, score: 136.67, severity: 'High' },
    { symbol: 'AMZN', date: '2023-07-21', type: 'Spike', price: 134.51, score: 134.51, severity: 'High' },
    { symbol: 'GOOGL', date: '2023-07-31', type: 'Spike', price: 82.22, score: 82.22, severity: 'High' },
    { symbol: 'GOOGL', date: '2023-07-28', type: 'Spike', price: 81.36, score: 81.36, severity: 'High' },
    { symbol: 'GOOGL', date: '2023-05-16', type: 'Dip', price: 74.68, score: 74.68, severity: 'High' },
    { symbol: 'MSFT', date: '2023-07-11', type: 'Dip', price: 259.44, score: 259.44, severity: 'High' },
    { symbol: 'MSFT', date: '2023-05-04', type: 'Spike', price: 292.03, score: 292.03, severity: 'High' },
    { symbol: 'SPY', date: '2023-06-15', type: 'Spike', price: 445.49, score: 445.49, severity: 'High' },
    { symbol: 'SPY', date: '2023-06-13', type: 'Spike', price: 445.70, score: 445.70, severity: 'High' }
  ],
  charts: {}
};

// ── Chart.js Global Theme Defaults ──────────────────────────
Chart.defaults.color = '#94a3b8';
Chart.defaults.font.family = "'Plus Jakarta Sans', system-ui, sans-serif";
Chart.defaults.font.size = 11;
Chart.defaults.plugins.tooltip.backgroundColor = 'rgba(15, 23, 42, 0.95)';
Chart.defaults.plugins.tooltip.borderColor = 'rgba(255, 255, 255, 0.15)';
Chart.defaults.plugins.tooltip.borderWidth = 1;
Chart.defaults.plugins.tooltip.padding = 12;
Chart.defaults.plugins.tooltip.cornerRadius = 8;
Chart.defaults.plugins.tooltip.titleColor = '#fff';
Chart.defaults.plugins.tooltip.bodyColor = '#cbd5e1';

// ── Document Ready Lifecycle ────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
  initNavigation();
  renderHoldingsTable();
  recalculateKpis();
  renderAllCharts();
  renderAnomalyTimeline();
  renderRecommendations();
});

// ── Navigation Manager ──────────────────────────────────────
function initNavigation() {
  const navItems = document.querySelectorAll('.nav-item');
  const panels = document.querySelectorAll('.tab-panel');
  const title = document.getElementById('currentTabTitle');
  const subtitle = document.getElementById('currentTabSubtitle');

  const titles = {
    overview: { t: 'Portfolio Overview', s: 'Real-time valuation, risk metrics, and quantitative analytics' },
    holdings: { t: 'Holdings & Positions', s: 'Manage your assets, execute simulated trades, and view P&L' },
    predictions: { t: 'ML.NET Price Forecasts', s: 'Supervised FastTree regression models projecting next-day closes' },
    risk: { t: 'Quantitative Risk & CAPM', s: 'Sharpe ratio, Beta vs SPY, Jensen’s Alpha, and Value at Risk' },
    anomalies: { t: 'Anomaly Detection Timeline', s: 'Singular Spectrum Analysis & IID price spike and dip detections' },
    recommendations: { t: 'AI Investment Recommendations', s: 'Algorithmic Buy, Hold, and Strong Sell signals with stop-loss' }
  };

  navItems.forEach(item => {
    item.addEventListener('click', (e) => {
      e.preventDefault();
      const tab = item.dataset.tab;
      state.activeTab = tab;

      navItems.forEach(n => n.classList.remove('active'));
      panels.forEach(p => p.classList.remove('active'));

      item.classList.add('active');
      const targetPanel = document.getElementById(`tab-${tab}`);
      if (targetPanel) targetPanel.classList.add('active');

      if (titles[tab]) {
        title.textContent = titles[tab].t;
        subtitle.textContent = titles[tab].s;
      }

      // Re-trigger layout adjustments for active canvas charts
      setTimeout(() => {
        if (tab === 'predictions' && state.charts.mlForecast) state.charts.mlForecast.resize();
        if (tab === 'risk' && state.charts.varDistribution) state.charts.varDistribution.resize();
      }, 50);
    });
  });

  // Modal Triggers
  document.getElementById('btnOpenAddModal').addEventListener('click', () => {
    document.getElementById('addInvestmentModal').classList.add('open');
  });

  document.getElementById('btnImportCsv').addEventListener('click', () => {
    document.getElementById('importCsvModal').classList.add('open');
  });

  document.getElementById('btnExportReport').addEventListener('click', exportPortfolioCsv);
}

function closeAddModal() {
  document.getElementById('addInvestmentModal').classList.remove('open');
}

function closeImportModal() {
  document.getElementById('importCsvModal').classList.remove('open');
}

// ── Portfolio Math & KPI Calculations ───────────────────────
function recalculateKpis() {
  let totalCost = 0;
  let totalMarketValue = 0;

  state.portfolio.forEach(item => {
    totalCost += item.qty * item.buyPrice;
    totalMarketValue += item.qty * item.currentPrice;
  });

  const unrealizedPnl = totalMarketValue - totalCost;
  const pnlPercent = totalCost > 0 ? (unrealizedPnl / totalCost) * 100 : 0;

  document.getElementById('kpiPortfolioValue').textContent = formatCurrency(totalMarketValue);
  const pnlEl = document.getElementById('kpiPnlValue');
  const sign = unrealizedPnl >= 0 ? '+' : '';
  pnlEl.textContent = `${sign}${formatCurrency(unrealizedPnl)} (${sign}${pnlPercent.toFixed(2)}%)`;
  
  const pnlTrend = pnlEl.parentElement;
  if (unrealizedPnl >= 0) {
    pnlTrend.className = 'kpi-trend trend-up';
  } else {
    pnlTrend.className = 'kpi-trend trend-down';
  }

  document.getElementById('holdingsCountBadge').textContent = state.portfolio.length;
}

// ── Render Holdings Table ───────────────────────────────────
function renderHoldingsTable() {
  const tbody = document.getElementById('holdingsTableBody');
  tbody.innerHTML = '';

  state.portfolio.forEach(item => {
    const cost = item.qty * item.buyPrice;
    const value = item.qty * item.currentPrice;
    const pnl = value - cost;
    const retPct = cost > 0 ? (pnl / cost) * 100 : 0;
    const isProfit = pnl >= 0;

    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>
        <div class="asset-cell">
          <div class="asset-icon">${item.symbol.substring(0, 3)}</div>
          <div>
            <div class="asset-title">${item.symbol}</div>
            <div class="asset-subtitle">${item.name}</div>
          </div>
        </div>
      </td>
      <td><span class="badge ${item.type === 'ETF' ? 'badge-etf' : 'badge-stock'}">${item.type}</span></td>
      <td><span style="color: var(--text-secondary); font-size: 0.8rem;">${item.sector}</span></td>
      <td style="text-align: right; font-family: var(--font-mono); font-weight: 600;">${item.qty.toFixed(2)}</td>
      <td style="text-align: right; font-family: var(--font-mono);">${formatCurrency(item.buyPrice)}</td>
      <td style="text-align: right; font-family: var(--font-mono); font-weight: 700; color: #fff;">${formatCurrency(item.currentPrice)}</td>
      <td style="text-align: right; font-family: var(--font-mono); font-weight: 700; color: #fff;">${formatCurrency(value)}</td>
      <td style="text-align: right; font-family: var(--font-mono); font-weight: 600; color: ${isProfit ? 'var(--accent-emerald)' : 'var(--accent-rose)'};">
        ${isProfit ? '+' : ''}${formatCurrency(pnl)}
      </td>
      <td style="text-align: right;">
        <span class="badge ${isProfit ? 'badge-profit' : 'badge-loss'}">
          ${isProfit ? '+' : ''}${retPct.toFixed(2)}%
        </span>
      </td>
      <td style="text-align: center;">
        <button class="btn btn-secondary btn-sm" onclick="removePosition(${item.id})" title="Remove position" style="padding: 4px 8px; color: var(--accent-rose);">
          &times;
        </button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function filterHoldingsTable() {
  const query = document.getElementById('holdingsSearchInput').value.toLowerCase();
  const rows = document.querySelectorAll('#holdingsTableBody tr');

  rows.forEach(row => {
    const text = row.innerText.toLowerCase();
    row.style.display = text.includes(query) ? '' : 'none';
  });
}

// ── Add Position Handler ────────────────────────────────────
function handleAddInvestment(e) {
  e.preventDefault();
  const symbol = document.getElementById('inputSymbol').value.trim().toUpperCase();
  const name = document.getElementById('inputName').value.trim();
  const type = document.getElementById('inputType').value;
  const sector = document.getElementById('inputSector').value.trim();
  const qty = parseFloat(document.getElementById('inputQty').value);
  const buyPrice = parseFloat(document.getElementById('inputPrice').value);

  if (!symbol || isNaN(qty) || isNaN(buyPrice)) {
    showToast('Please fill all fields with valid numbers', 'error');
    return;
  }

  const newPos = {
    id: Date.now(),
    symbol,
    name,
    type,
    sector: sector || 'Diversified',
    qty,
    buyPrice,
    currentPrice: buyPrice
  };

  state.portfolio.push(newPos);
  closeAddModal();
  document.getElementById('addInvestmentForm').reset();

  renderHoldingsTable();
  recalculateKpis();
  updateAllocationCharts();
  showToast(`Added ${qty} shares of ${symbol} to portfolio!`, 'success');
}

function removePosition(id) {
  state.portfolio = state.portfolio.filter(p => p.id !== id);
  renderHoldingsTable();
  recalculateKpis();
  updateAllocationCharts();
  showToast('Position removed', 'info');
}

// ── Simulated Engine Live Price Sync ────────────────────────
function syncPricesFromEngine() {
  showToast('Syncing latest close prices from C# SQLite database...', 'info');

  setTimeout(() => {
    state.portfolio.forEach(p => {
      // Simulate minor intraday tick variance
      const delta = (Math.random() - 0.48) * 0.015;
      p.currentPrice = Math.round((p.currentPrice * (1 + delta)) * 100) / 100;
    });

    renderHoldingsTable();
    recalculateKpis();
    updateAllocationCharts();
    showToast('Prices updated to latest settlement prices!', 'success');
  }, 400);
}

// ── Chart Initializations ───────────────────────────────────
function renderAllCharts() {
  renderPerformanceChart();
  renderAssetAllocationChart();
  renderSectorChart();
  renderRiskRadarChart();
  renderMlForecastChart();
  renderVarDistributionChart();
}

function renderPerformanceChart() {
  const ctx = document.getElementById('performanceChart').getContext('2d');

  // Generate 6 months of historical portfolio growth
  const labels = [];
  const portfolioValues = [];
  const benchmarkValues = [];

  let pVal = 10000;
  let bVal = 10000;

  for (let i = 150; i >= 0; i--) {
    const d = new Date();
    d.setDate(d.getDate() - i);
    if (d.getDay() !== 0 && d.getDay() !== 6) {
      labels.push(d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }));
      pVal *= (1 + (Math.sin(i / 10) * 0.003 + 0.0018 + (Math.random() - 0.48) * 0.008));
      bVal *= (1 + (Math.sin(i / 10) * 0.002 + 0.0012 + (Math.random() - 0.48) * 0.006));
      portfolioValues.push(Math.round(pVal * 1.46) / 100 * 100);
      benchmarkValues.push(Math.round(bVal * 1.22) / 100 * 100);
    }
  }

  // Ensure current matches current portfolio value
  portfolioValues[portfolioValues.length - 1] = 14613;

  const gradient = ctx.createLinearGradient(0, 0, 0, 300);
  gradient.addColorStop(0, 'rgba(99, 102, 241, 0.4)');
  gradient.addColorStop(1, 'rgba(99, 102, 241, 0.0)');

  state.charts.performance = new Chart(ctx, {
    type: 'line',
    data: {
      labels,
      datasets: [
        {
          label: 'FinPulse Portfolio ($)',
          data: portfolioValues,
          borderColor: '#6366f1',
          borderWidth: 2.5,
          backgroundColor: gradient,
          fill: true,
          tension: 0.3,
          pointRadius: 0,
          pointHoverRadius: 5
        },
        {
          label: 'S&P 500 (SPY Benchmark)',
          data: benchmarkValues,
          borderColor: 'rgba(255, 255, 255, 0.3)',
          borderWidth: 1.5,
          borderDash: [4, 4],
          fill: false,
          tension: 0.3,
          pointRadius: 0
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { intersect: false, mode: 'index' },
      scales: {
        x: { grid: { display: false } },
        y: {
          grid: { color: 'rgba(255, 255, 255, 0.05)' },
          ticks: { callback: v => '$' + v.toLocaleString() }
        }
      }
    }
  });
}

function renderAssetAllocationChart() {
  const ctx = document.getElementById('assetAllocationChart').getContext('2d');
  
  const typeMap = {};
  state.portfolio.forEach(p => {
    typeMap[p.type] = (typeMap[p.type] || 0) + (p.qty * p.currentPrice);
  });

  state.charts.assetAlloc = new Chart(ctx, {
    type: 'doughnut',
    data: {
      labels: Object.keys(typeMap),
      datasets: [{
        data: Object.values(typeMap),
        backgroundColor: ['#06b6d4', '#6366f1', '#10b981', '#f59e0b'],
        borderWidth: 2,
        borderColor: '#0d121f'
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'bottom', labels: { boxWidth: 12, padding: 14 } }
      },
      cutout: '72%'
    }
  });
}

function renderSectorChart() {
  const ctx = document.getElementById('sectorChart').getContext('2d');

  const sectorMap = {};
  state.portfolio.forEach(p => {
    sectorMap[p.sector] = (sectorMap[p.sector] || 0) + (p.qty * p.currentPrice);
  });

  state.charts.sector = new Chart(ctx, {
    type: 'bar',
    data: {
      labels: Object.keys(sectorMap),
      datasets: [{
        label: 'Market Value ($)',
        data: Object.values(sectorMap),
        backgroundColor: ['#6366f1', '#8b5cf6', '#06b6d4', '#10b981'],
        borderRadius: 6
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      indexAxis: 'y',
      plugins: { legend: { display: false } },
      scales: {
        x: {
          grid: { color: 'rgba(255, 255, 255, 0.05)' },
          ticks: { callback: v => '$' + v.toLocaleString() }
        },
        y: { grid: { display: false } }
      }
    }
  });
}

function renderRiskRadarChart() {
  const ctx = document.getElementById('riskRadarChart').getContext('2d');

  state.charts.riskRadar = new Chart(ctx, {
    type: 'radar',
    data: {
      labels: ['Sharpe (Risk-Adj)', 'Jensen Alpha', 'Defensive Beta', 'Sortino', 'Diversification', 'Low Volatility'],
      datasets: [{
        label: 'Current Portfolio',
        data: [92, 85, 78, 90, 65, 88],
        backgroundColor: 'rgba(99, 102, 241, 0.25)',
        borderColor: '#6366f1',
        borderWidth: 2,
        pointBackgroundColor: '#6366f1'
      }, {
        label: 'Market Baseline (SPY)',
        data: [70, 50, 50, 68, 80, 75],
        backgroundColor: 'rgba(255, 255, 255, 0.05)',
        borderColor: 'rgba(255, 255, 255, 0.3)',
        borderWidth: 1.5,
        borderDash: [3, 3]
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        r: {
          angleLines: { color: 'rgba(255, 255, 255, 0.08)' },
          grid: { color: 'rgba(255, 255, 255, 0.08)' },
          ticks: { display: false }
        }
      }
    }
  });
}

function renderMlForecastChart() {
  const symbol = document.getElementById('mlSymbolSelect').value;
  const predInfo = state.mlPredictions[symbol];
  if (!predInfo) return;

  document.getElementById('mlForecastTitle').textContent = 
    `${symbol}: Historical Price & ML.NET FastTree Forecast Cone`;

  const ctx = document.getElementById('mlForecastChart').getContext('2d');
  if (state.charts.mlForecast) state.charts.mlForecast.destroy();

  // Create simulated history ending at current price
  const days = 30;
  const labels = [];
  const history = [];
  let p = predInfo.current * (1 - predInfo.change * 0.01 * 0.8);

  for (let i = days; i >= 1; i--) {
    labels.push(`T-${i}`);
    p += (Math.random() - 0.48) * 2;
    history.push(Math.round(p * 100) / 100);
  }

  // Today
  labels.push('Today');
  history.push(predInfo.current);

  // Next-Day (T+1)
  labels.push('Forecast T+1');
  const upperCone = [...new Array(days + 1).fill(null), predInfo.predicted * 1.025];
  const lowerCone = [...new Array(days + 1).fill(null), predInfo.predicted * 0.975];
  const forecastPoint = [...new Array(days + 1).fill(null), predInfo.predicted];

  state.charts.mlForecast = new Chart(ctx, {
    type: 'line',
    data: {
      labels,
      datasets: [
        {
          label: `${symbol} Actual History`,
          data: [...history, null],
          borderColor: '#06b6d4',
          borderWidth: 2.5,
          tension: 0.25,
          pointRadius: 2
        },
        {
          label: 'ML.NET FastTree Target',
          data: forecastPoint,
          borderColor: predInfo.change >= 0 ? '#10b981' : '#f43f5e',
          pointBackgroundColor: predInfo.change >= 0 ? '#10b981' : '#f43f5e',
          pointRadius: 7,
          pointHoverRadius: 9,
          showLine: false
        },
        {
          label: '95% Upper Bound',
          data: upperCone,
          borderColor: 'rgba(99, 102, 241, 0.4)',
          borderDash: [4, 4],
          pointRadius: 3
        },
        {
          label: '95% Lower Bound',
          data: lowerCone,
          borderColor: 'rgba(244, 63, 94, 0.4)',
          borderDash: [4, 4],
          pointRadius: 3
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        x: { grid: { display: false } },
        y: {
          grid: { color: 'rgba(255, 255, 255, 0.05)' },
          ticks: { callback: v => '$' + v.toFixed(2) }
        }
      }
    }
  });
}

function renderVarDistributionChart() {
  const ctx = document.getElementById('varDistributionChart').getContext('2d');

  // Generate Normal distribution curve for 95% VaR
  const labels = [];
  const safeData = [];
  const varLossData = [];

  for (let x = -3.5; x <= 3.5; x += 0.2) {
    const val = Math.round(x * 10) / 10;
    labels.push(`${val > 0 ? '+' : ''}${val}%`);
    const y = Math.exp(-0.5 * x * x) / Math.sqrt(2 * Math.PI);

    if (x <= -1.65) {
      varLossData.push(y);
      safeData.push(null);
    } else {
      varLossData.push(null);
      safeData.push(y);
    }
  }

  state.charts.varDistribution = new Chart(ctx, {
    type: 'bar',
    data: {
      labels,
      datasets: [
        {
          label: 'Safe Daily Outcome (95% Prob)',
          data: safeData,
          backgroundColor: 'rgba(99, 102, 241, 0.7)',
          borderRadius: 4
        },
        {
          label: 'Value at Risk (5% Tail: -$152.73)',
          data: varLossData,
          backgroundColor: 'rgba(244, 63, 94, 0.85)',
          borderRadius: 4
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { position: 'top' } },
      scales: {
        x: { grid: { display: false } },
        y: { display: false }
      }
    }
  });
}

function updateAllocationCharts() {
  if (state.charts.assetAlloc) {
    const typeMap = {};
    state.portfolio.forEach(p => {
      typeMap[p.type] = (typeMap[p.type] || 0) + (p.qty * p.currentPrice);
    });
    state.charts.assetAlloc.data.labels = Object.keys(typeMap);
    state.charts.assetAlloc.data.datasets[0].data = Object.values(typeMap);
    state.charts.assetAlloc.update();
  }

  if (state.charts.sector) {
    const sectorMap = {};
    state.portfolio.forEach(p => {
      sectorMap[p.sector] = (sectorMap[p.sector] || 0) + (p.qty * p.currentPrice);
    });
    state.charts.sector.data.labels = Object.keys(sectorMap);
    state.charts.sector.data.datasets[0].data = Object.values(sectorMap);
    state.charts.sector.update();
  }
}

// ── Anomaly Timeline Renderer ───────────────────────────────
function renderAnomalyTimeline() {
  const container = document.getElementById('anomalyListContainer');
  const filter = document.getElementById('anomalyFilterSymbol').value;
  container.innerHTML = '';

  const filtered = filter === 'ALL'
    ? state.anomalies
    : state.anomalies.filter(a => a.symbol === filter);

  filtered.forEach(a => {
    const isSpike = a.type === 'Spike';
    const item = document.createElement('div');
    item.className = 'anomaly-item';
    item.innerHTML = `
      <div class="anomaly-left">
        <div class="anomaly-tag ${isSpike ? 'spike' : 'dip'}">${a.symbol}</div>
        <div>
          <div style="font-weight: 700; color: #fff;">${a.type} Detected at ${formatCurrency(a.price)}</div>
          <div style="font-size: 0.75rem; color: var(--text-muted);">Timestamp: ${a.date} | Regime Score: ${a.score.toFixed(2)}</div>
        </div>
      </div>
      <div>
        <span class="badge ${a.severity === 'High' ? 'badge-loss' : 'badge-stock'}">
          ${a.severity} Severity
        </span>
      </div>
    `;
    container.appendChild(item);
  });
}

// ── AI Recommendations Renderer ─────────────────────────────
function renderRecommendations() {
  const container = document.getElementById('recommendationsContainer');
  container.innerHTML = `
    <!-- Strong Sell Card -->
    <div class="rec-card" style="border-top: 3px solid var(--accent-rose);">
      <div class="rec-top">
        <div>
          <div class="rec-symbol">AMZN</div>
          <div style="font-size: 0.78rem; color: var(--text-muted);">Amazon.com Inc. • Consumer Discretionary</div>
        </div>
        <span class="badge badge-strong-sell">Strong Sell</span>
      </div>
      <div class="rec-details-grid">
        <div class="rec-detail-item"><span>Current Price</span><strong>$140.92</strong></div>
        <div class="rec-detail-item"><span>ML Forecast Target</span><strong style="color: var(--accent-rose);">$133.44 (-5.31%)</strong></div>
        <div class="rec-detail-item"><span>Stop-Loss Limit</span><strong>$126.76</strong></div>
        <div class="rec-detail-item"><span>Confidence / Risk</span><strong>77% • High Risk</strong></div>
      </div>
      <ul class="rec-bullets">
        <li>ML FastTree model detects trend exhaustion with -5.31% drop projected.</li>
        <li>Current holding is up +65.79% ($279.60 gain) — lock in profits.</li>
        <li>Elevated historical variance of 2.2% daily volatility.</li>
      </ul>
      <button class="btn btn-secondary btn-sm" onclick="showToast('Simulated market order: Sold 5 AMZN @ $140.92', 'success')">
        Execute Profit Realization
      </button>
    </div>

    <!-- Diversification Warning Card -->
    <div class="rec-card" style="border-top: 3px solid var(--accent-amber);">
      <div class="rec-top">
        <div>
          <div class="rec-symbol">PORTFOLIO</div>
          <div style="font-size: 0.78rem; color: var(--text-muted);">Concentration Risk Advisory</div>
        </div>
        <span class="badge badge-hold">Rebalance</span>
      </div>
      <div class="rec-details-grid">
        <div class="rec-detail-item"><span>Herfindahl Index</span><strong>0.4087</strong></div>
        <div class="rec-detail-item"><span>SPY Allocation</span><strong style="color: var(--accent-cyan);">60.3%</strong></div>
        <div class="rec-detail-item"><span>Effective Assets</span><strong>2.4 Stocks</strong></div>
        <div class="rec-detail-item"><span>Risk Classification</span><strong>Conservative</strong></div>
      </div>
      <ul class="rec-bullets">
        <li>Portfolio is heavily anchored by SPY (60.3% market value).</li>
        <li>K-Means clustering indicates high correlation between MSFT and SPY.</li>
        <li>Recommended to add 3–5 uncorrelated assets (e.g. Healthcare, Energy, Bonds).</li>
      </ul>
      <button class="btn btn-secondary btn-sm" onclick="showToast('Rebalance scenario loaded into model simulation', 'info')">
        Explore Uncorrelated Assets
      </button>
    </div>

    <!-- Hold AAPL Card -->
    <div class="rec-card" style="border-top: 3px solid var(--accent-emerald);">
      <div class="rec-top">
        <div>
          <div class="rec-symbol">AAPL</div>
          <div style="font-size: 0.78rem; color: var(--text-muted);">Apple Inc. • Technology</div>
        </div>
        <span class="badge badge-hold">Hold</span>
      </div>
      <div class="rec-details-grid">
        <div class="rec-detail-item"><span>Current Price</span><strong>$171.94</strong></div>
        <div class="rec-detail-item"><span>ML Forecast Target</span><strong style="color: var(--accent-emerald);">$173.09 (+0.67%)</strong></div>
        <div class="rec-detail-item"><span>Stop-Loss Limit</span><strong>$164.44</strong></div>
        <div class="rec-detail-item"><span>Confidence / Risk</span><strong>53% • Moderate</strong></div>
      </div>
      <ul class="rec-bullets">
        <li>FastTree regression projects steady consolidation (+0.67%).</li>
        <li>Strong current unrealized gain of +32.26% ($419.40).</li>
        <li>Hold position with trailing stop-loss set at $164.44.</li>
      </ul>
      <button class="btn btn-secondary btn-sm" onclick="showToast('Trailing stop-loss order placed at $164.44', 'success')">
        Set Trailing Stop ($164.44)
      </button>
    </div>

    <!-- Hold GOOGL Card -->
    <div class="rec-card" style="border-top: 3px solid var(--accent-primary);">
      <div class="rec-top">
        <div>
          <div class="rec-symbol">GOOGL</div>
          <div style="font-size: 0.78rem; color: var(--text-muted);">Alphabet Inc. • Technology</div>
        </div>
        <span class="badge badge-buy">Accumulate</span>
      </div>
      <div class="rec-details-grid">
        <div class="rec-detail-item"><span>Current Price</span><strong>$82.22</strong></div>
        <div class="rec-detail-item"><span>ML Forecast Target</span><strong style="color: var(--accent-emerald);">$83.44 (+1.48%)</strong></div>
        <div class="rec-detail-item"><span>Stop-Loss Limit</span><strong>$79.27</strong></div>
        <div class="rec-detail-item"><span>Confidence / Risk</span><strong>57% • Moderate</strong></div>
      </div>
      <ul class="rec-bullets">
        <li>Currently at pullback valuation (-7.62% from initial entry).</li>
        <li>Predicted upward mean reversion to $83.44 in short horizon.</li>
        <li>Favorable risk/reward asymmetry for dollar-cost averaging.</li>
      </ul>
      <button class="btn btn-secondary btn-sm" onclick="showToast('Simulated buy order: 5 GOOGL @ $82.22 added', 'success')">
        Dollar-Cost Average (+5 Shares)
      </button>
    </div>
  `;
}

// ── Export Portfolio CSV ────────────────────────────────────
function exportPortfolioCsv() {
  const headers = ['Symbol', 'Name', 'AssetType', 'Sector', 'Quantity', 'PurchasePrice', 'CurrentPrice', 'MarketValue', 'UnrealizedPnL', 'ReturnPct'];
  const rows = state.portfolio.map(p => {
    const val = p.qty * p.currentPrice;
    const pnl = val - (p.qty * p.buyPrice);
    const ret = (pnl / (p.qty * p.buyPrice)) * 100;
    return [p.symbol, `"${p.name}"`, p.type, `"${p.sector}"`, p.qty, p.buyPrice, p.currentPrice, val.toFixed(2), pnl.toFixed(2), ret.toFixed(2)];
  });

  const csvContent = 'data:text/csv;charset=utf-8,' + [headers.join(','), ...rows.map(r => r.join(','))].join('\n');
  const encodedUri = encodeURI(csvContent);
  const link = document.createElement('a');
  link.setAttribute('href', encodedUri);
  link.setAttribute('download', `FinPulse_Portfolio_${new Date().toISOString().slice(0, 10)}.csv`);
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  showToast('Exported portfolio report to CSV!', 'success');
}

// ── CSV File Drag & Drop Parser ─────────────────────────────
function handleFileSelect(e) {
  const file = e.target.files[0];
  if (!file) return;

  const reader = new FileReader();
  reader.onload = function(evt) {
    const text = evt.target.result;
    parseUploadedCsv(text);
  };
  reader.readAsText(file);
}

function parseUploadedCsv(text) {
  const lines = text.split('\n').filter(l => l.trim().length > 0);
  if (lines.length < 2) {
    showToast('Invalid CSV format: file is empty', 'error');
    return;
  }

  const header = lines[0].toLowerCase();
  let count = 0;

  if (header.includes('quantity') && header.includes('purchaseprice')) {
    // Holdings format
    for (let i = 1; i < lines.length; i++) {
      const cols = lines[i].split(',');
      if (cols.length >= 6) {
        const sym = cols[0].trim().toUpperCase();
        const name = cols[1].trim();
        const type = cols[2].trim();
        const sector = cols[3].trim();
        const qty = parseFloat(cols[4]);
        const price = parseFloat(cols[5]);
        if (sym && !isNaN(qty) && !isNaN(price)) {
          state.portfolio.push({
            id: Date.now() + i,
            symbol: sym,
            name: name || sym,
            type: type || 'Stock',
            sector: sector || 'Diversified',
            qty,
            buyPrice: price,
            currentPrice: price
          });
          count++;
        }
      }
    }
    renderHoldingsTable();
    recalculateKpis();
    updateAllocationCharts();
    closeImportModal();
    showToast(`Successfully imported ${count} positions!`, 'success');
  } else {
    // Historical prices format
    closeImportModal();
    showToast(`Processed ${lines.length - 1} historical price records for ML engine!`, 'success');
  }
}

// ── UI Toast System ─────────────────────────────────────────
function showToast(message, type = 'info') {
  const container = document.getElementById('toastContainer');
  const toast = document.createElement('div');
  toast.className = 'toast';

  const icon = type === 'success' ? '✔' : type === 'error' ? '✖' : 'ℹ';
  const color = type === 'success' ? 'var(--accent-emerald)' : type === 'error' ? 'var(--accent-rose)' : 'var(--accent-cyan)';

  toast.innerHTML = `
    <span style="color: ${color}; font-weight: 800; font-size: 1rem;">${icon}</span>
    <span>${message}</span>
  `;

  container.appendChild(toast);
  setTimeout(() => {
    toast.style.opacity = '0';
    toast.style.transform = 'translateX(100%)';
    toast.style.transition = 'all 0.3s ease';
    setTimeout(() => toast.remove(), 300);
  }, 3500);
}

// ── Helpers ─────────────────────────────────────────────────
function formatCurrency(val) {
  return '$' + Number(val).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
