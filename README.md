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

### Priorytet 5: Interaktywny Dashboard z Wykresem (TradingView Lightweight Charts) ✅
- [x] Nowoczesny panel frontendowy serwowany bezpośrednio z Kestrel (`wwwroot`) jako pojedynczy, lekki kontener bez zbędnego narzutu i bez problemów z CORS.
- [x] Pełna integracja z biblioteką **TradingView Lightweight Charts v5.2.1** (renderowanie na Canvas, płynne 60 FPS, gesty, celownik).
- [x] Interaktywny wykres świecowy BTC/USDT z automatycznie naniesionymi algorytmicznymi znacznikami punktów zwrotnych (zielone strzałki na lokalnych dołkach, czerwone strzałki na lokalnych szczytach).
- [x] Zsynchronizowany dolny wykres wskaźników z przełączanymi zakładkami: **Funding Rate Z-Score** (z poziomami wykupienia/wyprzedania), **Przepływy ETF** (zielone/czerwone słupki w mln USD) oraz **Fear & Greed Index**.
- [x] Karty metryk na żywo: predykcja ML (FastTree), prawdopodobieństwo wzrostu, reżim rynku, cena BTC, sentyment, dźwignia oraz kluczowe czynniki rynkowe (Key Drivers).
- [x] Moduł szybkiego przeliczania Walk-Forward Backtestu z metrykami (Win Rate, Profit Factor, Sharpe Ratio, Max Drawdown) bezpośrednio w interfejsie.
- [x] Tabela historii ostatnich dobowych predykcji z bazy SQLite oraz przyciski synchronizacji i alertu Telegram.

### Priorytet 6: Konteneryzacja & Hosting w Chmurze (Docker & GCP Always Free) ✅
- [x] Zoptymalizowany wieloetapowy `Dockerfile` dla .NET 9 i SQLite z warstwowym buforowaniem NuGet.
- [x] Konfiguracja `docker-compose.yml` z trwałym wolumenem `./Data/storage` (chroniącym bazę `market_data.db` i cache ETF) oraz healthcheckiem.
- [x] Szablon `.env.example` dla bezpiecznego przekazywania kluczy bota Telegram bez commitowania sekretów.
- [x] Wdrożenie na darmową maszynę `e2-micro` w Google Cloud Platform (GCP Always Free).
- [x] Inteligentny fallback przed geoblokadą Binance (HTTP 451) w amerykańskich centrach danych (Binance.US + Deribit).

### Priorytet 7: Automatyzacja CI/CD (GitHub Actions Continuous Deployment) ✅
- [x] Workflow GitHub Actions automatycznie kompilujący i weryfikujący projekt (.NET 9 SDK Release) po pushu do gałęzi `main`.
- [x] W pełni zautomatyzowany deploy na maszynę wirtualną w GCP przez dedykowany klucz SSH (`git pull` + `docker compose up -d --build`).

### Priorytet 8: Silnik Fal i Poziomów Fibonacciego (Golden Pocket & Kaufman Wave Efficiency) ✅
- [x] Matematyczna inżynieria cech: dynamiczny 30-dniowy zakres swing, współczynnik zniesienia Fibonacciego (`FibRetracementRatio30d`), odległość i flaga złotej kieszeni (`IsInGoldenPocket30d` dla 0.618–0.650).
- [x] Kaufman Efficiency Ratio (KER 10d) jako ilościowy estymator dynamiki fal Elliotta (fala impulsowa >0.55 vs struktura korekcyjna ABC <0.25) oraz 50-dniowy trend makro (`WeeklyTrendBias`).
- [x] Integracja nowych cech z modelem FastTree GBDT ML.NET, regułami heurystycznymi i diagnozą Key Drivers.
- [x] Wizualizacja poziomów Fibonacciego i strefy Golden Pocket bezpośrednio na świecach TradingView Lightweight Charts oraz dedykowany subwykres KER w interfejsie.

### Priorytet 9: Integracja Globalnego Makro (DXY, US10Y, S&P 500, NASDAQ) ✅
- [x] Asynchroniczna integracja bezpłatnego Yahoo Finance API v8 dla kluczowych aktywów makroekonomicznych: Indeksu Dolara (`DX-Y.NYB`), rentowności 10-letnich obligacji USA (`^TNX`), S&P 500 (`^GSPC`) oraz NASDAQ (`^IXIC`).
- [x] Inteligentny algorytm wyrównywania kalendarza TradFi (forward-fill) eliminujący luki weekendowe i święta rynkowe dla bezszwowej korelacji z rynkiem krypto (24/7).
- [x] Ilościowe wskaźniki makro: `DxyReturn7d`, `DxyZScore30d` (kontrakcja vs ekspansja płynności dolarowej), `Us10YYieldChange7d` (zmiana kosztu kapitału benchmarkowego) oraz `Sp500Return7d` (apetyt instytucjonalny na ryzyko).
- [x] Wzbogacenie modelu FastTree ML.NET, reguł heurystycznych oraz generowanych wyjaśnień Key Drivers o relacje makro-płynnościowe.
- [x] Nowa karta wskaźnika makro w panelu głównym oraz dedykowany wieloosiowy subwykres TradingView zestawiający dynamikę DXY i S&P 500.

### Priorytet 10: Silnik Puli Płynności i Poziomów Likwidacji (Liquidity Sweep Engine & Liquidation Walls) ✅
- [x] Matematyczny model estymacji poziomów likwidacji pozycji lewarowanych 10x, 25x i 50x (górne i dolne klastry Stop Loss) w oparciu o formuły depozytu zabezpieczającego (Maintenance Margin Rate ~0.5%) i swing pivots.
- [x] Detekcja wybicia płynności (Liquidity Sweeps / Stop Hunts / SFPs): identyfikacja sytuacji, gdy cena przebija kluczowe dołki/szczyty, czyści klastry zleceń stop-loss i gwałtownie powraca do zakresu (reclaim).
- [x] Nowe cechy ilościowe w pipeline ML: `DistToLowerLiquidityWall`, `DistToUpperLiquidityWall`, `IsLongLiquiditySwept` i `IsShortLiquiditySwept` zasilające model FastTree ML.NET, wagi heurystyczne oraz Key Drivers.
- [x] Rozszerzenie API (`/api/marketdata/chart`) o obiekt `LiquiditySummary` (poziomy ścian 50x/25x, dystanse procentowe, strona dominująca) oraz punkty historyczne odległości do ścian.

### Priorytet 11: Wizualny Stożek Projekcji 7D, Dynamiczne Targety TP/SL, Płynne Skalowanie Wykresów & Siatka 2x4 z Tooltipami ✅
- [x] **Stożek Projekcji Zmienności (7-Day Volatility Forecast Cone):** Dorysowanie na wykresie TradingView 7-dniowej trajektorii modelu FastTree wraz z dynamicznym przedziałem zmienności $\pm \sigma \cdot \sqrt{t}$ opartym na indeksie Deribit DVOL i zrealizowanej zmienności 7d. Przycisk w nagłówku wykresu umożliwia wygodne włączanie/wyłączanie stożka.
- [x] **Dynamiczne Targety Cenowe (Take-Profit / Stop-Loss / Risk-Reward Ratio):** Automatyczne wyznaczanie poziomów realizacji zysku (TP) i obrony kapitału (SL) sprzężonych z prawdopodobieństwem modelu oraz najbliższymi ścianami likwidacji; pigułki z targetami w karcie sygnału ML oraz na skali cenowej wykresu.
- [x] **Płynne Skalowanie Wykresów w Pionie (Draggable Resizers) & Fullscreen:** Interaktywne poziome uchwyty (splittery) z możliwością przeciągania góra-dół, pamięcią rozmiaru w `localStorage` (zapamiętywanie preferencji użytkownika) oraz trybem pełnoekranowym (`⛶`).
- [x] **Reorganizacja Metryk w Siatkę 2x4:** Podział górnych kafelków na dwa rzędy po cztery elementy (eliminacja pustej przestrzeni i ucinania długich liczb, dodanie zakresu 24h Low/High).
- [x] **Interaktywne Tooltipy Informacyjne `?`:** Dedykowane okienka wyjaśniające znaczenie każdego wskaźnika (m.in. dynamika fal KER, strefa Golden Pocket, Z-score fundingu, przepływy ETF, makro TradFi) po najechaniu myszą lub kliknięciu.

---



## 🚀 Jak uruchomić projekt

### Opcja 1: Bezpośrednio przez .NET SDK (najszybsza do developingu)
Wymagania: **.NET SDK 9.0+**

```bash
# Uruchomienie aplikacji:
dotnet run

# lub w trybie hot-reload:
dotnet watch run
```

### Opcja 2: Kontener Docker (Docker Compose)
Wymagania: **Docker Desktop**

```bash
# Zbudowanie obrazu i uruchomienie w tle:
docker compose up -d --build

# Sprawdzenie logów aplikacji:
docker compose logs -f

# Zatrzymanie:
docker compose down
```

### 🌐 Interfejsy w przeglądarce:
- ⚡ **Interaktywny Dashboard TradingView:** 👉 **`http://localhost:5078/`**
- 📑 **Dokumentacja API Swagger UI:** 👉 **`http://localhost:5078/swagger`**


