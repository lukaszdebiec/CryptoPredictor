# CryptoPredictor 🚀

Zaawansowany system analityczno-predykcyjny oparty na **.NET 9 Web API** oraz **ML.NET**, dedykowany do wykrywania asymetrii ryzyka do zysku, punktów zwrotnych (lokalne dołki i szczyty) oraz reżimów rynkowych na Bitcoinie (BTC).

Zamiast prostych wskaźników analizy technicznej (RSI, MACD), system opiera się na **mikrostrukturze instrumentów pochodnych (Derivatives)**, **zmienności implikowanej**, **sentymentu** oraz **stacjonarnych wskaźnikach Z-Score**.

---

## 🏗️ Architektura i Aktualne Funkcjonalności

- **Pobieranie i agregacja danych (`Data/Ingestion`):**
  - **Farside Investors:** Oficjalne raporty napływów/odpływów netto funduszy Spot BTC ETF (IBIT, FBTC, BITB, GBTC itd.) z automatycznym omijaniem filtrów Cloudflare i lokalnym cache.
  - **Binance Spot:** Świece dzienne OHLCV dla BTC/USDT.
  - **Binance Futures:** Dobowa historia Funding Rate oraz Open Interest (USD).
  - **Alternative.me:** Indeks Fear & Greed.
  - **Coinbase:** Różnica cenowa spot BTC/USD vs Binance (wskaźnik *Coinbase Premium*).
  - **Deribit:** Indeks implikowanej zmienności opcji (*BTC DVOL*).
  - *Wszystkie źródła są w 100% darmowe i nie wymagają płatnych kluczy API.*
- **Silnik Inżynierii Cech (`Features/FeatureCalculator.cs`):**
  - Płynność instytucjonalna ETF (`EtfFlow7d`, `EtfFlow30d` oraz znormalizowany Z-Score `EtfFlowZScore30d`).
  - Stacjonarne wskaźniki Z-Score (30d i 90d dla Fundingu, OI, DVOL).
  - Zannualizowana zrealizowana zmienność (`Realized Volatility` 7d i 30d).
  - Stopy zwrotu (1d, 3d, 7d, 30d) i obsunięcia cenowe (Drawdowny od 30d high, 90d high i ATH).
- **Wykrywanie Punktów Zwrotnych i Etykietowanie (`Backtest/TurningPointDetector.cs`):**
  - Detekcja lokalnych dołków (`LocalBottom`) i szczytów (`LocalTop`) w symetrycznym oknie czasowym.
  - Wyliczanie stóp zwrotu w przód (`ForwardReturn` 3d, 7d, 14d) do nadzorowanego uczenia maszynowego.
- **Silnik Uczenia Maszynowego (`Prediction/PredictorService.cs`):**
  - Wdrożony natywnie w C# algorytm **FastTree (Gradient Boosted Decision Trees)** z biblioteki `Microsoft.ML`.
  - Tryb **`strictlyOutOfSample = true`** uniemożliwiający jakikolwiek przeciek przyszłości (*Lookahead Bias*).
- **Silnik Backtestowy (`Backtest/BacktestEngine.cs`):**
  - Symulacja strategii walk-forward z uwzględnieniem Stop-Loss (-4%), Take-Profit (+8%) oraz okna trzymania pozycji (7 dni).
  - Pełne metryki finansowe: **Win Rate**, **Profit Factor**, **Sharpe Ratio** i **Max Drawdown** w zestawieniu z Buy & Hold.
- **Interaktywna dokumentacja Swagger UI:**
  - Dostępna bezpośrednio pod adresem głównym: `http://localhost:5078/`.

---

## 🗺️ ROADMAP

### Priorytet 1: Uzupełnienie danych ETF (Darmowy Scraper Farside) ✅
- [x] Stworzenie dedykowanego serwisu `FarsideEtfService` zasilającego historię od 11 stycznia 2024 roku z archiwum Farside Investors.
- [x] Ominięcie blokady Cloudflare TLS/WAF oraz wdrożenie lokalnego bufora w `etf_flows.json`.
- [x] Wpięcie do `MarketDataAggregator` i zasilenie `BitcoinEtfNetFlowUsd`.
- [x] Aktywacja cech `EtfFlow7d`, `EtfFlow30d` i `EtfFlowZScore30d` w modelu FastTree (GBDT) oraz interpretacji `KeyDrivers`.


### Priorytet 2: Migracja Storage z JSON do SQLite (EF Core)
- [ ] Zastąpienie pliku cache `market_data.json` lekką, plikową bazą danych **SQLite** za pomocą Entity Framework Core (`Microsoft.EntityFrameworkCore.Sqlite`).
- [ ] Zapewnienie indeksowania po dacie, transakcyjności oraz szybkiego dopisywania pojedynczych dni bez konieczności przepisywania całego pliku.

### Priorytet 3: Automatyczny Background Worker (Harmonogram 00:05 UTC)
- [ ] Wdrożenie `IHostedService` / `BackgroundService` w .NET.
- [ ] Automatyczne dociąganie nowo zamkniętej świecy dobowej o północy, przeliczanie cech i zapisywanie świeżej diagnozy rynku w bazie danych.

### Priorytet 4: System Alertów i Powiadomień (Telegram Bot / Discord)
- [ ] Integracja z Telegram Bot API lub webhookiem Discord.
- [ ] Automatyczne wysyłanie alertu push na telefon, gdy model zidentyfikuje sygnał o wysokim prawdopodobieństwie (`BULLISH_OPPORTUNITY` z szansą > 70%) lub ostrzeżenie o przegrzaniu (`BEARISH_RISK`).

### Priorytet 5: Interaktywny Dashboard z Wykresem (TradingView Lightweight Charts)
- [ ] Prosty, nowoczesny panel frontendowy serwowany z aplikacji.
- [ ] Wykres świecowy BTC z naniesionymi punktami zwrotnymi (znaczniki dołków i szczytów) oraz wskaźnikiem Z-Score fundingu pod wykresem.

---

## 🚀 Jak uruchomić projekt

Wymagania: **.NET SDK 9.0+**

1. Sklonuj repozytorium:
   ```bash
   git clone https://github.com/lukaszdebiec/CryptoPredictor.git
   cd CryptoPredictor
   ```
2. Uruchom aplikację:
   ```bash
   dotnet run
   # lub w trybie hot-reload:
   dotnet watch run
   ```
3. Otwórz w przeglądarce interfejs Swagger UI:
   👉 **`http://localhost:5078/`**
