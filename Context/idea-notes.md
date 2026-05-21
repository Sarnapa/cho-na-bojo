## ChoNaBojo - MVP

### Główny problem
Do uprawiania niektórych sportów potrzebna jest większa liczba osób, a nie zawsze jest możliwe, żeby wśród znajomych znaleźć wystarczająca liczbę osób, którzy będą podzielali Twoje sportowe upodobania, bądź nie są dostępni czasowo. 

### Najmniejszy zestaw funkcjonalności
- Użytkownik ma indywidualne konto, do którego musi się zalogować.
- Użytkownik ma możliwość wyboru interesującego go w danej chwili dyscypliny sportu ze zdefiniowanej listy dostępnych w aplikacji.
- Użytkownik ma sposobność otworzenia widoku mapy, w celu znalezienia obiektu, gdzie może uprawiać interesującą go dyscyplinę sportu. Widok mapy zostaje dostosowany według aktualnej lokalizacji użytkownika.
- Po wyborze obiektu, użytkownik ma dwie opcje - utworzenie swojego wydarzenia albo dołączenie już do stworzonego przez innego użytkownika.
- Przy tworzeniu nowego wydarzenia, użytkownik definiuje datę jego rozpoczęcia, szacowany czas zakończenia oraz limit uczestników.
- Po dołączeniu użytkownika do istniejącego już wydarzenia, musi on czekać na akceptację ze strony organizatora wydarzenia.
- W przypadku dołączenia jakiegoś użytkownika do wydarzenia, jego organizator dostaje należyte powiadomienie push.
- Użytkownik, będący organizatorem wydarzenia, może zaakceptować dołączenie innego użytkownika do swojego wydarzenia, albo je odrzucić, tym samym usuwając tego użytkownika z listy uczestników.
- Użytkownik, będący potencjalnym uczestnikiem wydarzenia, dostaje powiadomienie push o akceptacji, bądź odrzuceniu jego dołączenia do wydarzenia.
- Po akceptacji dołączenia danego użytkownika do wydarzenia, uzyskuje on dostęp do danych kontaktowych (numer telefonu, adres email, login / identyfikator na danym komunikatorze) organizatora i vice versa. Ma to na celu umożliwienie komunikacji między osobami, w celu ustalenia szczegółów organizacyjnych.
- Organizator wydarzenia ma możliwość odwołania wydarzenia. Jego uczestnicy dostaną należyte powiadomienie push.
- Organizator może usunąć danego uczestnika z wydarzenia, nawet po akceptacji jego dołączenia. Uczestnik zostanie o tym poinformowany poprzez powiadomienie push.
- Uczestnik może opuścić wydarzenie, nawet po akceptacji jego dołączenia. Organizator zostanie o tym poinformowany poprzez powiadomienie push.
- Po rozpoczęciu danego wydarzenia, użytkownik nie ma już możliwości dołączenia do niego.

### Co nie wchodzi w zakres MVP
- Dodawanie lokalizacji nowych obiektów sportowych - aplikacja oferuje predefiniowaną listę obiektów.
- Zapraszanie konkretnych użytkowników na wydarzenie.
- System oceniania użytkowników na podstawie ich zapowiadanej obecności na wydarzeniu oraz zachowania na nim.
- Wewnętrzny komunikator dla uczestników wydarzenia (po to wprowadzono pokazywanie danych kontaktowych użytkowników po akceptacji dołączenia do wydarzenia, w celu korzystania do komunikacji z zewnętrznych komunikatorów).
- Inny rodzaj aplikacji niż aplikacja mobilna kompatybilna z systemem Android.

### Kryteria sukcesu
- Do wydarzenia danego użytkownika dołączyła oczekiwana liczba osób.
- Użytkownik dołączył do wybranego przez siebie wydarzenia, które się odbyło i pozwoliło poznać nowych ludzi, mających podobne upodobanie, co do uprawiania sportu.
