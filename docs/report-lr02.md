# Звіт WEB ЛР 2

## 1. Ідентифікація

- ПІБ і група: ____________________ (заповнити).
- Спільний baseline 2-A «Трекер інцидентів»; індивідуального номера немає.
- Заявлений рівень: **відмінний («Відмінно»)**; обрано до початку етапу 1.
- Гілка: `lab/2-input-sqli` (створена від `v0.1.0`).
- Квитанція `.scaffolds/lab-02.json`:
  - release: `lab-02-start-v1`;
  - base_commit: `9b87aa096c56ce9a7244c773cb9cf278a1cb3f8f` (це `v0.1.0`);
  - entry_parent: `9b87aa096c56ce9a7244c773cb9cf278a1cb3f8f`.
- Vulnerable commit: `4b87cf013a71fedb9cb21d1d958a22f0fe88cded` — «Validate create contract; preserve search evidence».
- Fixed commit коду: `b37cc32acbbce8bca5fc3bda881b6d10e8a42b99` — «Validate incident input and secure search query».
- Фінальний тег `v0.2.0` стоїть на commit зі звітом; `git diff b37cc32 v0.2.0 -- src tests` порожній.
- DEL-01: remote https://github.com/KirilYurk/securelab (гілка `lab/2-input-sqli`, тег `v0.2.0`); резервна копія — bundle `securelab-lr02.bundle`.

SDK: `dotnet --version` у корені дає 10.0.404. `global.json` уже містив потрібну політику (`10.0.100`, `latestFeature`, `allowPrerelease: false`), тому окремий SDK-commit не робився.

Встановлення scaffold:
- `py -3 install.py 02 …` (dry run) і `--write` вивели очікуваний перелік із 8 файлів і рядок «Scaffold installed…».
- `git diff` після встановлення — рівно 4 рядки: `using` і `app.MapLab02Endpoints();` у `Program.cs`, два виклики `Lab02Seed.EnsureAsync` у `DatabaseBootstrap.cs`.
- Перевірка контрольних сум `SHA256SUMS` архіву: 0 розбіжностей.

Після встановлення впав рівно один тест ЛР 1: `SeveritySummary_WithoutFilter_…`. Scaffold додав два Low-інциденти (`…0004`, `…0005`), тому підсумок змінився: Low = 3, Medium = 1, High = 1. Я змінив лише очікувані числа `[1,1,1,0] → [3,1,1,0]`, саму перевірку не послаблював.

## 2. Контракт і CP-01

Код: `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`, метод `CreateIncidentAsync` (рядки 100–201 у fixed commit). Початковий `app.MapPost` залишено тим самим маршрутом; дубля немає.

### Зовнішній контракт POST `/api/incidents`

| Поле | Хто задає | Правило | Помилка |
|---|---|---|---|
| `title` | клієнт | обов’язкове після `Trim()`; сирий рядок ≤ 160 | 400, ключ `title` |
| `description` | клієнт | обов’язкове після `Trim()`; сирий рядок ≤ 4000 | 400, ключ `description` |
| `severity` | клієнт | лише `Low`, `Medium`, `High`, `Critical` (без урахування регістру) | 400, ключ `severity` |
| `occurredAtUtc` | клієнт | обов’язкове; не пізніше `now + 5 хв`; зсув часового поясу допустимий, зберігається в UTC | 400, ключ `occurredAtUtc` |
| `id`, `ownerUserId`, `status`, `createdAtUtc`, `updatedAtUtc` | **лише сервер** | у `CreateIncidentRequest` відсутні (рядок 207); сервер задає `Guid.NewGuid()`, `DbSeeder.AliceId`, `New`, `now` | — |
| cross-field (T-09) | — | `High`/`Critical` ⇒ опис після `Trim()` ≥ 40 символів | 400, ключ `description` |
| конфлікт (T-03) | — | немає активного інциденту (`New`/`Triaged`/`InProgress`/`Resolved`) з тим самим title після `Trim()`, з урахуванням регістру; `Closed` не блокує | 409 без `errors` |
| власне правило (T-10) | — | `occurredAtUtc` не раніше `now − 90 днів` | 400, ключ `occurredAtUtc` |

Успіх: `201 Created`, заголовок `Location: /api/incidents/{id}`, тіло — новий record `CreatedIncidentResponse` (рядок 220). Поля тіла: `id`, `title`, `severity`, `status`, `occurredAtUtc`, `createdAtUtc`, `updatedAtUtc`. Entity не серіалізується: у відповіді немає `ownerUserId`, `description` чи навігаційних властивостей.

### Порядок обробки

Маршрут запиту: JSON → model binding у `CreateIncidentRequest` → `CreateIncidentAsync`.

1. `now = DateTimeOffset.UtcNow` фіксується один раз (рядок 106).
2. Базові перевірки збирають **усі** помилки в `Dictionary<string,string[]>`: title (рядок 110), description, severity (125), дата (135–137).
3. Нормалізація `Trim()` один раз (рядок 141). Далі використовуються лише нормалізовані значення.
4. Cross-field T-09 (рядок 149). Виконується лише тоді, коли severity коректна, а description ще не має власної помилки.
5. За наявності помилок — `Results.ValidationProblem(errors)` (рядок 156), **до** створення entity й до будь-якого доступу до БД. Тести перевіряють, що після 400 запис не з’явився (`CountByTitleAsync == 0`).
6. `AnyAsync` для конфлікту (рядок 161) → 409 через `Results.Problem` (рядок 171).
7. Створення entity: серверні поля (рядки 180–184), `OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime()` (рядок 185), потім `SaveChangesAsync`.
8. `Results.Created(...)` (рядок 192).

### Чому `TryParse` + `IsDefined`, і чому ще один крок

- `Enum.TryParse("7")` успішно повертає `(IncidentSeverity)7`, і без перевірки це значення потрапило б у БД. `Enum.IsDefined` відкидає його. Фактичний результат: `severity: "7"` → 400 з `errors.severity` (`evidence/t02-severity-7.txt`).
- Під час перевірки я знайшов, що рецепту `TryParse` + `IsDefined` недостатньо для цього enum:
  - `"3"` перетворюється на визначене значення `Critical`;
  - `"Medium,High"` дає `1 | 2 = 3`, теж `Critical`.

  Тому додав третю умову: назва розпізнаного значення має збігатися з уведеним рядком (рядок 127).
- Тест `T02_SeverityOutsideAllowlist_…` перевіряє `"7"`, `"3"`, `"Medium,High"` і `""`: усі дають 400 лише з ключем `severity`.

### Чому `<select>` у браузері не є контролем

`<select>` обмежує лише нашу сторінку. Будь-хто може надіслати POST напряму (curl, `.http`, DevTools) з довільним JSON, тому межу довіри задає тільки серверна перевірка.

### Серверні поля і overposting

- `CreateIncidentRequest` (рядок 207) містить лише чотири клієнтські поля. Зайві властивості JSON (`id`, `ownerUserId`, `status`, `createdAtUtc`) binder просто ігнорує, бо їм нема куди потрапити.
- Фактично: POST з `"id": "11111111-…"`, `"ownerUserId"` Bob, `"status": "Closed"`. Відповідь 201; GET за `Location` дав новий id, `status: "New"`, `ownerDisplayName: "Аліса Коваль"` (`evidence/a02-overposting.txt`, `evidence/a02-details.txt`).

## 3. Security-сценарій і CP-02/CP-03

### 3.1 Контекст і гіпотеза

`GET /api/incidents/search?q=&sortBy=` шукає підрядок у `title` і `description`.

Гіпотеза: значення `q` і `sortBy` потрапляють у текст SQL. Тоді введення може змінити не лише дані, а й **структуру** запиту.

### 3.2 Стан до (vulnerable commit `4b87cf0`)

Файл `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`, лямбда `MapGet("/api/incidents/search", …)` (рядок 18):
- рядки 25–26: `q` конкатенується в літерал `'%…%'` двічі (для title і description);
- рядок 23: гілка `_ => sortBy` передає сире значення в `ORDER BY`;
- рядок 27: готовий текст виконується через `db.Incidents.FromSqlRaw(sql)`.

Маршрут від query string до SQL: `q` з query string → параметр лямбди `string? q` → рядкова конкатенація → `FromSqlRaw` → PostgreSQL отримує одну суцільну команду, де дані й код не розділені.

### 3.3 Мінімальний PoC (лише локальний стенд, лише читання)

- Використано тільки дозволені методичкою запити A–D (`tests/http/lab-02-checks.http`, розділ «Етап 3/4»).
- Контрольний ввід — запит **C**. Текст payload у звіті не наводжу.
- Жодних DROP/DELETE/UPDATE/DDL, stacked statements чи нелокальних цілей.

### 3.4 Спостереження (CP-02)

| Запит | Передумови | Дія | Очікування без дефекту | Фактично (vulnerable) | Пояснення |
|---|---|---|---|---|---|
| A `q=USB` | reset seed, API на 5080 | GET | 200, один запис …0005 | 200, `[…0005]` | збіг лише в title …0005 |
| B `q=zz-no-match` | те саме | GET | 200, `[]` | 200, `[]` | збігів немає |
| C контрольний ввід | те саме | GET | 200, `[]` (такого тексту немає) | **200, усі 5 seed-записів** (…0001–…0005) | апостроф закрив літерал, далі введення стало частиною умови WHERE і зробило її завжди істинною; коментар відкинув решту |
| D `q=O'Brien` | те саме | GET | 200, «O'Brien classroom report» | **500** (Problem Details без деталей) | апостроф ламає синтаксис SQL; у журналі `42601: syntax error at or near "Brien"` |
| `q=комп'ютерного` | те саме | GET | 200, …0003 | **500** | той самий механізм на легітимному українському слові |
| `q=%25` (`%`) | те саме | GET | `[]` (символу `%` у даних немає) | 200, усі записи | `%` сприймається як шаблон LIKE |
| `sortBy=price` | те саме | GET | 400 | **500** | сире значення в `ORDER BY` → неіснуючий стовпець |

Докази:
- `evidence/s01-before-*.txt`;
- `evidence/s04-log-before.txt`: `Parameters=[]`, значення вклеєне в текст SQL.

Окремо: сама по собі відповідь 500 не доводить SQL-ін’єкцію. Доказом є запит C: він повертає 200 і **розширену вибірку**. Отже, введення змінило логіку запиту, а не лише спричинило помилку.

### 3.5 Першопричина

Дані (`q`) і структуру запиту (`sortBy`) зібрано в один рядок SQL конкатенацією. Конкретне місце: vulnerable commit `4b87cf0`, `Lab02Endpoints.cs`, рядки 25–26 і 23, виконання на рядку 27 через `FromSqlRaw`.

Фільтрувати апостроф чи слово `OR` — не виправлення:
- апостроф — легітимний символ (`O'Brien`, `комп'ютерного`), і його видалення ламає правильні запити (T-04);
- чорний список обходиться іншими формами запису;
- справжня причина — змішування коду й даних — від фільтра не зникає.

### 3.6 Виправлення (fixed commit `b37cc32`, CP-03)

Метод `SearchAsync` (рядки 32–89):
1. **Allowlist для структури** (рядок 40). `sortBy` лише з `createdAtUtc`, `severity`, `status`; порожнє значення означає `createdAtUtc`. Інше значення → 400 `ValidationProblem` з ключем `sortBy` **до** побудови запиту. Назву стовпця параметризувати неможливо (параметр — це завжди значення), тому для структури потрібен саме allowlist.
2. **Значення — лише як параметр.** LINQ `Where` з явним OR по `Title` і `Description` через `EF.Functions.ILike(field, pattern, "\\")` (рядки 53–54). EF Core передає `pattern` окремим параметром: у журналі `Parameters=[@pattern='?', @pattern0='?', …]` і `ILIKE @pattern ESCAPE '\'` (`evidence/s05-log-after.txt`).
3. **Літеральний пошук.** `EscapeLike` (рядок 93) екранує спершу `\`, потім `_` і `%`, а результат обгортає в `%…%`. Апостроф лишається звичайним значенням.
4. **Сортування рангами** (рядки 57–75). Тернарні ланцюжки EF перекладає в `CASE WHEN`; enum зберігається рядком, тому алфавітний порядок був би хибним. Після кожного варіанта йде `.ThenBy(item => item.Id)`, потім `.Take(50)` (обмеження рядків, не пагінація).
5. **Проєкція** в іменований record `IncidentSearchItemResponse` (рядок 211) з тією самою формою, що й стартовий пошук.

Різниця «до/після»:
- **До:** один зклеєний текст, у якому межі між кодом і даними визначає введення.
- **Після:** текст SQL сталий для будь-якого `q`, змінюється лише значення `@pattern`; структуру (`ORDER BY`) обирає сервер з allowlist.

Повний diff `git diff 4b87cf0..b37cc32 -- src` — у `evidence/cp03-diff.txt`.

### 3.7 Retest і позитивна регресія

| Запит | Після виправлення | Доказ |
|---|---|---|
| A `q=USB` | 200, той самий єдиний запис …0005 | `evidence/s02-after-A-usb.txt` |
| B | 200, `[]` | `evidence/s02-after-B-nomatch.txt` |
| C контрольний ввід | **200, `[]`** | `evidence/s02-after-C-control.txt` |
| D `O'Brien` | 200, «O'Brien classroom report» | `evidence/t04-after-D-obrien.txt` |
| `комп'ютерного` | 200, …0003 «Перевірка журналу комп'ютерного класу» | `evidence/t04-after-apostrophe-cyr.txt` |
| `%` | 200, `[]` | `evidence/t04-after-percent.txt` |
| без sortBy | 0004 → 0005 → 0003 → 0002 → 0001 | `evidence/sort-default.txt` |
| `sortBy=severity` | 0002 High → 0001 Medium → 0003, 0004, 0005 Low | `evidence/sort-severity.txt` |
| `sortBy=status` | 0003, 0004, 0005 New → 0001 Triaged → 0002 InProgress | `evidence/sort-status.txt` |
| `sortBy=price` | 400, `errors.sortBy`; у журналі для нього DbCommand немає | `evidence/t05-response.txt` |

Автоматичний S-02 (клас `Lab02SearchTests`) перевіряє точну множину результатів, а не лише «не 500»:
- `S02_ControlInputFromStage3_ReturnsEmptyResultSet` → `Assert.Empty`;
- позитивний контроль у тому самому класі: `S02_PositiveControl_…` → `Assert.Single`, `Id == …0005`.

Щоб переконатися, що тести справді ловлять дефект, я запустив їх на vulnerable commit (тимчасовий worktree). Результат: 8 із 13 Failed. Серед них S-02, обидва T-04 з апострофом, `%`/`_`, T-05 і два сортування. На fixed commit усі Passed (`evidence/s02-tests-vs-vulnerable.txt`).

### 3.8 Залишковий ризик

- **Авторизації немає.** Пошук повертає інциденти всіх власників; це тема ЛР 3–4 (IDOR).
- **Обмеження ресурсів.** `Take(50)` лише обмежує кількість рядків. Пагінації, обмеження довжини `q` і rate limit немає (ЛР 6). `ILIKE '%…%'` не використовує звичайний індекс, тож на великих даних це ризик навантаження.
- **Інші endpoints:** повний A-01 (розділ 4) не знайшов інших місць, де SQL збирається з введення.
- **Журнали.** У Development EF пише текст SQL. Значення параметрів приховані (`'?'`), бо `EnableSensitiveDataLogging` вимкнено, але в production рівень `Database.Command` варто знизити (ЛР 6).

## 4. Фактичні перевірки DEL-02

Стенд:
- PostgreSQL у Docker Compose (`healthy`);
- перед автотестами ручний API зупинено й виконано `--reset-database`.

| ID | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|
| T-01 | Коректне створення | reset; дата в межах 90 днів | POST з 4 коректними полями | 201, Location, DTO без зайвих полів | 201, `Location: /api/incidents/c18a2d7d-…`; тіло рівно з 7 полів, `status: New`; `ownerUserId` відсутній | `evidence/t01-response.txt`; тест `T01_ValidCreate_…` |
| T-02 | Некоректний DTO | — | порожній title, порожній description, `severity: "Urgent"`, без дати | 400 problem+json з ключами полів | 400, `application/problem+json`, `errors` має `title`, `description`, `severity`, `occurredAtUtc` одночасно; stack trace/SQL відсутні | `evidence/t02-all-invalid.txt`; тест `T02_InvalidDto_…` |
| T-02 | severity `"7"` | — | POST | 400, `errors.severity` | 400, лише `errors.severity`; запис не створено | `evidence/t02-severity-7.txt`; тест `T02_SeverityOutsideAllowlist_…` (7, 3, Medium,High, "") |
| T-02 | Відсутня дата / дата 2099 | — | POST | 400, `errors.occurredAtUtc` | 400, лише `errors.occurredAtUtc` в обох випадках | `evidence/t02-missing-date.txt`, `evidence/t02-future-date.txt`; тест `T02_OccurredAtMoreThan5Minutes…` |
| T-02 | Межа довжини title / зсув часового поясу | — | 161 vs 160 символів; дата з `+03:00` | 400 / 201; дата в UTC | 161 → 400 `title`, 160 → 201; `+03:00` → 201, `occurredAtUtc` з offset `+00:00` | `evidence/t02-offset-accepted.txt`; тести `T02_TitleLongerThan160_…`, `T02_OccurredAtWithOffset_…` |
| T-03 | Повторний активний title | перший POST → 201 | той самий title (також з пробілами) | 409 без `errors` | 409, problem+json, `title` «Інцидент уже зареєстровано», `detail` зрозумілий, `errors` немає; інший регістр → 201; після переведення першого в `Closed` → 201 | `evidence/t03-first-201.txt`, `evidence/t03-response.txt`, `evidence/t03-other-case-201.txt`; тест `T03_DuplicateActiveTitle_…` |
| T-04 | Пошук після виправлення | fixed, reset | USB, `O'Brien`, `комп'ютерного`, `%`, `_`, `\` | правильні записи, без 500 | USB → …0005; O'Brien → …0004; комп'ютерного → …0003; `%`, `_`, `\` → `[]` | `evidence/t04-after-*.txt`; тести `T04_*` |
| T-05 | Невідомий sortBy | fixed | `sortBy=price` | 400 | 400, `application/problem+json`, `errors.sortBy` з переліком createdAtUtc, severity, status; stack trace і SQL відсутні | `evidence/t05-response.txt`; тест `T05_UnknownSortBy_…` |
| T-06 | Невідомий id | — | GET `/api/incidents/99999999-…` | 404 problem+json | 404, `title` «Інцидент не знайдено», `detail` з id; внутрішніх деталей немає | `evidence/t06-response.txt` |
| T-09 | Межа 39/40 | — | High з описом 39 і 40 символів | 400 `description` / 201 | 39 → 400 лише `errors.description`, запис не створено; 40 → 201; Critical 39 → 400; Medium 39 → 201 | `evidence/t09-39-rejected.txt`, `evidence/t09-40-accepted.txt`; тест `T09_…` |
| T-10 | Власне правило: вікно 90 днів | — | дата 2026-01-01 (> 90 днів) і 89 днів тому | 400 `occurredAtUtc` / 201 | 2026-01-01 → 400 лише `errors.occurredAtUtc`; 89 днів → 201; автотест: 91 → 400, 89 → 201 | `evidence/t10-old-rejected.txt`, `evidence/t10-89days-accepted.txt`; тест `T10_…` |
| S-01 | Контрольний ввід до виправлення | vulnerable `4b87cf0`, reset | запит C | розширена вибірка | 200, усі 5 seed-записів; `O'Brien` → 500 | `evidence/s01-before-C-control.txt`, `evidence/s04-log-before.txt` |
| S-02 | Той самий ввід після виправлення | fixed `b37cc32`, reset | запит C | `[]` | 200, `[]`; позитивний контроль USB → рівно …0005 | `evidence/s02-after-C-control.txt`; тести `S02_*`; `evidence/s02-tests-vs-vulnerable.txt` |
| A-01 | Огляд доступу до даних | fixed | `data-access-scan.py` + ручний огляд `src` | файл/метод, категорія, вердикт | див. таблицю A-01 нижче | `evidence/a01-scan.txt` |
| A-02 | Overposting | fixed | POST із `id`, `ownerUserId`, `status: Closed`, `createdAtUtc` | поля ігноруються | 201; GET: новий id ≠ надісланого, `status: New`, `ownerDisplayName` «Аліса Коваль», `createdAtUtc` — поточний | `evidence/a02-overposting.txt`, `evidence/a02-details.txt`; тест `A02_ServerManagedFieldsInBody_AreIgnored` |

### T-10: власне правило

Правило:
- `occurredAtUtc` не може бути раніше ніж за 90 днів до поточного UTC-часу сервера;
- межа `now − 90 днів` з тим самим `now`, що й для інших перевірок;
- помилка: 400, ключ `occurredAtUtc`;
- перевіряється в тому самому handler до доступу до БД (рядок 137).

Обґрунтування: форма призначена для оперативних повідомлень. Давній інцидент потребує окремого ретроспективного розбору, а його пізня реєстрація звичайною формою спотворює статистику реагування.

Правило не послаблює 2-A: усі базові перевірки, T-03 і T-09 залишились без змін.

Приклади:
- допустимий: дата 89 днів тому → 201;
- недопустимий: `2026-01-01T00:00:00Z` → 400.

### A-01: повний огляд доступу до даних (fixed commit)

`data-access-scan.py` (шаблони `FromSqlRaw`, `ExecuteSqlRaw`, `ORDER BY`, `$"SELECT`, `+ query`) знайшов 4 збіги. Решту точок я знайшов ручним оглядом усіх звернень до `DbContext` у `src`.

| Файл / метод | Рядок | Категорія | Вердикт |
|---|---|---|---|
| `Data/DatabaseBootstrap.cs`, `InitializeAsync` (reset) | 29 | raw SQL (`ExecuteSqlRawAsync`) | **безпечно**: сталий рядок `TRUNCATE … CASCADE` без жодного введення; виконується лише з `--reset-database` і `AllowReset=true` |
| `Scaffolding/Lab02Endpoints.cs` | 38 | збіг «ORDER BY» у коментарі | хибний збіг (коментар) |
| `tests/…/Lab02SearchTests.cs` | 19 | `+ query` | хибний збіг: це URL тестового HTTP-запиту, не SQL |
| `tests/…/Lab02SearchTests.cs` | 97 | «ORDER BY» | хибний збіг: перевірка, що 400 не розкриває SQL |
| `Scaffolding/Lab02Endpoints.cs`, `SearchAsync` | 51–83 | LINQ + `EF.Functions.ILike` | **безпечно**: значення — параметр `@pattern`, структура — allowlist |
| `Scaffolding/Lab02Endpoints.cs`, `CreateIncidentAsync` | 161, 189–190 | LINQ `AnyAsync`, `Add` + `SaveChangesAsync` | безпечно (параметризовано EF); обмеження гонки — у розділі 5 |
| `Application/Incidents/IncidentQueries.cs`, `GetListAsync` | 16 | LINQ, фільтр status через enum | безпечно: status попередньо пройшов allowlist `TryParseStatus` |
| `Application/Incidents/IncidentQueries.cs`, `GetDetailsAsync` | 38 | LINQ за `Guid` | безпечно: route constraint `{id:guid}` |
| `Application/Incidents/IncidentQueries.cs`, `GetSeveritySummaryAsync` (ЛР 1) | 71 | LINQ `GroupBy`/`Count` | безпечно |
| `Program.cs`, `/health` | 54 | `Database.CanConnectAsync` | безпечно, без введення |
| `Data/DatabaseBootstrap.cs` | 26, 42 | `MigrateAsync` | безпечно, міграції з коду |
| `Data/DbSeeder.cs`, `SeedAsync` | 18, 93–126 | LINQ/`AddRange` зі сталими seed-даними | безпечно |
| `Scaffolding/Lab02Seed.cs`, `EnsureAsync` | 16–27 | LINQ/`Add` зі сталими даними | безпечно |
| `Data/Migrations/*` | — | `migrationBuilder` без `.Sql(...)` | безпечно |

Інших конкатенацій SQL немає, тож для A-02 обрано overposting.

### A-02: overposting

**До:**
- Серверні поля `id`, `ownerUserId`, `status`, `createdAtUtc` (і `updatedAtUtc`) у `CreateIncidentRequest` відсутні (`Lab02Endpoints.cs:207`).
- Відтвореного дефекту в коді не було: ризик полягав би в тому, щоб додати такі поля в DTO або зв’язувати entity напряму.

**Після:**
- Автотест `A02_ServerManagedFieldsInBody_AreIgnored` надсилає ці поля й перевіряє:
  - новий id ≠ надісланого;
  - `status == "New"`;
  - `ownerDisplayName == "Аліса Коваль"`;
  - `createdAtUtc` — поточний час.
- Локальна перевірка чутливості (не комічена): тимчасово додав у DTO `string? Status` і `Status = Enum.Parse<IncidentStatus>(request.Status ?? "New")`. Тест упав з `Expected: "New"`, `Actual: "Closed"`. Відновлено через `git restore` (`evidence/a02-sensitivity.txt`).

### Build і тести після останньої зміни коду

- `dotnet build tests/SecureLab.Api.Tests -c Release`: 0 Warning(s), 0 Error(s).
- `dotnet test tests/SecureLab.Api.Tests -c Release`: **Passed 46, Failed 0, Skipped 0** (`evidence/tests-final.txt`).

Склад тестів:
- 10 тестів ЛР 1, включно з оновленим очікуванням severity-summary;
- `Search_ReturnsJson` зі scaffold;
- 17 тестів `Lab02CreateIncidentTests`;
- 18 тестів `Lab02SearchTests`.

Навчальний `ContractExampleTests.cs` до проєкту не додавався.

## 5. Обмеження і висновок

**Що перевірено:**
- повний контракт POST: усі базові правила, T-09, T-03, T-10, overposting;
- єдиний безпечний формат помилок 400/404/409;
- відтворення та усунення SQL-ін’єкції в пошуку;
- літеральний пошук `%`, `_`, `\`;
- allowlist і порядок сортування.

Усе це перевірено автотестами (46/46) і ручними HTTP-запитами з `tests/http/lab-02-checks.http`.

**Обмеження:**
- **Гонка в T-03.** Перевірка `AnyAsync` перед `SaveChangesAsync` не гарантує інваріант за паралельних запитів: два одночасні POST з однаковим title можуть обидва пройти перевірку. У реальному застосунку потрібне обмеження БД (наприклад, частковий унікальний індекс за title для активних статусів) з обробкою конфлікту або серіалізована транзакція. У ЛР 2 migration навмисно не додавалась.
- **Помилки читання JSON.** Обрізаний JSON або `"severity": 7` (число замість рядка) дають 500 з безпечним Problem Details. Причина — `BadHttpRequestException`/`JsonException` у binder ще до мого handler (`evidence/limit-malformed-json.txt`). Внутрішні деталі не розкриваються. Як доказ T-02 це не використовується: валідацію в handler воно не перевіряє.
- **Правило T-10 залежить від часу.** Сталі дати в `tests/http/lab-02-checks.http` (`@occurred`) з часом вийдуть за 90-денне вікно, тому змінну потрібно оновлювати; автотести використовують відносні дати.
- **Інші ризики:** авторизації, пагінації й обмеження частоти запитів немає (див. 3.8).

**Висновок.**

Межу довіри для створення інциденту перенесено на сервер:
- окремий request DTO без серверних полів;
- повна валідація з усіма помилками разом до доступу до БД;
- окремий response DTO.

SQL-ін’єкцію в пошуку відтворено на vulnerable commit і усунено на fixed commit:
- значення передаються лише параметрами;
- структура запиту — лише з серверного allowlist.

Регресійні тести перевіряють точні множини результатів і падають на вразливому коді.

## 6. Декларація використання ШІ

| Сервіс | Задачі | Авторська адаптація/інтерпретація | Фактична перевірка |
|---|---|---|---|
| Claude (Anthropic), Claude Code | Стислий виклад методички; допомога з вирішенням помилок; написання .md файлів; автоматизація сетапу стенду та виконання команд у терміналі; підказки щодо написання коду | Склав таблицю контракту 2-A й порядок перевірок; обрав і обґрунтував власне правило T-10 (вікно 90 днів); виявив, що `TryParse` + `IsDefined` пропускає `"3"` і `"Medium,High"`, і додав перевірку назви; відтворив S-01 лише дозволеними запитами A–D; пояснив першопричину й чому фільтр апострофа не є виправленням; перевірив чутливість тестів (A-02 з тимчасовим полем `Status`; S-02 на vulnerable commit); класифікував кожен збіг A-01 | Build 0 warnings; 46/46 тестів; ручні HTTP-запити T-01…T-10, S-01/S-02, A-01/A-02; журнали EF до/після (`Parameters=[]` → `@pattern`); 8/13 тестів пошуку падають на vulnerable commit |

## 7. Передавання

- Remote https://github.com/KirilYurk/securelab: гілка `lab/2-input-sqli` і тег `v0.2.0` надіслано. Bundle `securelab-lr02.bundle` перевірено через `git bundle verify` («is okay») і `list-heads` (`lab/2-input-sqli`, `main`, `v0.1.0`, `v0.2.0`).
- У Moodle: DEL-01 (посилання на remote або bundle) і DEL-02 (цей звіт із `docs/evidence/`).
- Git diff та історію перевірено на секрети. Нових секретів, `.env`, дампів і журналів у Git немає; повні журнали API зберігаються поза репозиторієм.
