# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасуванням запиту,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку або деталей інциденту |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Підготовлений наскрізний маршрут

```text
submit/click у Client/app.js
  → GET /api/incidents або GET /api/incidents/{id}
  → Presentation/Endpoints/IncidentEndpoints.cs
  → Application/Incidents/IncidentQueries.cs
  → Data/SecureLabDbContext.cs
  → PostgreSQL
  → response DTO у Presentation/Contracts/
  → JSON
  → textContent/createTextNode у Client/app.js
```

## Межі довіри

Доповніть таблицю щонайменше трьома конкретними спостереженнями.

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач контролює введення (вибір `status` у формі, кліки) | Клієнт лише кодує значення через `encodeURIComponent`; клієнтська перевірка не є захистом |
| Browser client → API | Клієнт і HTTP-запит можна змінити поза UI (curl, DevTools Console): `?status=`, `{id}` у шляху | `IncidentEndpoints`: route constraint `{id:guid}`, allowlist `status` через `Enum.TryParse` + `Enum.IsDefined` → 400 Validation Problem Details |
| API → PostgreSQL | Значення фільтра походить від клієнта | EF Core LINQ → параметризований SQL; фільтр — уже типізований `IncidentStatus`, а не рядок |
| PostgreSQL → API → Browser | Сутність містить `OwnerUserId`, internal comments тощо | Проєкція в DTO (`IncidentDetailsResponse`, `IncidentSeveritySummaryResponse`) — назовні лише дозволені поля |
| Response → DOM | У БД може зберігатися раніше введений недовірений текст (seed-інцидент №3 містить `<script>`) | `textContent` / `createTextNode` у `Client/app.js`; `innerHTML` заборонено (тест `ClientScript_DoesNotUseDangerousInnerHtmlSink`) |

## Маршрут ЛР 1: підсумок за severity

```text
click #load-severity-summary у Client/index.html
  → loadSeveritySummary() у Client/app.js (стан «Завантаження…»)
  → GET /api/incidents/severity-summary[?status=...]
  → Presentation/Endpoints/IncidentEndpoints.cs → GetSeveritySummaryAsync
      (allowlist status → 400; traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier)
  → Application/Incidents/IncidentQueries.cs → GetSeveritySummaryAsync
      (AsNoTracking → Where(status) → GroupBy(Severity) → Count() → ToListAsync)
  → Data/SecureLabDbContext.cs → DbSet Incidents → таблиця incidents (PostgreSQL)
      SQL: SELECT severity, count(*) FROM incidents [WHERE status = @p] GROUP BY severity
  → доповнення відсутніх груп нулями в пам'яті → IncidentSeveritySummaryResponse[]
  → JSON [{ "severity": "Low", "count": 1 }, ...]
  → li.textContent = `${severity}: ${count}` у Client/app.js
```

Контракт: `GET /api/incidents/severity-summary`, необов'язковий `status`
(New, Triaged, InProgress, Resolved, Closed; інше → 400 з ключем `errors.status`).
Відповідь `200 application/json` — масив `{ severity, count }` без опису, власника чи коментарів.

- **Політика нульових груп:** повний список значень `IncidentSeverity`; відсутні групи мають `count = 0`.
  Для seed: `Low 1, Medium 1, High 1, Critical 0`. Для `?status=Resolved` — усі нулі.
- **Порядок:** стабільний, за зростанням критичності (порядок оголошення enum): Low, Medium, High, Critical.
  Сортування виконується в пам'яті після матеріалізації, тому не залежить від текстового сортування SQL.
- **Журнал:** структурований запис `Built severity summary with {GroupCount} non-empty groups for status filter {Status}, trace {TraceId}` без опису інцидентів і секретів.
- **Стани UI:** «Завантаження…», «Даних немає» (порожній масив або всі нулі), фіксоване повідомлення про помилку без деталей сервера.

## Ключові файли

| Шар | Файл |
|---|---|
| Client | `src/SecureLab.Api/Client/index.html`, `src/SecureLab.Api/Client/app.js` |
| Endpoint | `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs` |
| Application | `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs` |
| DTO | `src/SecureLab.Api/Presentation/Contracts/IncidentResponses.cs` |
| Data | `src/SecureLab.Api/Data/SecureLabDbContext.cs` (`ToTable("incidents")`) |
| Перевірка | `tests/SecureLab.Api.Tests/IncidentEndpointTests.cs`, `tests/http/incidents.http` |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.

### Залежності між конфігураційними входами

- `global.json` (`10.0.100`, `rollForward: latestFeature`, без prerelease) змушує `dotnet` у цьому каталозі
  обрати найвищу встановлену стабільну SDK сімейства 10.0; без такої SDK збирання зупиняється.
- `ConnectionStrings__SecureLab` (змінна середовища) має вищий пріоритет за `appsettings.Development.json`,
  а той — за `appsettings.json`; тому реальний рядок підключення можна передати поза Git.
- Порт `127.0.0.1:${POSTGRES_PORT:-54329}` у `infra/compose.yaml` (значення з `infra/.env.example`)
  має збігатися з `Port=54329` у connection string; якщо змінити один без іншого — `connection refused` і `/health` → 503.
- `Database:ApplyMigrationsOnStartup` і `Database:AllowReset` дозволені лише в Development-конфігурації.

## Відновлення seed

Зупиніть API і виконайте з кореня репозиторію:

```bash
dotnet run --project src/SecureLab.Api -- --reset-database
```

Команда працює лише в Development, очищає відомі навчальні таблиці й повторно заповнює seed.
