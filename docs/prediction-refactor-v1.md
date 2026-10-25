# prediction-refactor-v1

## Cel i zakres

Ta wersja usuwa potwierdzone błędy pomiaru i rozdziela prognozę kierunku od zarządzania transakcją. Nie jest deklaracją przewagi inwestycyjnej. Wersja modelu zapisywana w API i bazie: `direction-v1`.

Przewidywane zdarzenie to **cena zamknięcia za 7 dni kalendarzowych > cena zamknięcia dnia sygnału**. Wzrost o 1% jest etykietą dodatnią. Neutralny sygnał oznacza brak pozycji, a nie dodatkową klasę treningową ani wygraną transakcję. Trafność kierunku i zysk z TP/SL są raportowane osobno.

## Zmiany modelu

- Jeden model logistyczny z regularizacją L1=0,01 i L2=1, bez ręcznych korekt prawdopodobieństwa, fikcyjnej rady modeli ani optymistycznych AUC w razie błędu.
- 10 cech cenowo-wolumenowych: zwroty 1/3/7/30D, drawdown 30D, zmienność zrealizowana 7/30D, wolumen względem średniej 30D, KER 10D i odchylenie od średniej ceny 50D.
- Co najmniej 60 kolejnych dni ceny na rozgrzewkę, następnie 100 kompletnych etykiet. To ostrożne minimum techniczne, nie dowód wystarczającej liczby niezależnych obserwacji.
- Ostatnie 20% etykiet służy do chronologicznej walidacji. Z pierwszych 80% usuwane są próbki, których termin wyniku dotyka początku walidacji lub go przekracza. Normalizacja jest dopasowana wyłącznie na części treningowej.
- Metryki obejmują rzeczywistą accuracy, precision, recall, F1, AUC (null przy jednej klasie testowej), Brier score i log loss. Baseline to stała częstość wzrostów wyznaczona z treningu, nie z walidacji. Raportowane są daty, liczebności i fingerprint danych.
- Stała specyfikacja jest po walidacji dopasowana ponownie do wszystkich dostępnych, dojrzałych etykiet. `TrainingSampleCount` opisuje historyczny fit walidacyjny; `RefitSampleCount` opisuje model użyty do bieżącej estymaty.
- Próg wejścia to 0,60 dla long / 0,40 dla short. Aktywny sygnał wymaga ponadto zdefiniowanego AUC oraz poprawy Brier, log loss i accuracy względem baseline w walidacji. Ten warunek nie dowodzi istotności statystycznej; ogranicza wydawanie sygnałów bez zaobserwowanej przewagi.
- `ProbabilityIsCalibrated=false`: estymata logistyczna nie jest przedstawiana jako niezależnie potwierdzona częstość sukcesu.

Nie dobierano parametrów, aby poprawić wynik próbki audytowej. Wybrany model jest punktem odniesienia do kolejnych porównań, nie zwycięzcą przeszukiwania hiperparametrów.

## Dlaczego źródła zewnętrzne nie uczą modelu v1

W audycie lokalny fallback OI zawierał jeden snapshot zamiast historii, a funding tylko 32 daty na 215. Brak danych stawał się zerem. Nie da się odróżnić rzeczywistego zera od awarii przy odczycie starszych rekordów. Nie używamy więc tych pól jako cech kierunkowego modelu v1. Pozostają dostępne w dashboardzie jako kontekst. Fibonacci, projekcje likwidacji i opisy KER również nie nadpisują estymaty ML.

Ten branch **nie odbudowuje historycznych danych OI/fundingu i nie rozwiązuje wszystkich ograniczeń ingestion**. Włączenie dodatkowych źródeł wymaga kompletności, znaczników faktycznej dostępności, pochodzenia rynku i testu ablation na niewykorzystanym okresie. Usunięto natomiast wypełnianie dawnych dni przyszłą pierwszą wartością makro.

## Czas i odtwarzalność

- Binance daily odrzuca niezakończoną świecę na podstawie close time. SQLite nie zapisuje ani nie zwraca aktualnej doby UTC jako zamkniętej obserwacji.
- Worker i API generują estymatę dla ostatniej zamkniętej doby. Datą sygnału pozostaje data tej świecy; rzeczywisty moment utworzenia jest w `GeneratedAtUtc`.
- Dziennik historyczny dopasowuje model codziennie na danych dostępnych do daty prognozy. Nie ma podwójnego skracania historii o 7 dni.
- Zapisywany jest pierwszy snapshot prognozy dla daty: wynik, specyfikacja, walidacja i cechy wejściowe. Odświeżenie nie przepisuje pierwotnej prognozy. Save zwraca kanonicznie zapisany wynik także przy równoczesnych żądaniach.
- Schemat SQLite jest rozszerzany o nullable `PredictionJson` i `FeatureSnapshotJson`; historyczne prognozy nie są przepisywane. Legacy pozostają w `/api/prediction/history`, lecz nie mieszają się z metrykami live v1. Jeśli legacy już zajmuje tę samą datę, API zwraca 409; pierwsza prognoza v1 zostanie zapisana dla kolejnej daty.
- Cache historyczny i cache treningu uwzględniają wartości danych, a nie wyłącznie ich liczbę i ostatnią datę.

Nadal nie jest to pełny rejestr wszystkich korekt surowych danych. Historyczna symulacja na dzisiejszym pliku nie zastępuje niezmiennego forward-testu. Cena wejścia na zamknięciu jest założeniem symulacyjnym; realne wykonanie po obliczeniach i opóźnienie workera mogą dać inną cenę.

## Transakcje i wyniki

- Wspólny `PredictionOutcomeResolver` rozlicza dziennik historyczny, live i time machine. Jeśli jedna świeca dotknie obu barier, przyjmowany jest stop-loss. Luka nie pozwala na fikcyjne wykonanie po lepszej cenie stopa.
- Niepełny horyzont pozostaje `PENDING`; luka uniemożliwiająca poznanie ścieżki ceny daje `DATA_UNAVAILABLE`. `NO_TRADE` nie zwiększa win rate.
- Domyślny SL zależy od historycznej zmienności: 2–6%; TP wynosi 1,5 × SL. Jest to osobna polityka ryzyka, nie zdarzenie treningowe. Nie gwarantuje zyskowności.
- Opłata i poślizg domyślnie wynoszą po 0,05% na stronę, łącznie 0,20% round trip w przybliżeniu do nominału wejścia. Można je zmieniać w `BacktestSettings`. Finansowanie i pożyczka shortów nie są uwzględnione.
- Backtest wykorzystuje dzienne prognozy walk-forward, jedną pozycję naraz i rzeczywistą datę wyjścia. Nie korzysta z singletona wytrenowanego na całej historii. Pozycja otwarta na końcu danych jest jawnie zamykana jako `END_OF_DATA`.
- Drawdown i Sharpe strategii liczone są z dziennych wartości portfela. Benchmark buy-and-hold używa tych samych dat oceny. Dziennik nakładających się sygnałów nie udaje krzywej kapitału portfela.
- Brak strat nie zamienia się w fikcyjny profit factor 99. `ProfitFactorValue=null` oznacza wynik niezdefiniowany; stary liczbowy `ProfitFactor` pozostaje dla kompatybilności API.
- Stożek ma płaski poziom odniesienia, dodatnie pasma lognormalne i poprawną jednostkę annualizowanej zmienności. Nie jest uczoną prognozą ścieżki ceny ani zweryfikowanym przedziałem ufności. Widok 4H nadal pokazuje kierunkowy model dobowy, nie osobny model intraday.

## Powtórzenie sprawdzeń

Wymagany .NET 9 oraz Node (dla czterech testów matematyki dashboardu):

```bash
dotnet restore Tests/CryptoPredictor.Tests.csproj
dotnet test Tests/CryptoPredictor.Tests.csproj -c Release --no-restore
node --test Tests/journal-math.test.cjs
dotnet build CryptoPredictor.csproj -c Release
```

W chmurowym środowisku z onboardingu należy najpierw aktywować SDK i katalog runtime zgodnie z zapisanymi instrukcjami, np. `source /workspace/.cloud-setup/cryptopredictor/env.sh`.

Ocena własnego pliku JSON `DailyMarketData` (PascalCase lub camelCase):

```bash
dotnet run --project tools/Evaluate/Evaluate.csproj -c Release -- /path/to/market-data.json
```

Narzędzie tylko odczytuje plik i wypisuje JSON. Nie synchronizuje źródeł, nie używa ani nie zmienia bazy aplikacji. Raport zawiera SHA-256 wejścia. Dla porównania modeli trzeba używać identycznych danych, dat, kosztów i reguł wykonania.

## Wynik próbki z audytu — ograniczenie, nie obietnica

Wejście: publiczny snapshot audytowy 2026-03-25–2026-10-25. Po odrzuceniu aktualnej doby: 214 obserwacji do 2026-10-24. Był to już oglądany okres, więc nie jest niezależnym końcowym holdoutem.

Pełny raport liczbowy i SHA-256 wejścia: [direction-v1-audit-snapshot.json](benchmarks/direction-v1-audit-snapshot.json). Surowy snapshot nie jest dodawany do repozytorium; narzędzie oceny przyjmuje osobny plik danych. Weryfikacja tej zmiany obejmowała 23 testy .NET, 4 testy JavaScript, build Release i żądania HTTP do osobnej instancji z kopią danych audytowych. Nie zmieniano roboczej bazy aplikacji.

- 155 ocenianych dat po rozgrzewce: 106 z niedostateczną historią treningową, 39 bez przewagi na walidacji, 10 z przejściem walidacji.
- **0 aktywnych transakcji**: na datach z przejściem walidacji estymata nie osiągnęła progu wejścia. Nie ma więc podstaw do podania nowego win rate lub profit factor transakcji.
- Dla 42 estymat kierunkowych z pełnym siedmiodniowym wynikiem, niezależnie od bramki handlowej: 22 poprawne znaki vs 19 dla „zawsze wzrost”. Brier score 0,2767. Próbka jest mała, okna się nakładają, a wynik nie stanowi dowodu poprawy.
- Ostatnia walidacja ma accuracy 43,3% wobec baseline większościowego 60,0%, Brier 0,2602 wobec 0,2460 baseline. Model poprawnie powstrzymuje się od aktywnego sygnału.

To pierwsza wersja do przeglądu i dalszych eksperymentów. Potwierdzona zmiana to rzetelniejszy proces uczenia i pomiaru oraz brak wymuszanych sygnałów. Kolejny krok powinien obejmować dłuższą historię różnych reżimów, porównania ze stałymi baseline i testy osobnych zestawów cech na okresie, który nie służył do projektowania reguł. Kalibracja i ewentualny powrót do ensemble wymagają takich danych.
