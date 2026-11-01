# prediction-refactor-v2 — wielkość ruchu BTC

Branch bazuje na `prediction-refactor-v1` (`870c314`). Dodaje osobny model `trajectory-v2.0` do przewidywania dziennych zamknięć za 1, 2, …, 7 dni. Klasyfikator kierunku, reguły transakcji i zapisane prognozy v1 pozostają osobną warstwą.

## Co przedstawia wykres

- Centralna linia to **estymowana mediana ceny zamknięcia** z siedmiu osobnych modeli horyzontowych, a nie ostatnia cena powielona siedem razy ani liniowe dojście do TP.
- Dwie granice to empiryczne kwantyle 10% i 90% rozkładu błędów, skorygowane o zmienność znaną w dniu prognozy. Nominalne pokrycie pojedynczego horyzontu wynosi 80%; rzeczywiste pokrycie jest mierzone i zwracane w API.
- Są to prognozy marginalne kolejnych zamknięć. **Nie oznaczają 80% prawdopodobieństwa, że cała ścieżka znajdzie się w pasmach.** Połączenie median linią jest wizualizacją, nie prognozą ruchów wewnątrz każdej doby. Model nie prognozuje dziennych high/low, kolejności TP/SL ani maksymalnego obsunięcia.
- W widoku 4H punkty nadal dotyczą zamknięć dobowych. Są umieszczane na faktycznym końcu doby UTC; nie tworzymy 42 fikcyjnych predykcji 4H i nie przesuwamy początku prognozy do najnowszej świecy intraday.
- Brak danych daje pustą serię z przyczyną; stara krzywa jest usuwana. Wyłączony overlay również usuwa wszystkie trzy serie.
- Estymata bez przewagi w walidacji jest pokazywana przerywaną, pomarańczową linią z komunikatem **„estymata eksperymentalna — brak potwierdzonej przewagi”**. Dodatni wynik historycznej walidacji nie stanowi gwarancji na przyszłość. Trajektoria nie otwiera transakcji i nie zmienia prawdopodobieństwa klasyfikatora v1.
- Płaska lub prawie płaska linia jest nadal możliwa, jeżeli tak wynika z dopasowanego modelu. Nie wymuszamy nachylenia ani minimalnej wielkości ruchu.

## Dane i pobieranie historii

Nowy downloader używa wyłącznie jednego jawnie wybranego źródła: domyślnie Binance.US, symbol BTCUSDT, interwał 1D. `startTime` i `endTime` pozwalają pobrać więcej niż 1000 świec. Nie ma automatycznego przełączania giełdy pomiędzy stronami zapytania.

Konfiguracja opcjonalna:

```text
PriceHistory__Source=BinanceUS
# albo Binance, jeśli ten rynek jest dostępny z regionu serwera
```

To publiczne API; nie deklarujemy nowego wymogu kluczy. Downloader honoruje odrzucenie dostępu, ograniczone ponowienia 429/5xx i Retry-After. Błąd strony przerywa całą operację; nie zapisuje częściowej odpowiedzi jako pełnej historii. Sprawdzane są daty UTC, zamknięcie świec, poprawność OHLCV, duplikaty i postęp paginacji. Brakujące daty są jawne; ceny nie są interpolowane. Historia sprzed notowania instrumentu może być krótsza niż żądany zakres — `isComplete=false` i `missingDates` pokazują ten fakt.

Oddzielny cache znajduje się w ignorowanym katalogu:

```text
Data/storage/price-history/BinanceUS_BTCUSDT_1d.json
```

Odczyt sprawdza zgodność rynku i metadanych. Odświeżenie scala obserwacje z tego samego źródła i zapisuje plik atomowo, zachowując wcześniejszy backfill. Pobrane świece nie nadpisują wzbogaconych rekordów `MarketData` zerami dla fundingu, ETF lub OI. Jeśli cache już istnieje, worker odświeża ostatni fragment przed cyklem dotychczasowych źródeł, więc awaria opcjonalnego źródła v1 nie blokuje aktualizacji trajektorii.

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

## Metoda modelowania

1. Dla każdego horyzontu `h` celem jest `log(close[D+h] / close[D])`, podzielony przez zmienność dobową znaną w D i `sqrt(h)`. Używany jest stały zestaw 10 cech cenowo-wolumenowych z v1. Funding, OI, sentyment i przyszłe świece nie wchodzą do regresji.
2. Niewielka, deterministyczna regresja ridge używa standaryzacji dopasowanej wyłącznie na treningu. Kara L2=1, intercept nie jest karany. Standaryzowane cechy są ograniczane do ±5 odchyleń, a dzienna skala zmienności do 0,2–20%; parametry są ustalone przed oceną, nie wybierane na próbce audytowej.
3. Najstarsze 60% dostępnych początków prognozy służy do dopasowania, następne 20% do kalibracji reszt, a ostatnie 20% do walidacji. Przy obu granicach usuwane są obserwacje, których siedmiodniowy wynik dotyka następnego okresu lub do niego wchodzi.
4. Reszty na osobnym okresie kalibracyjnym wyznaczają kwantyle 0,1/0,5/0,9. Estymowana mediana jest więc regresyjną prognozą z poprawką mediany reszt; nie jest osobnym modelem bezpośrednio optymalizującym medianowy loss. Kalibracja globalnych reszt zakłada pewną przenośność rozkładu błędów po przeskalowaniu zmiennością. Może zawieść po zmianie reżimu.
5. Dla każdego horyzontu raportowane są MAE/RMSE logarytmicznej stopy zwrotu, pinball loss kwantyli, rzeczywiste pokrycie i szerokość przedziału. Punkt odniesienia ma prognozę centralną „brak zmiany” oraz przedział z historycznych skalowanych zwrotów kalibracyjnych. Nie jest używana walidacyjna wiedza o przyszłości do dopasowania przedziałów.
6. Status `MODEL_ESTIMATE` dotyczy przewagi siedmiodniowego kandydata: MAE i pinball loss muszą być mniejsze niż baseline, a pokrycie nominalnego przedziału 80% nie mniejsze niż 70%. Jest to jawna bramka badawcza z tolerancją, **nie dowód kalibracji na poziomie 80% lub istotności statystycznej**. API podaje wyniki również dla pozostałych horyzontów.
7. Po ocenie stała specyfikacja jest dopasowana ponownie na pierwszych 80% dojrzałych etykiet z purge; najnowsze 20% staje się kalibracją prognozy bieżącej. Wszystkie te wyniki są już znane w momencie estymaty. Raportowana walidacja opisuje wcześniejszego kandydata, nie pozornie pozatreningowy wynik refitu.

Nie ma gwarancji, że regresja przewyższy losowe błądzenie lub stabilnie przewidzi kierunek. Szczególnie ważne jest odróżnienie małej medianowej zmiany od szerokiego przedziału możliwych wyników.

## Zapis i wersjonowanie

Tabela `TrajectorySnapshots` jest dodawana oddzielnie, bez przepisywania istniejących prognoz v1. Kluczem są data źródłowej świecy, giełda, symbol i wersja modelu. Pierwsza wygenerowana estymata z kompletem ośmiu punktów (D oraz D+1…D+7) jest zachowywana. Niedostateczna historia nie blokuje późniejszego wytrenowania modelu tego samego dnia.

API, główny wykres i symulacja historyczna korzystają z tego samego providera. Zapisany snapshot dla danej daty jest zwracany także po korektach/backfillu danych; nie przepisujemy wyniku „po poznaniu przyszłości”. Refit eksperymentalny na innym zbiorze można wykonać narzędziem offline. Prognoza zawiera czas wygenerowania, datę źródła, wiek danych, fingerprint OHLCV, daty i liczebności podziałów oraz zmierzoną walidację. Zmiana specyfikacji wymaga nowej wersji modelu.

Snapshot zachowuje wynik, ale nie jest pełnym archiwum wag i wszystkich historycznych wersji cen. Do ścisłej reprodukcji eksperymentu należy zachować plik wejściowy zgodny z SHA-256. Symulacja historyczna na skorygowanym dzisiaj pliku nie zastępuje rzeczywistego forward-testu.

## Narzędzie badawcze

```bash
# Opcjonalnie: aktywacja SDK w przygotowanym środowisku cloud
source /workspace/.cloud-setup/cryptopredictor/env.sh

# Pobranie do osobnego pliku; nie modyfikuje bazy aplikacji
dotnet run --project tools/Trajectory -c Release -- download /tmp/btc-history.json --days=1825 --source=BinanceUS

# Najnowsza prognoza, walidacja i krocząca ocena poza próbą
dotnet run --project tools/Trajectory -c Release -- evaluate /tmp/btc-history.json --step=30
```

`evaluate` przyjmuje eksport historii albo tablicę `DailyMarketData` w JSON (PascalCase lub camelCase). Dla każdego badanego początku odtwarza dopasowanie i kalibrację wyłącznie z ówcześnie dostępnych cen, po czym porównuje z rzeczywistym zamknięciem dla horyzontów 1–7. Domyślny krok 30 dni ogranicza nakładanie okresów wynikowych. Raport zawiera także estymaty bez przewagi na wewnętrznej walidacji, aby nie wybierać wyłącznie korzystnych przypadków. `DatasetKind=Synthetic` jest zachowywane w raporcie, jeśli plik tak oznaczono. Wejście użytkownika nie jest automatycznie uznawane za zweryfikowane dane giełdy.

## Weryfikacja i aktualne ograniczenie

```bash
dotnet test Tests/CryptoPredictor.Tests.csproj -c Release
node --test Tests/*.test.cjs
dotnet build tools/Trajectory/Trajectory.csproj -c Release
dotnet build CryptoPredictor.csproj -c Release
```

Sprawdzono logikę modeli, brak dostępu do przyszłych danych, rozdzielenie treningu/kalibracji/walidacji, kwantyle, benchmark, paginację ponad 1000 świec, obsługę braków i odmowy dostępu, scalenie cache oraz niezmienność snapshotów. Izolowana instancja HTTP na 800 **syntetycznych** świecach potwierdziła przepływ API–wykres–symulacja historyczna i działanie oceny kroczącej. To testy poprawności, a nie dowód jakości na BTC.

**Pobranie rzeczywistej historii pozostaje zablokowane:** bieżący proxy odrzuca API kodem 403. Sprawdzono, że nowy endpoint zgłasza 502 z przyczyną i zachowuje dotychczasowy cache. Nie raportujemy skuteczności modelu na wieloletnich danych BTC, których nie udało się pobrać. Po przywróceniu dostępu należy wykonać backfill, ocenę kroczącą i osobny test na okresie niewykorzystanym przy projektowaniu modelu.
