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

### Priorytet 12: Obsługa Multi-Timeframe (Intraday 4H z Binance, Przełącznik 1D/4H, Dynamiczne Likwidacje 4H i Punkty Zwrotne) ✅
- [x] **Asynchroniczny Ingestion Świec 4H z Binance:** Rozszerzenie `IBinanceDataService` o metodę `GetIntradayKlinesAsync` pobierającą świece 4H OHLCV z automatycznym buforowaniem w pamięci (TTL 60s) oraz bezpiecznym fallbackiem `Binance.US` w chmurze GCP.
- [x] **Przełącznik Rozdzielczości 1D / 4H w UI:** Elegancki przełącznik `[ 1D ]` `[ 4H ]` w pasku narzędzi wykresu umożliwiający bezszwowe przełączanie między horyzontem dziennym a 4-godzinnym bez przeładowania strony.
- [x] **Adaptacyjne Punkty Zwrotne 4H (Intraday Turning Points):** Dedykowana detekcja lokalnych dołków i szczytów 4H w 48-godzinnym oknie czasowym z dynamicznym progiem odbicia $\ge 1.5\%$.
- [x] **Ściany Likwidacji 4H & Detekcja Polowań (Stop Hunts / SFPs):** Wyliczanie klastrów likwidacji 25x i 50x z węższych swingów 4H, dające natychmiastową informację o intraday liquidity sweepach.
- [x] **Inteligentne Filtry Zakresu:** Automatyczna adaptacja przycisków zakresu (`7D` | `14D` | `30D` | `60D` | `ALL` dla 4H oraz `30D` | `90D` | `180D` | `1Y` | `ALL` dla 1D).
- [x] **Precyzyjny Datownik w Legendzie:** Wyświetlanie pełnego czasu UTC z godziną i minutą (`YYYY-MM-DD HH:mm UTC`) dla świec 4H.

### Priorytet 13: Architektura Multi-Model Ensemble Consensus (Dynamiczne Wagi, 4 Modele, Konsensus i Alerty Telegram) ✅
- [x] **Rada 4 Niezależnych Modeli ML i Ilościowych (Model Council):**
  - **FastTree (GBDT):** Drzewa decyzyjne z boostingiem gradientowym, wyłapujące nieliniowe zależności wieloczynnikowe (np. jednoczesny sweep płynności + wyprzedany funding).
  - **FastForest (Random Forest):** Zespół lasów losowych z baggingiem, wygładzający szum rynkowy i redukujący wariancję predykcji.
  - **SDCA Logistic Regression (L1/L2 Regularized GLM):** Probabilistyczny model liniowy idealny do monotonicznych trendów makro (indeks DXY, rentowności obligacji, napływy do ETF).
  - **Quant Rule-Based Expert:** Ilościowy silnik reguł rynkowych (strefa 0.618 Golden Pocket, formacje wybić płynności SFP, skrajności sentymentu Fear & Greed).
- [x] **Dynamiczne Ważenie Modeli (Adaptive Regime Weighting):**
  - Bazowe wagi kalkulowane z nadwyżki AUC na zbiorze testowym ($\max(0.10, AUC - 0.45)$).
  - Adaptacja do reżimu rynku: w silnych trendach (Kaufman Efficiency Ratio $> 0.55$) preferencja modeli liniowych i FastTree; w konsolidacjach i korektach (KER $< 0.25$) wzmocnienie wag modelu FastForest oraz eksperta regułowego.
  - Automatyczna normalizacja wag do sumy równej 1.0.
- [x] **Wskaźnik Zgodności Konsensusu (Consensus Agreement & Verdict):**
  - Ilościowy pomiar odchylenia predykcji poszczególnych modeli od średniej zespołu z metryką zgodności $0-100\%$.
  - Klasyfikacja werdyktu rady: `Unanimous Consensus`, `Strong Consensus`, `Moderate Consensus`, `Divided / Split Council`.
- [x] **Persystencja SQLite & Rozszerzenie Alertów Telegram:**
  - Automatyczna migracja kolumny `EnsembleJson` w tabeli `MarketPredictions`.
  - Wzbogacone powiadomienia na Telegramie zawierające werdykt konsensusu, poziom zgodności oraz rozbicie prawdopodobieństw i wag poszczególnych modeli.
- [x] **Interfejs Użytkownika:**
  - Przebudowa karty głównej na `ML ENSEMBLE CONSENSUS` z dynamiczną plakietką poziomu zgodności (np. `85% Agree`), paskiem prawdopodobieństwa oraz rozwijaną siatką modeli z paskami postępu i wagami.

### Priorytet 14: Dziennik Trafności Predykcji i Rozliczanie Sygnałów na Żywo (Live Forward-Testing & Realized PnL Tracker) ✅
- [x] **Silnik Automatycznego Rozliczania Sygnałów (Forward-Testing Outcome Resolver):**
  - Ciągłe śledzenie zrealizowanego wyniku dla każdej historycznej predykcji w 7-dniowym oknie w przód:
    - `TARGET_HIT` – cena osiągnęła pełny poziom Take-Profit (np. $+8.0\%$).
    - `INVALIDATED` – cena wybiła poziom Stop-Loss (np. $-4.0\%$).
    - `EXPIRED_PROFIT` / `EXPIRED_LOSS` – upłynął horyzont 7 dni, rozliczenie po cenie zamknięcia 7. dnia.
    - `PENDING` – transakcja w toku z kalkulacją bieżącego niezrealizowanego PnL (np. `Dzień 2/7, +1.8%`).
- [x] **Pasek Zbiorczych Metryk Skuteczności (Live KPI Performance Bar):**
  - Dynamiczne obliczanie: **Win Rate** (% zyskownych pozycji), **Target Hit Rate** (% pełnych TP), **Profit Factor** ($\sum \text{Gains} / \sum |\text{Losses}|$), **Cumulative Realized PnL** oraz licznika pozycji zamkniętych vs w toku.
- [x] **Rozbudowa Tabeli SQLite & Bazy Danych:**
  - Automatyczna migracja kolumn w tabeli `MarketPredictions`: `TargetPrice`, `InvalidationPrice`, `TargetChangePercent`, `InvalidationChangePercent`, `RiskRewardRatio`, `OutcomeStatus`, `RealizedReturnPercent`, `ResolvedDate`, `ResolvedPrice`.
- [x] **Nowy Endpoint API (`GET /api/prediction/journal`):**
  - Zwraca zagregowane metryki skuteczności oraz pełną listę pozycji z wyliczonym statusem i zrealizowaną stopą zwrotu.
- [x] **Nowoczesny Interfejs Tabeli Journala:**
  - Przebudowa tabeli na 7 kolumn: `Data`, `BTC Close`, `Sygnał`, `Targety (TP / SL)`, `Konsensus`, `Wynik 7D`, `Zrealizowany PnL` z kolorowymi plakietkami (`🎯 TP HIT`, `🛑 SL HIT`, `⏱️ EXPIRED`, `⏳ PENDING`).

### Priorytet 15: "Back to the Future" – Wizualny Wehikuł Czasu i Point-in-Time Backtest ✅
- [x] **Ścisła Symulacja Point-in-Time (Strictly Out-of-Sample):**
  - Użytkownik wybiera dowolną datę historyczną $T$ z dostępnego okna (kwiecień – październik 2026).
  - Backend (`GET /api/prediction/historical`) izoluje zbiór danych wyłącznie do daty $T$ (z 7-dniowym buforem antywyciekowym) i od zera trenuje Radę Modeli (FastTree GBDT, FastForest, SDCA, Heurystyki).
- [x] **Czysty Wykres i 7-Dniowy Stożek Prognozy:**
  - Wykres Lightweight Charts renderuje świece wyłącznie do dnia $T$, po czym rozpościera w pustą przestrzeń 7-dniowy stożek zmienności (`projectionCone`) oraz poziomy docelowe Take-Profit (+8%) i Stop-Loss (-4%).
- [x] **Animacja "▶ Play (Odtwórz 7 dni przyszłości)":**
  - Przycisk odtwarzania dorysowuje świeca po świecy kolejne 7 dni rzeczywistego rynku (z regulowaną prędkością 1x, 2x, natychmiast).
  - Dynamiczny monitoring: sprawdzenie w locie, czy cena osiągnęła TP, SL czy wygasła po 7 dniach.
- [x] **Karta Werdyktu Końcowego (Outcome Verdict Overlay):**
  - Podsumowanie zrealizowanego PnL, rzeczywistej 7-dniowej stopy zwrotu Bitcoina, dnia rozstrzygnięcia oraz oceny trafności kierunkowej modelu.
- [x] **Szybkie Presety i Losowy Dzień:**
  - Zdefiniowane presety kluczowych punktów zwrotnych (dołek paniki $83.5k, szczyt euforyczny $86.5k, bounce $81.7k) oraz generator losowego dnia (`🎲 Losowy dzień`).
### Priorytet 16: Dziennik Predykcji & Walk-Forward Trade Ledger na Pełnej Historii (~180 Dni) ✅
- [x] **Kroczący Rejestr Walk-Forward (Expanding-Window Walk-Forward Ledger):**
  - Generowanie pełnego rejestru transakcyjnego dla każdego dnia w historii (~180 dni, kwiecień – październik 2026).
  - Tygodniowy harmonogram przetrenowywania modeli ML.NET (co 7 dni na danych ściśle sprzed $T-7$) z zerowym wyciekiem przyszłości i buforowaniem w pamięci RAM.
  - Automatyczne rozliczanie każdego 7-dniowego horyzontu: `TARGET_HIT` (+8.0%), `INVALIDATED` (-4.0%), `EXPIRED_PROFIT`, `EXPIRED_LOSS` oraz `PENDING` dla ostatnich 7 dni.
- [x] **Przełącznik Trybów: Cała Historia (~180D) vs Na Żywo (Forward-Testing):**
  - Płynny toggle `[ 📜 Cała Historia (~180D) ]` i `[ 🔴 Na Żywo (Forward-Testing) ]` pozwalający porównać empiryczną historię z bieżącymi sygnałami SQLite.
- [x] **Błyskawiczne Pigułki Filtrujące (Quick Filter Pills):**
  - Filtrowanie tabeli w locie z dynamicznymi licznikami: `Wszystkie`, `🎯 TP HIT`, `🛑 SL HIT`, `⏱️ EXPIRED`, `⏳ W toku`, `🟢 Bullish`, `🔴 Bearish`.
- [x] **Ilościowa Przewaga R:R 2.0 (Quant Context Banner):**
  - Edukacyjny baner wyjaśniający asymetrię zysku do ryzyka: przy $R:R = 2.0$, próg rentowności (break-even) wynosi 33.3%, a skuteczność 50–55% generuje wysoką rentowność (Profit Factor > 1.5).
- [x] **Rozbudowana Tabela Transakcji:**
  - 8 kolumn ze statusem reżimu, celami TP/SL, zgodnością konsensusu, plakietkami rozstrzygnięcia, zrealizowanym PnL i datą wyjścia z pozycji.
### Priorytet 17: Filtr Przekonania (Conviction Threshold), Tryby Egzekucji i Jakość Konsensusu ✅
- [x] **Pasek Kontroli Jakości Sygnału (Conviction & Execution Controls Toolbar):**
  - **Próg Siły Przekonania (Conviction Selector):** Wybór progu minimalnego prawdopodobieństwa modelu: `50% (Wszystkie)`, `≥ 60%`, `≥ 65% (Quant Benchmark)`, `≥ 70% (High Conviction)`, `≥ 75% (Extreme)`.
  - **Przełącznik Trybu Egzekucji:**
    - `📊 Ciągły (Każdy dzień / Rolling 7D)`: ciągła ewaluacja prognozy dla każdego dnia niezależnie.
    - `🎯 Dyskretny (1 trade naraz)`: symulacja realnego portfela, w którym wejście w transakcję blokuje otwieranie kolejnych pozycji do momentu rozstrzygnięcia (eliminuje klastrowanie strat w konsolidacjach).
  - **Filtr Jakości Konsensusu:** Przełącznik `Dowolny` vs `⭐ Tylko Silny Konsensus (Strong/Unanimous Consensus ≥ 60%)`.
- [x] **Błyskawiczne Przeliczanie Metryk w Czasie Rzeczywistym (<5ms):**
  - Natychmiastowa dynamiczna rekalkulacja wszystkich 5 kart KPI (**Win Rate**, **Target Hit Rate**, **Profit Factor**, **Cumulative PnL**, **Resolved / Pending**) oraz liczników pigułek po zmianie dowolnego parametru na suwaku/przełączniku.
- [x] **Plakietki Siły Przekonania na Pozycjach:**
  - Wskaźniki w tabeli dla każdej pozycji: `🔥 High Conviction (≥70%)`, `⚡ Mid Conviction (≥60%)`, `⚠️ Chop Risk (<60%)`.
- [x] **Wskaźnik i Ostrzeżenie w Wehikule Czasu (Time-Machine):**
  - Wyświetlanie siły przekonania oraz dynamicznego ostrzeżenia przed szumem konsolidacyjnym na wybranym dniu $T$ w module "Back to the Future".

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


