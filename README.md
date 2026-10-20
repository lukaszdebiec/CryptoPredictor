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


### Priorytet 2: Migracja Storage z JSON do SQLite (EF Core) ✅
- [x] Wdrożenie Entity Framework Core z dostawcą SQLite (`Microsoft.EntityFrameworkCore.Sqlite`).
- [x] Stworzenie `CryptoPredictorDbContext` oraz serwisu `SqliteMarketDataStorage` z atomowym Upsertem (AddOrUpdate).
- [x] Automatyczna, bezstratna migracja: przy starcie dotychczasowe rekordy z `market_data.json` są samoczynnie importowane do `market_data.db`.
- [x] Zoptymalizowane zapytania `LoadRecentAsync(days)` z natywnym `LIMIT` i indeksem po dacie.


### Priorytet 3: Automatyczny Background Worker (Harmonogram 00:05 UTC) ✅
- [x] Wdrożenie serwisu `DailyMarketDataWorker` jako `BackgroundService` w .NET.
- [x] Precyzyjny harmonogram: automatyczne dociąganie nowo zamkniętej świecy dobowej codziennie o 00:05 UTC (oraz opcjonalny cykl wstępny przy starcie aplikacji).
- [x] Automatyczne przeliczanie cech i ponowne dotrenowanie modelu ML (FastTree) po zamknięciu doby.
- [x] Trwały zapis diagnozy rynku i prognoz w tabeli `MarketPredictions` w SQLite oraz nowy endpoint `GET /api/prediction/history`.


### Priorytet 4: System Alertów i Powiadomień (Telegram Bot) ✅
- [x] Integracja z oficjalnym Telegram Bot API (prosty HTTP Client bez ciężkich bibliotek zewnętrznych).
- [x] Automatyczne formatowanie alertów HTML ze statystykami: sygnał, prawdopodobieństwo, cena BTC, Fear & Greed, Funding Rate Z-Score, 7-dniowy napływ ETF oraz kluczowe czynniki rynkowe (Key Drivers).
- [x] Automatyczne wyzwalanie alertu po dobowym cyklu agregacji i predykcji (`DailyMarketDataWorker`).
- [x] Dedykowany kontroler `AlertsController` (`/api/alerts/status`, `/api/alerts/test`, `/api/alerts/send-today`) ułatwiający weryfikację i ręczne testowanie bota z poziomu Swagger UI.
- [x] Bezpieczny fallback – brak uzupełnionego tokenu nie zatrzymuje działania aplikacji ani cyklu dobowego.

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
