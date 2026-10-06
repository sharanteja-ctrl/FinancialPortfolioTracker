#!/usr/bin/env python3
"""
FinPulse Live Market Data Backend Server
Powered by yfinance and Python's http.server.
Fetches genuine real-time market quotes and historical financial series from NYSE / NASDAQ.
"""

import http.server
import socketserver
import urllib.parse
import json
import os
import sys
import time
import math
import sqlite3
from datetime import datetime
from concurrent.futures import ThreadPoolExecutor

import yfinance as yf
import pandas as pd
import numpy as np

PORT = 5173
WEB_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(WEB_DIR)
DATA_FILE = os.path.join(PROJECT_DIR, 'src', 'Data', 'historical_prices.csv')
DB_PATH = os.path.join(PROJECT_DIR, 'portfolio_tracker.db')

# In-memory cache to ensure sub-second response times
CACHE = {}
CACHE_TTL = 90  # seconds

def get_cached(key):
    if key in CACHE:
        ts, data = CACHE[key]
        if time.time() - ts < CACHE_TTL:
            return data
    return None

def set_cached(key, data):
    CACHE[key] = (time.time(), data)

def get_stock_quote(symbol):
    symbol = symbol.strip().upper()
    cache_key = f"quote:{symbol}"
    cached = get_cached(cache_key)
    if cached:
        return cached

    try:
        ticker = yf.Ticker(symbol)
        fast = ticker.fast_info
        price = getattr(fast, 'last_price', None)
        prev_close = getattr(fast, 'previous_close', None)
        currency = getattr(fast, 'currency', 'USD')
        mcap = getattr(fast, 'market_cap', None)
        high52 = getattr(fast, 'year_high', None)
        low52 = getattr(fast, 'year_low', None)

        if price is None:
            # Fallback to history
            hist = ticker.history(period='5d')
            if not hist.empty:
                price = float(hist['Close'].iloc[-1])
                prev_close = float(hist['Close'].iloc[-2]) if len(hist) > 1 else price

        if price is not None:
            prev = prev_close or price
            chg = price - prev
            chg_pct = (chg / prev * 100.0) if prev else 0.0

            # Get name from info or fallback
            name = symbol
            sector = 'Technology'
            qtype = 'Stock'
            try:
                info = ticker.info
                name = info.get('shortName') or info.get('longName') or symbol
                sector = info.get('sector') or ('Broad Market' if 'ETF' in info.get('quoteType', '') else 'Technology')
                qtype = 'ETF' if info.get('quoteType') == 'ETF' else 'Stock'
            except:
                pass

            data = {
                'symbol': symbol,
                'name': name,
                'type': qtype,
                'sector': sector,
                'currentPrice': round(float(price), 2),
                'previousClose': round(float(prev), 2),
                'change': round(float(chg), 2),
                'changePercent': round(float(chg_pct), 2),
                'currency': currency,
                'marketCap': mcap,
                'fiftyTwoWeekHigh': round(float(high52), 2) if high52 else None,
                'fiftyTwoWeekLow': round(float(low52), 2) if low52 else None,
                'timestamp': int(time.time())
            }
            set_cached(cache_key, data)
            return data
    except Exception as ex:
        print(f"[WARN] Quote failed for {symbol}: {ex}", file=sys.stderr)

    return None

def get_stock_history(symbol, period='1y'):
    symbol = symbol.strip().upper()
    cache_key = f"hist:{symbol}:{period}"
    cached = get_cached(cache_key)
    if cached:
        return cached

    try:
        ticker = yf.Ticker(symbol)
        df = ticker.history(period=period)
        if df.empty:
            return None

        records = []
        for idx, row in df.iterrows():
            date_str = idx.strftime('%Y-%m-%d')
            records.append({
                'date': date_str,
                'timestamp': int(idx.timestamp()),
                'open': round(float(row['Open']), 2),
                'high': round(float(row['High']), 2),
                'low': round(float(row['Low']), 2),
                'close': round(float(row['Close']), 2),
                'volume': int(row['Volume'])
            })

        data = {
            'symbol': symbol,
            'period': period,
            'records': records
        }
        set_cached(cache_key, data)
        return data
    except Exception as ex:
        print(f"[WARN] History failed for {symbol}: {ex}", file=sys.stderr)

    return None

def train_ml_regression(records):
    """
    Supervised Machine Learning regression for price forecasting.
    Uses technical indicators: MA5, MA20, RSI-14, Momentum, Volatility.
    """
    if not records or len(records) < 30:
        return None

    closes = [r['close'] for r in records]
    
    features = []
    labels = []

    for i in range(25, len(closes) - 1):
        c = closes[i]
        next_c = closes[i + 1]
        
        ma5 = sum(closes[i-4:i+1]) / 5.0
        ma20 = sum(closes[i-19:i+1]) / 20.0
        p_to_ma20 = (c / ma20) - 1.0
        momentum = (c / closes[i-10]) - 1.0
        
        rets = [(closes[k] - closes[k-1])/closes[k-1] for k in range(i-9, i+1)]
        vol = math.sqrt(sum(r*r for r in rets) / len(rets))
        
        features.append([1.0, c, ma5, ma20, p_to_ma20, momentum, vol])
        labels.append(next_c)

    if len(features) < 15:
        return None

    X = np.array(features, dtype=float)
    Y = np.array(labels, dtype=float)

    # Ridge Regression: beta = (X^T X + lambda I)^-1 X^T Y
    lambda_reg = 0.05
    xtx = X.T @ X + lambda_reg * np.eye(X.shape[1])
    xty = X.T @ Y
    try:
        weights = np.linalg.solve(xtx, xty)
    except:
        weights = np.linalg.lstsq(X, Y, rcond=None)[0]

    preds = X @ weights
    mae = float(np.mean(np.abs(preds - Y)))
    rmse = float(np.sqrt(np.mean((preds - Y)**2)))
    
    ss_tot = float(np.sum((Y - np.mean(Y))**2))
    ss_res = float(np.sum((Y - preds)**2))
    r2 = max(0.0, min(0.999, 1.0 - (ss_res / ss_tot) if ss_tot > 0 else 0.9))

    # Predict Next Day Close
    last_idx = len(closes) - 1
    last_c = closes[last_idx]
    last_ma5 = sum(closes[last_idx-4:last_idx+1]) / 5.0
    last_ma20 = sum(closes[last_idx-19:last_idx+1]) / 20.0
    last_p_to_ma20 = (last_c / last_ma20) - 1.0
    last_mom = (last_c / closes[last_idx-10]) - 1.0
    rets = [(closes[k] - closes[k-1])/closes[k-1] for k in range(last_idx-9, last_idx+1)]
    last_vol = math.sqrt(sum(r*r for r in rets) / len(rets))
    
    x_curr = np.array([1.0, last_c, last_ma5, last_ma20, last_p_to_ma20, last_mom, last_vol])
    next_pred = float(x_curr @ weights)
    change_pct = ((next_pred - last_c) / last_c) * 100.0

    if change_pct > 2.0:
        signal = 'StrongBuy'
        confidence = min(92, int(60 + abs(change_pct)*5))
    elif change_pct > 0.5:
        signal = 'Buy'
        confidence = min(80, int(50 + abs(change_pct)*5))
    elif change_pct < -2.0:
        signal = 'StrongSell'
        confidence = min(90, int(60 + abs(change_pct)*5))
    elif change_pct < -0.5:
        signal = 'Sell'
        confidence = min(75, int(50 + abs(change_pct)*5))
    else:
        signal = 'Hold'
        confidence = 55

    stop_loss = round(next_pred * 0.95, 2) if change_pct >= 0 else round(last_c * 0.95, 2)

    return {
        'currentPrice': round(last_c, 2),
        'predictedPrice': round(next_pred, 2),
        'predictedChangePct': round(change_pct, 2),
        'signal': signal,
        'confidence': confidence,
        'stopLoss': stop_loss,
        'r2': round(r2, 4),
        'rmse': round(rmse, 2),
        'mae': round(mae, 2),
        'historicalDays': len(closes)
    }

def detect_series_anomalies(records, symbol):
    if not records or len(records) < 25:
        return []

    anomalies = []
    window = 20
    closes = [r['close'] for r in records]

    for i in range(window, len(records)):
        slice_vals = closes[i-window:i]
        mean = sum(slice_vals) / window
        variance = sum((x - mean)**2 for x in slice_vals) / window
        std = math.sqrt(variance)
        if std <= 0.01:
            continue

        c = closes[i]
        z = (c - mean) / std
        if abs(z) >= 2.2:
            anomalies.append({
                'symbol': symbol,
                'date': records[i]['date'],
                'price': c,
                'score': round(abs(z), 2),
                'type': 'Spike' if z > 0 else 'Dip',
                'severity': 'High' if abs(z) > 3.0 else 'Medium'
            })

    return anomalies

def get_db():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn

def get_portfolio_investments():
    if not os.path.exists(DB_PATH):
        return []
    with get_db() as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT Id as id, PortfolioId as portfolioId, Symbol as symbol, Name as name, AssetType as type, Sector as sector, Quantity as qty, PurchasePrice as buyPrice, CurrentPrice as currentPrice, PurchaseDate as purchaseDate, LastUpdated as lastUpdated, Currency as currency, Notes as notes FROM Investments ORDER BY Id ASC")
        rows = [dict(r) for r in cursor.fetchall()]
        return rows

def add_portfolio_investment(data):
    with get_db() as conn:
        cursor = conn.cursor()
        now = datetime.utcnow().isoformat()
        cursor.execute("""
            INSERT INTO Investments (PortfolioId, Symbol, Name, AssetType, Sector, Quantity, PurchasePrice, CurrentPrice, PurchaseDate, LastUpdated, Currency, Notes)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            1,
            data.get('symbol', '').upper(),
            data.get('name') or data.get('symbol', '').upper(),
            data.get('type') or 'Stock',
            data.get('sector') or 'Diversified',
            float(data.get('qty') or data.get('quantity') or 0),
            float(data.get('buyPrice') or data.get('purchasePrice') or 0),
            float(data.get('currentPrice') or data.get('buyPrice') or 0),
            data.get('purchaseDate') or now[:10],
            now,
            data.get('currency', 'USD'),
            data.get('notes', '')
        ))
        conn.commit()
        return cursor.lastrowid

def remove_portfolio_investment(item_id):
    with get_db() as conn:
        cursor = conn.cursor()
        cursor.execute("DELETE FROM Investments WHERE Id = ?", (item_id,))
        conn.commit()
        return cursor.rowcount

def sync_realtime_to_sqlite():
    holdings = get_portfolio_investments()
    if not holdings:
        return {'success': False, 'error': 'No holdings found in database'}

    symbols = list(set([h['symbol'].upper() for h in holdings]))
    with ThreadPoolExecutor(max_workers=6) as pool:
        quotes = list(pool.map(get_stock_quote, symbols))

    quote_map = {q['symbol']: q for q in quotes if q}
    now = datetime.utcnow().isoformat()
    today_date = now[:10]

    with get_db() as conn:
        cursor = conn.cursor()
        total_market_val = 0.0
        total_cost_val = 0.0

        for h in holdings:
            sym = h['symbol']
            q = quote_map.get(sym)
            cur_price = q.get('currentPrice') if q else h['currentPrice']
            if cur_price:
                cursor.execute("UPDATE Investments SET CurrentPrice = ?, LastUpdated = ? WHERE Id = ?", (cur_price, now, h['id']))
                total_market_val += h['qty'] * cur_price
                total_cost_val += h['qty'] * h['buyPrice']

                prev = (q.get('previousClose') or cur_price) if q else cur_price
                daily_ret = ((cur_price - prev) / prev) if prev > 0 else 0.0
                cursor.execute("""
                    INSERT INTO StockPrices (Symbol, Date, Open, High, Low, Close, Volume, DailyReturn)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(Symbol, Date) DO UPDATE SET Close=excluded.Close, DailyReturn=excluded.DailyReturn
                """, (
                    sym, today_date,
                    prev,
                    cur_price,
                    cur_price,
                    cur_price,
                    100000.0,
                    daily_ret
                ))

        cursor.execute("""
            INSERT INTO PortfolioSnapshots (PortfolioId, Date, TotalValue, TotalCost, DailyReturn)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(PortfolioId, Date) DO UPDATE SET TotalValue=excluded.TotalValue, TotalCost=excluded.TotalCost
        """, (
            1, today_date, total_market_val, total_cost_val,
            ((total_market_val - total_cost_val) / total_cost_val) if total_cost_val > 0 else 0.0
        ))
        conn.commit()

    return {
        'success': True,
        'updatedCount': len(holdings),
        'totalMarketValue': round(total_market_val, 2),
        'totalCost': round(total_cost_val, 2),
        'timestamp': now
    }

class FinPulseApiHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=WEB_DIR, **kwargs)

    def end_headers(self):
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        super().end_headers()

    def do_OPTIONS(self):
        self.send_response(200)
        self.end_headers()

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path
        params = urllib.parse.parse_qs(parsed.query)

        if path == '/api/health':
            self._send_json({'status': 'ok', 'time': time.time(), 'db': os.path.exists(DB_PATH)})
            return

        elif path == '/api/portfolio':
            holdings = get_portfolio_investments()
            self._send_json({'holdings': holdings})
            return

        elif path == '/api/quote':
            symbol = params.get('symbol', ['AAPL'])[0].upper()
            quote = get_stock_quote(symbol)
            if quote:
                self._send_json(quote)
            else:
                self._send_json({'error': f'Symbol {symbol} not found'}, 404)
            return

        elif path == '/api/quotes':
            symbols = params.get('symbols', ['AAPL,MSFT,SPY'])[0].split(',')
            symbols = [s.strip().upper() for s in symbols if s.strip()]

            with ThreadPoolExecutor(max_workers=6) as pool:
                quotes = list(filter(None, pool.map(get_stock_quote, symbols)))

            self._send_json({'quotes': quotes})
            return

        elif path == '/api/history':
            symbol = params.get('symbol', ['AAPL'])[0].upper()
            period = params.get('range', ['1y'])[0]
            history = get_stock_history(symbol, period=period)
            if history:
                self._send_json(history)
            else:
                self._send_json({'error': f'No history for {symbol}'}, 404)
            return

        elif path == '/api/search':
            q = params.get('q', [''])[0].strip().upper()
            # Fast popular tickers match
            popular = [
                {'symbol': 'AAPL', 'name': 'Apple Inc.', 'type': 'Stock', 'sector': 'Technology'},
                {'symbol': 'MSFT', 'name': 'Microsoft Corp.', 'type': 'Stock', 'sector': 'Technology'},
                {'symbol': 'NVDA', 'name': 'NVIDIA Corporation', 'type': 'Stock', 'sector': 'Technology'},
                {'symbol': 'GOOGL', 'name': 'Alphabet Inc.', 'type': 'Stock', 'sector': 'Technology'},
                {'symbol': 'AMZN', 'name': 'Amazon.com Inc.', 'type': 'Stock', 'sector': 'Consumer Discretionary'},
                {'symbol': 'META', 'name': 'Meta Platforms Inc.', 'type': 'Stock', 'sector': 'Communication'},
                {'symbol': 'TSLA', 'name': 'Tesla Inc.', 'type': 'Stock', 'sector': 'Automotive'},
                {'symbol': 'SPY', 'name': 'SPDR S&P 500 ETF', 'type': 'ETF', 'sector': 'Broad Market'},
                {'symbol': 'QQQ', 'name': 'Invesco QQQ Trust', 'type': 'ETF', 'sector': 'Broad Market'},
                {'symbol': 'AMD', 'name': 'Advanced Micro Devices', 'type': 'Stock', 'sector': 'Technology'},
                {'symbol': 'BRK-B', 'name': 'Berkshire Hathaway Inc.', 'type': 'Stock', 'sector': 'Financial'},
                {'symbol': 'JNJ', 'name': 'Johnson & Johnson', 'type': 'Stock', 'sector': 'Healthcare'},
                {'symbol': 'V', 'name': 'Visa Inc.', 'type': 'Stock', 'sector': 'Financial'},
                {'symbol': 'WMT', 'name': 'Walmart Inc.', 'type': 'Stock', 'sector': 'Consumer Staples'},
                {'symbol': 'JPM', 'name': 'JPMorgan Chase & Co.', 'type': 'Stock', 'sector': 'Financial'}
            ]
            matches = [p for p in popular if q in p['symbol'] or q in p['name'].upper()] if q else popular[:8]
            self._send_json({'results': matches})
            return

        elif path == '/api/sync-csharp-dataset':
            # Download real 1-year historical dataset for C# application
            symbols = params.get('symbols', ['AAPL,MSFT,GOOGL,AMZN,SPY'])[0].split(',')
            symbols = [s.strip().upper() for s in symbols if s.strip()]

            lines = ['Symbol,Date,Open,High,Low,Close,Volume']
            count = 0
            for s in symbols:
                hist = get_stock_history(s, period='1y')
                if hist and hist.get('records'):
                    for r in hist['records']:
                        lines.append(f"{s},{r['date']},{r['open']},{r['high']},{r['low']},{r['close']},{r['volume']}")
                        count += 1

            try:
                os.makedirs(os.path.dirname(DATA_FILE), exist_ok=True)
                with open(DATA_FILE, 'w') as f:
                    f.write('\n'.join(lines) + '\n')
                self._send_json({'success': True, 'records': count, 'file': DATA_FILE})
            except Exception as ex:
                self._send_json({'success': False, 'error': str(ex)})
            return

        super().do_GET()

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        content_length = int(self.headers.get('Content-Length', 0))
        body = self.rfile.read(content_length) if content_length > 0 else b'{}'
        
        try:
            payload = json.loads(body.decode('utf-8'))
        except:
            payload = {}

        if parsed.path == '/api/portfolio/add':
            new_id = add_portfolio_investment(payload)
            self._send_json({'success': True, 'id': new_id})
            return

        elif parsed.path == '/api/portfolio/remove':
            item_id = payload.get('id')
            deleted = remove_portfolio_investment(item_id)
            self._send_json({'success': True, 'deleted': deleted})
            return

        elif parsed.path == '/api/portfolio/sync-realtime':
            res = sync_realtime_to_sqlite()
            self._send_json(res)
            return

        elif parsed.path == '/api/ml-forecast':
            symbol = payload.get('symbol', 'AAPL').upper()
            hist = get_stock_history(symbol, period='1y')
            if not hist or not hist.get('records'):
                self._send_json({'error': f'No historical data for {symbol}'}, 404)
                return

            res = train_ml_regression(hist['records'])
            if res:
                res['symbol'] = symbol
                self._send_json(res)
            else:
                self._send_json({'error': 'Insufficient training data'}, 400)
            return

        elif parsed.path == '/api/anomalies':
            symbol = payload.get('symbol', 'AAPL').upper()
            hist = get_stock_history(symbol, period='1y')
            if not hist or not hist.get('records'):
                self._send_json({'anomalies': []})
                return

            anoms = detect_series_anomalies(hist['records'], symbol)
            self._send_json({'symbol': symbol, 'anomalies': anoms})
            return

        elif parsed.path == '/api/analyze':
            holdings = payload.get('holdings', [])
            if not holdings:
                self._send_json({'error': 'No holdings provided'}, 400)
                return

            syms = list(set([h['symbol'].upper() for h in holdings]))
            if 'SPY' not in syms:
                syms.append('SPY')

            with ThreadPoolExecutor(max_workers=6) as pool:
                hist_results = list(pool.map(lambda s: (s, get_stock_history(s, '1y')), syms))

            histories = {}
            for sym, h in hist_results:
                if h and h.get('records'):
                    histories[sym] = {r['date']: r['close'] for r in h['records']}

            if 'SPY' not in histories:
                self._send_json({'error': 'Failed to fetch benchmark SPY'}, 500)
                return

            common_dates = sorted(list(histories['SPY'].keys()))

            portfolio_daily_vals = []
            spy_daily_vals = []
            valid_dates = []

            for d in common_dates:
                val = 0.0
                complete = True
                for h in holdings:
                    s = h['symbol'].upper()
                    q = float(h.get('qty') or h.get('quantity') or h.get('shares') or 0)
                    if s in histories and d in histories[s]:
                        val += q * histories[s][d]
                    else:
                        complete = False
                        break
                if complete and val > 0:
                    portfolio_daily_vals.append(val)
                    spy_daily_vals.append(histories['SPY'][d])
                    valid_dates.append(d)

            if len(portfolio_daily_vals) < 20:
                self._send_json({'error': 'Insufficient aligned return records'}, 400)
                return

            p_rets = [(portfolio_daily_vals[i] - portfolio_daily_vals[i-1]) / portfolio_daily_vals[i-1] for i in range(1, len(portfolio_daily_vals))]
            s_rets = [(spy_daily_vals[i] - spy_daily_vals[i-1]) / spy_daily_vals[i-1] for i in range(1, len(spy_daily_vals))]

            mean_p = float(np.mean(p_rets))
            mean_s = float(np.mean(s_rets))
            var_p = float(np.var(p_rets, ddof=1))
            var_s = float(np.var(s_rets, ddof=1))
            vol_p = math.sqrt(var_p * 252)
            vol_s = math.sqrt(var_s * 252)
            ann_ret_p = mean_p * 252
            ann_ret_s = mean_s * 252

            downside_rets = [min(0.0, r) for r in p_rets]
            downside_vol = math.sqrt(sum(r*r for r in downside_rets) / len(downside_rets) * 252)

            rf = 0.05
            sharpe = (ann_ret_p - rf) / vol_p if vol_p > 0 else 0.0
            sortino = (ann_ret_p - rf) / downside_vol if downside_vol > 0 else 0.0

            cov_ps = float(np.cov(p_rets, s_rets)[0][1])
            beta = cov_ps / var_s if var_s > 0 else 1.0
            alpha = ann_ret_p - (rf + beta * (ann_ret_s - rf))

            # Max Drawdown
            peak = portfolio_daily_vals[0]
            max_dd = 0.0
            for v in portfolio_daily_vals:
                if v > peak:
                    peak = v
                dd = (peak - v) / peak if peak > 0 else 0.0
                if dd > max_dd:
                    max_dd = dd

            current_val = portfolio_daily_vals[-1]
            total_cost = sum(h.get('qty', 0) * h.get('buyPrice', 0) for h in holdings)
            var_95_dollar = current_val * (1.645 * math.sqrt(var_p))

            weights = []
            for h in holdings:
                s = h['symbol'].upper()
                last_price = histories.get(s, {}).get(valid_dates[-1], h.get('buyPrice', 0))
                weights.append((h.get('qty', 0) * last_price) / current_val if current_val else 0)
            hhi = sum(w*w for w in weights)

            # Performance timeline sample for charts (every 2nd date)
            timeline_dates = valid_dates[::2]
            timeline_portfolio = [round(portfolio_daily_vals[valid_dates.index(d)], 2) for d in timeline_dates]
            # Normalize SPY benchmark to portfolio start value
            spy_start = spy_daily_vals[0]
            timeline_benchmark = [round(portfolio_daily_vals[0] * (spy_daily_vals[valid_dates.index(d)] / spy_start), 2) for d in timeline_dates]

            self._send_json({
                'currentMarketValue': round(current_val, 2),
                'totalCost': round(total_cost, 2),
                'unrealizedPnl': round(current_val - total_cost, 2),
                'unrealizedPnlPct': round(((current_val - total_cost) / total_cost * 100) if total_cost else 0, 2),
                'annualizedReturn': round(ann_ret_p * 100, 2),
                'benchmarkReturn': round(ann_ret_s * 100, 2),
                'annualizedVolatility': round(vol_p * 100, 2),
                'sharpeRatio': round(sharpe, 2),
                'sortinoRatio': round(sortino, 2),
                'beta': round(beta, 2),
                'alpha': round(alpha * 100, 2),
                'maxDrawdown': round(max_dd * 100, 2),
                'var95Dollar': round(var_95_dollar, 2),
                'hhi': round(hhi, 4),
                'effectiveDiversified': round(1.0 / hhi if hhi > 0 else len(holdings), 1),
                'timeline': {
                    'dates': timeline_dates,
                    'portfolio': timeline_portfolio,
                    'benchmark': timeline_benchmark
                }
            })
            return

        self.send_error(404, "Endpoint not found")

    def _send_json(self, data, status=200):
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps(data).encode('utf-8'))

if __name__ == '__main__':
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("", PORT), FinPulseApiHandler) as httpd:
        print(f"[FinPulse] Live Market Data Server listening on http://localhost:{PORT}")
        httpd.serve_forever()
