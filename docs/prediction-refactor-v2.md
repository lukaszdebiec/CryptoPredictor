# prediction-refactor-v2 — wielkość ruchu BTC

Branch bazuje na `prediction-refactor-v1` (`870c314`). Dodaje osobny model `trajectory-v2.2` do przewidywania dziennych zamknięć za 1, 2, …, 7 dni. Klasyfikator kierunku (`direction-v1.1`), reguły transakcji i zapisane prognozy v1 pozostają osobną warstwą.

## Co przedstawia wykres

- Centralna linia to **estymowana mediana ceny zamknięcia** z siedmiu osobnych modeli horyzontowych (regularyzowana regresja kwantylowa $\tau = 0{,}50$), a nie ostatnia cena powielona siedem razy ani liniowe dojście do TP.
- Dwie granice to warunkowe kwantyle 10% i 90% skalowane zmiennością oraz poddane kalibracji konformalnej (*Split Conformalized Quantile Regression*) na osobnym chronologicznym okresie kalibracyjnym. Nominalne pokrycie pojedynczego horyzontu wynosi 80%; rzeczywiste pokrycie jest mierzone i zwracane w API.
- Są to prognozy marginalne kolejnych zamknięć. **Nie oznaczają 80% prawdopodobieństwa, że cała ścieżka znajdzie się w pasmach.** Połączenie median linią jest wizualizacją, nie prognozą ruchów wewnątrz każdej doby. Model nie prognozuje dziennych high/low, kolejności TP/SL ani maksymalnego obsunięcia.
- W widoku 4H punkty nadal dotyczą zamknięć dobowych. Są umieszczane na faktycznym końcu doby UTC; nie tworzymy 42 fikcyjnych predykcji 4H i nie przesuwamy początku prognozy do najnowszej świecy intraday.
- Brak danych daje pustą serię z przyczyną; stara krzywa jest usuwana. Wyłączony overlay również usuwa wszystkie trzy serie.
- Estymata bez przewagi w walidacji jest pokazywana przerywaną, pomarańczową linią (zarówno na głównym wykresie, jak i w Time Machine) z komunikatem **„estymata eksperymentalna — brak potwierdzonej przewagi”**. Dodatni wynik historycznej walidacji nie stanowi gwarancji na przyszłość. Trajektoria nie otwiera transakcji i nie zmienia prawdopodobieństwa klasyfikatora v1.
- Płaska lub prawie płaska linia jest nadal możliwa, jeżeli tak wynika z dopasowanego modelu. Nie wymuszamy nachylenia ani minimalnej wielkości ruchu.

## Dane i pobieranie historii

Nowy downloader używa wyłącznie jednego jawnie wybranego źródła: domyślnie Binance.US, symbol BTCUSDT, interwał 1D. `startTime` i `endTime` pozwalają pobrać więcej niż 1000 świec. Nie ma automatycznego przełączania giełdy pomiędzy stronami zapytania.

Konfiguracja opcjonalna:

```text
PriceHistory__Source=BinanceUS
# albo Binance, jeśli ten rynek jest dostępny z regionu serwera
```

To publiczne API; nie deklarujemy nowego wymogu kluczy. Downloader honoruje odrzucenie dostępu, ograniczone ponowienia 429/5xx i Retry-After. Błąd strony przerywa całą operację; nie zapisuje częściowej odpowiedzi jako pełnej historii. Sprawdzane są daty UTC, zamknięcie świec, poprawność OHLCV, duplikaty i postęp paginacji. Brakujące daty są jawne; ceny nie są interpolowane. Jeżeli w bardzo starej części historii wystąpiła luka (lub żądano okresu sprzed startu notowań), model wykorzystuje wyłącznie najdłuższy ciągły sufiks kończący się w dniu prognozy (o ile liczy co najmniej 566 kolejnych świec).

Oddzielny cache znajduje się w ignorowanym katalogu:

```text
Data/storage/price-history/BinanceUS_BTCUSDT_1d.json
```

Odczyt sprawdza zgodność rynku i metadanych. Odświeżenie scala obserwacje z tego samego źródła i zapisuje plik atomowo, zachowując wcześniejszy backfill. Pobrane świece nie nadpisują wzbogaconych rekordów `MarketData` zerami dla fundingu, ETF lub OI. Jeśli cache już istnieje, zarówno worker dobowy, jak i ręczne `POST /api/marketdata/sync` automatycznie dociągają końcowy fragment historii, utrzymując spójność dat między wykresem a trajektorią.

W dashboardzie jest przycisk **„Historia BTC: 5 lat”**. Wywołuje:

```http
POST /api/prediction/trajectory/sync-history?days=1825
GET  /api/prediction/trajectory/history-status
GET  /api/prediction/trajectory
GET  /api/prediction/trajectory/historical?date=2026-10-01
GET  /api/prediction/trajectory/chart?days=180
```

Limit jednego backfillu to 3650 zakończonych dni UTC. 1825 dni to około pięciu lat, nie obietnica tak długiej dostępności wybranego instrumentu. Do trenowania zalecane są dane obejmujące kilka reżimów rynku. Minimum techniczne to 500 dojrzałych, kompletnych początków prognozy po 60-dniowej rozgrzewce, czyli co najmniej 566 kolejnych świec. Nie oznacza to 500 niezależnych siedmiodniowych obserwacji.

Gdy stare źródła nie pozwalają pobrać głównego wykresu 1D, frontend może pokazać niezależny wykres cen z nowego cache. Nie wymyśla wówczas brakujących wskaźników ani sygnału transakcyjnego. Pozostałe funkcje v1 mogą nadal wymagać ich własnych danych.

## Metoda modelowania (`trajectory-v2.2`)

1. Dla każdego horyzontu `h` celem jest `log(close[D+h] / close[D])`, podzielony przez zmienność dobową znaną w D i `sqrt(h)`. Używany jest odchudzony zestaw 8 cech cenowo-wolumenowych (w tym kierunkowy `SignedKaufmanEfficiencyRatio10d` zamiast bezkierunkowego `KER10d`, bez zduplikowanych zwrotów 3D/30D). Funding, OI, sentyment i przyszłe świece nie wchodzą do regresji czysto cenowej.
2. Zamiast regresji średniokwadratowej (MSE) z post-hoc przesunięciem medianowym, `QuantileRegressor` bezpośrednio minimalizuje wygładzoną funkcję straty kwantylowej (*pinball loss*, $\varepsilon = 0{,}02$) metodą IRLS (*Iteratively Reweighted Least Squares*) dla trzech osobnych modeli na każdy horyzont: $\tau \in \{0{,}10; 0{,}50; 0{,}90\}$. Standaryzacja jest dopasowana wyłącznie na treningu, a cechy ograniczone do ±5 odchyleń. Silna regularyzacja L2 nachyleń (`10.0`) oraz wyrazu wolnego mediany (`5.0`) zapobiega przenoszeniu jednorocznego dryfu hossy/bessy na kolejne reżimy.
3. Najstarsze 60% dostępnych początków prognozy służy do dopasowania, następne 20% do konformalnej kalibracji ogonów, a ostatnie 20% do walidacji. Przy obu granicach usuwane są obserwacje, których siedmiodniowy wynik dotyka następnego okresu lub do niego wchodzi.
4. Na osobnym okresie kalibracyjnym wyznaczane są poprawki konformalne kwantyli dolnego (0,10) i górnego (0,90) z wymuszeniem monotoniczności $Q_{10} \le Q_{50} \le Q_{90}$ (*Split Conformalized Quantile Regression*). Mediana $Q_{50}$ pochodzi bezpośrednio z modelu kwantylowego $\tau = 0{,}50$, bez doklejania historycznego dryfu z okresu kalibracji.
5. Dla każdego horyzontu raportowane są MAE/RMSE logarytmicznej stopy zwrotu, pinball loss kwantyli, rzeczywiste pokrycie i szerokość przedziału. Punkt odniesienia ma prognozę centralną „brak zmiany” oraz przedział z historycznych skalowanych zwrotów kalibracyjnych. Nie jest używana walidacyjna wiedza o przyszłości do dopasowania przedziałów.
6. Status `MODEL_ESTIMATE` dotyczy przewagi siedmiodniowego kandydata: MAE i pinball loss muszą być mniejsze niż baseline, a pokrycie nominalnego przedziału 80% nie mniejsze niż 70%. Jest to jawna bramka badawcza z tolerancją, **nie dowód kalibracji na poziomie 80% lub istotności statystycznej**. API podaje wyniki również dla pozostałych horyzontów.
7. Po ocenie stała specyfikacja jest dopasowana ponownie na pierwszych 80% dojrzałych etykiet z purge; najnowsze 20% staje się kalibracją prognozy bieżącej. Wszystkie te wyniki są już znane w momencie estymaty. Raportowana walidacja opisuje wcześniejszego kandydata, nie pozornie pozatreningowy wynik refitu.

Nie ma gwarancji, że regresja kwantylowa przewyższy losowe błądzenie w każdym reżimie lub stabilnie przewidzi kierunek. Szczególnie ważne jest odróżnienie małej medianowej zmiany od szerokiego przedziału możliwych wyników.

## Zapis i wersjonowanie

Tabela `TrajectorySnapshots` jest dodawana oddzielnie, bez przepisywania istniejących prognoz v1 ani wcześniejszych snapshotów `trajectory-v2.0` / `v2.1`. Kluczem są data źródłowej świecy, giełda, symbol i wersja modelu (`trajectory-v2.2`). Pierwsza wygenerowana estymata z kompletem ośmiu punktów (D oraz D+1…D+7) jest zachowywana. Niedostateczna historia nie blokuje późniejszego wytrenowania modelu tego samego dnia.

API, główny wykres i symulacja historyczna korzystają z tego samego providera oraz wieloelementowego cache'u LRU (do 64 początków prognozy w pamięci). Zapisany snapshot dla danej daty jest zwracany także po korektach/backfillu danych; nie przepisujemy wyniku „po poznaniu przyszłości”.

## Narzędzie badawcze i weryfikacja

```bash
# Pobranie do osobnego pliku; nie modyfikuje bazy aplikacji
dotnet run --project tools/Trajectory -c Release -- download /tmp/btc-history.json --days=1825 --source=BinanceUS

# Najnowsza prognoza, walidacja i krocząca ocena poza próbą
dotnet run --project tools/Trajectory -c Release -- evaluate /tmp/btc-history.json --step=30
```

```bash
dotnet test Tests/CryptoPredictor.Tests.csproj -c Release
node --test Tests/*.test.cjs
dotnet build tools/Trajectory/Trajectory.csproj -c Release
dotnet build CryptoPredictor.csproj -c Release
```

Na próbie 1825 rzeczywistych świec dziennych `BinanceUS` (`BTCUSDT`, `missingDates=0`, do `2026-10-31`) model `trajectory-v2.2` osiągnął na rocznym zbiorze walidacyjnym (`N=352`) dla horyzontu 7D `Pinball=0.01547` wobec `0.01549` dla benchmarku „brak zmiany” oraz pokrycie przedziału `79.3%` (nominalnie `80%`), a w 42-okresowej kroczącej ewaluacji poza próbą (co 30 dni, 2023–2026) obniżył 7-dniowe RMSE o 6,1% względem `trajectory-v2.0` (`0.05470` vs `0.05827`) przy pokryciu `83.3%`. Przewaga punktowej mediany nad błądzeniem losowym na dziennych danych OHLCV pozostaje minimalna — główną wartością informacyjną krzywej są skalibrowane zmiennościowo pasma niepewności Q10–Q90.


## 4-Filarowy Wskaźnik Konfluencji (`confluence-v1.0`)

Niezależnie od czysto cenowej mediany `trajectory-v2.2` oraz klasyfikatora `direction-v1.1`, warstwa `ConfluenceIndicatorService` agreguje wieloźródłowe czynniki rynkowe w czytelny indeks `ConfluenceScore` w skali `[-100, +100]` i rysuje na wykresie pasmo zmiennościowe na **7 dni** (`Confluence7d`) lub **30 dni** (`Confluence30d`):

1. **Przepływy Spot & ETF** (`40%` wagi w 7D / `35%` w 30D): `CoinbasePremiumZScore30d` oraz `EtfFlowZScore30d`.
2. **Pozycjonowanie & Sentyment kontrariański** (`30%` w 7D / `25%` w 30D): odwrócony `FundingZScore30d`, nieliniowe skrajności `FearGreedCentered`, wyczerpanie wolumenowe 3D oraz bonus za wycięcie płynności (`IsLongLiquiditySwept` / `IsShortLiquiditySwept`).
3. **Struktura Ceny — Fibo & KER** (`20%` w 7D / `10%` w 30D): `WeeklyTrendBias` (SMA50), głębokość zniesienia `FibRetracementRatio30d` (z premią w `Golden Pocket`) oraz kierunkowy `SignedKaufmanEfficiencyRatio10d` z tłumieniem pogoni za parabolicznym przegrzaniem (`KER > 0.45`).
4. **Makro Płynność** (`10%` w 7D / `30%` w 30D): odwrócony `DxyZScore30d` oraz 7-dniowa zmiana rentowności `Us10YYieldChange7d`.

Jeżeli dany feed zewnętrzny nie jest dostępny w danym dniu (np. brak danych makro lub przepływów w czystej historii OHLCV), jego waga jest proporcjonalnie rozdzielana na aktywne filary bez sztucznego spłaszczania wyniku do zera. W trybie **30D + Cykl**, po pobraniu wieloletniej historii świec (**„Historia BTC: 5 lat”**), na wykres nakładana jest dodatkowo przerywana ścieżka **Analogu Cyklu / Sezonowości**, która normalizuje historyczne ścieżki `D..D+30` z poprzednich lat względem lokalnej zmienności i nadaje wagę `2x` latom o tej samej fazie 4-letniego cyklu Halving / Midterms (`year % 4`), z zachowaniem pełnej izolacji czasowej (*point-in-time*, bez wycieku przyszłości).
