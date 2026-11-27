# SecureLab starter: трекер інцидентів

Це baseline starter для дисципліни «Прикладні технології програмування в
інформаційній безпеці», варіант **2-A**. Він містить один простий
трирівневий моноліт, локальний PostgreSQL, мінімальний Vanilla JavaScript
клієнт, відтворювані seed/reset і готовий `WebApplicationFactory` test harness.

Це стан системи **до ЛР 1**. Далі студент послідовно розвиває власний проєкт:
результат завершеної ЛР стає основою наступної. Якщо попередню роботу технічно
не завершено, її потрібно завершити або виправити перед переходом далі.

Starter навмисно не містить завершених рішень ЛР 1–6, автентифікації,
авторизації або навмисних уразливостей. Потрібні для конкретної роботи зміни
студент виконує у власному проєкті за методичними вказівками й лише у
дозволеному локальному середовищі.

## 1. Передумови

- остання стабільна .NET 10 SDK; `global.json` із `10.0.100`, `latestFeature`, `allowPrerelease: false` обирає найвищу встановлену стабільну SDK сімейства `10.0`;
- Docker із підтримкою `docker compose`;
- Git;
- вільні локальні порти `5080` і `54329`.

Перевірте:

```bash
dotnet --version
docker --version
docker compose version
```

## 2. Перший запуск

Starter одержують через приватний remote або `lab-01-starter.zip`. Для ZIP прочитайте `START-HERE.md` і створіть локальний Git-репозиторій у `starter-project/` за етапом 1 методички; університетський Git-сервер для локальної роботи не потрібен. Незалежні мости лежать поряд у `one-time-examples/`, а готові консольні приклади й команди — у `resources/copyable/`. До starter їх не додають. Тег `v0.1.0` створюють після виконання ЛР 1. Без remote результат передають через Git bundle і звіт у Moodle за розділом подання.

У корені starter виконайте:

```bash
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet tool restore
dotnet restore SecureLab.sln
dotnet run --project src/SecureLab.Api
```

`dotnet tool restore` за успішного відновлення в англомовному виводі завершується `Restore was successful.` Може також з’явитися інформаційна підказка `A newer version of tool 'dotnet-ef' is available (…). Consider updating your manifest file.` Її наявність залежить від доступних відомостей про версії; це не помилка. Для цієї ЛР залиште версію з `.config/dotnet-tools.json`: підказка не є командою оновити manifest. Якщо restore завершився з помилкою, збережіть її текст і перевірте мережу та джерело пакетів. [Довідка Microsoft](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-restore).

Під час першого Development-запуску API застосує готову EF Core migration і
додасть фіксовані навчальні дані. Відкрийте:

- browser client: <http://localhost:5080/>;
- health/readiness: <http://localhost:5080/health>;
- інтерактивний OpenAPI: <http://localhost:5080/scalar/v1>;
- OpenAPI JSON: <http://localhost:5080/openapi/v1.json>.

Зупиніть API через `Ctrl+C`. Контейнер БД зупиняється командою:

```bash
docker compose --env-file infra/.env.example -f infra/compose.yaml down
```

### Запуск і дослідження в IDE

Основні середовища — Visual Studio 2026 Community у Windows 11 із workload ASP.NET and web development або актуальний Rider у Windows/macOS/Linux. VS Code із C# Dev Kit — резервний шлях, якщо повну IDE не можете або не хочете встановлювати. Незалежно від вибору виконайте дві перевірки SDK нижче.

Для Visual Studio 2026 на клієнтській Windows потрібна 64-розрядна Windows 11; Windows 10 не входить до [офіційно підтримуваних ОС VS2026](https://learn.microsoft.com/en-us/visualstudio/releases/2026/vs-system-requirements). Версію Windows перевірте через Win+R → `winver`. У Windows 10 оберіть актуальний Rider, який [підтримує Windows 10/11](https://www.jetbrains.com/help/rider/Installation_guide.html), або резервний VS Code з C# Dev Kit. Visual Studio 2022 не підтримує ціль `net10.0` цього starter; це обмеження проєкту, а не заборона Windows 10 для інших робіт.

Для навчальної роботи достатньо [Visual Studio Community](https://visualstudio.microsoft.com/vs/community/), безкоштовної для такого використання; Professional чи Enterprise не потрібні. У Rider оберіть безкоштовну ліцензію **Non-commercial use** для особистого навчання або освітню ліцензію JetBrains; платна підписка чи trial для ЛР не потрібні. [Умови JetBrains](https://sales.jetbrains.com/hc/en-gb/articles/18950890312210-The-free-non-commercial-licensing-FAQ).

**Що тут налаштовується.** .NET SDK — набір інструментів розробника: компілятор C#, MSBuild і команда `dotnet`. Для програміста C++ це приблизно компілятор разом із системою збирання. Runtime лише виконує програму й не замінює SDK. `net10.0` у `.csproj` позначає цільову платформу .NET 10 і доступні програмі API; це не номер компілятора чи версія мови C#. [Цільові платформи Microsoft](https://learn.microsoft.com/en-us/dotnet/standard/frameworks).

На комп’ютері можуть співіснувати кілька SDK. `global.json` у корені проєкту визначає, яку з них використовує `dotnet` саме тут. Наприклад, у `10.0.302` частина `10.0` — сімейство .NET, `3xx` — смуга випусків SDK (feature band), `02` — номер виправлення в цій смузі. `latestFeature` дозволяє новіші смуги в межах `10.0`, а `allowPrerelease: false` виключає Preview/RC. Наш файл обирає найновішу встановлену стабільну SDK 10.0, не старішу за `10.0.100`; тому достатньо встановити останню стабільну .NET 10 SDK.

Установіть останню стабільну **.NET 10 SDK** з [офіційної сторінки Microsoft](https://dotnet.microsoft.com/download/dotnet/10.0), у блоці **Build apps — SDK**, для своєї ОС та архітектури. Runtime окремо не замінює SDK. Не обирайте Preview або RC. Поточний номер випуску змінюється, тому його тут не фіксуємо.

**Якщо у вашому `global.json` указано `"version": "10.0.302"`** (наприклад, ви розпакували архів курсу раніше). Збережіть свою роботу; відкрийте наявний `global.json` у корені власного проєкту й замініть лише об’єкт `sdk` наведеним нижче. Інші ключі, якщо вони є, збережіть. Правильний об’єкт має такий вигляд:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

`10.0.100` — нижня межа, а не вимога встановити саме цей старий випуск. `latestFeature` обирає найвищу встановлену стабільну SDK з major/minor `10.0`, зокрема зі смуг `1xx`, `3xx` або `4xx`; `allowPrerelease: false` виключає Preview/RC. SDK 9 чи 11 не замінює SDK 10 для цього проєкту. `net10.0`, версії NuGet, `packages.lock.json` і `.config/dotnet-tools.json` залишаються без змін. [Правила Microsoft для global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) пояснюють вибір SDK окремо від target framework.

**Дві різні перевірки.** В окремому терміналі поза проєктом виконайте:

```bash
dotnet --list-sdks
```

Серед рядків має бути стабільна `10.0.… [шлях]` без `-preview` чи `-rc`. Це перелік SDK, доступних саме цьому виконуваному файлу `dotnet`, а не доказ вибору проєкту. Потім у корені проєкту з оновленим `global.json` виконайте:

```bash
dotnet --version
```

Очікуйте один номер `10.0.…` без суфікса попереднього випуску — найвищу сумісну встановлену SDK. Якщо бачите `A compatible .NET SDK was not found`, перевірте збережений файл, установлення SDK і шлях до `dotnet`. Якщо повідомлення ще містить `Requested SDK version: 10.0.302`, залишилася стара копія `global.json` або команда виконується не в потрібному каталозі. Перевірте поточний каталог і `global.json` у ньому та вище: CLI шукає цей файл також у батьківських каталогах. Не видаляйте його для обходу помилки. Рядок `global.json file:` у тому самому повідомленні показує повний шлях до прочитаного файла: перевірте саме його.

Якщо виправили старий файл у Git-проєкті, перегляньте `git diff -- global.json`, переконайтеся, що diff містить лише політику SDK, і збережіть цю зміну окремим commit. Після наведених команд `git status --short` не має нічого друкувати:

```bash
git add global.json
git commit -m "Update .NET 10 SDK selection policy"
git status --short
```

Цей блок потрібен лише після фактичної зміни старого файла; для нового незміненого файла commit не потрібний. Після перерви знову перевірте `git status` і `dotnet --version` у тому самому корені; не розпаковуйте starter поверх власної роботи; якщо встановлена стабільна SDK 10 уже відповідає політиці, повторно встановлювати її не потрібно.

**Коли CLI працює, а IDE не бачить SDK.** Оновіть Visual Studio через Visual Studio Installer до стабільної версії, сумісної з обраною SDK, з workload **ASP.NET and web development**. Підтримка `net10.0` від 18.0 — мінімум для target; новіша смуга SDK може потребувати новішої IDE/MSBuild, тому звірте [таблицю Microsoft SDK і Visual Studio](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs). Для Rider також установіть актуальну стабільну версію з підтримкою .NET 10. У Settings (у macOS — Rider → Settings…) → **Build, Execution, Deployment → Toolset and Build** перевірте **.NET CLI executable path**. Це шлях до `dotnet`/`dotnet.exe`, а не до каталогу `sdk/10.0.…`; укажіть виконуваний файл тієї інсталяції, яка показує SDK 10 у `--list-sdks`. Знайти його можна командою `where.exe dotnet` у Windows або `command -v dotnet` у macOS/Linux. [Довідка JetBrains про Toolset and Build](https://www.jetbrains.com/help/rider/Settings_Toolset_and_Build.html) описує автоматичне визначення й ручний шлях.

Після встановлення SDK, зміни PATH або шляху Rider повністю закрийте й перезапустіть IDE та її термінал, відкрийте те саме `SecureLab.sln` і повторіть `dotnet --version` та build. Якщо термінал успішний, а Build IDE ні, збережіть її діагностику SDK/MSBuild: це різні механізми пошуку. До з’ясування використовуйте термінальний build/run із цієї методички. Для резервного VS Code оновіть C# Dev Kit і його залежності та також перезапустіть редактор.

Відкрийте `SecureLab.sln`, виберіть `SecureLab.Api` і профіль `http`. Visual Studio: Set as Startup Project → F5, журнал у консолі або Output → Debug; Rider: імпортована конфігурація `SecureLab.Api: http` → Debug, журнал у Run/Debug → Console. У VS Code відкрийте папку starter, `Program.cs` → F5 і виберіть API та `http`. Очікується `Now listening on: http://localhost:5080` і `Development`; браузер відкривайте вручну. Після зміни C# виконайте Stop → новий Run/Debug. Не запускайте два API одночасно.

На першому старті порожньої БД може бути `fail: … Failed executing DbCommand` для `__EFMigrationsHistory`, потім `Applying migration`: це очікувано лише коли схема створюється, далі з’являється `Now listening on` і `/health` повертає `200` із `{"status":"ready"}`. Інші `fail:`, `Unhandled exception` або відсутність запуску потребують діагностики.

У методичці ЛР 1, етап 1, крок 4а є повний маршрут IDE, а в етапі 2, кроці 3 — точка зупинки й перегляд `id`/`null`. Для зміненого порту PostgreSQL використовуйте термінальні команди зі змінною `ConnectionStrings__SecureLab`; не записуйте connection string у відстежуваний `launchSettings.json`. Reset залишається разовою термінальною командою.

## 3. Відомий початковий стан

Seed містить Alice, Bob, Morgan (Analyst) та Admin, три інциденти, два
публічні коментарі й одну зміну статусу. UUID не випадкові: їх можна
використовувати у `.http`-сценаріях і тестах.

Локальні значення `securelab / local-study-password` є лише відкритими
навчальними credentials для БД, прив'язаної до `127.0.0.1`. Це не секрет і
його заборонено повторно використовувати для іншої БД чи середовища. Реальний
connection string передавайте поза Git через `ConnectionStrings__SecureLab`.

Щоб застосувати наявні migrations, **очистити лише відомі навчальні таблиці**
локальної БД і повернути seed до відомого стану:

```bash
dotnet run --project src/SecureLab.Api -- --reset-database
```

Команда навмисно відмовляється працювати поза явно налаштованим Development
environment. Вона знищує поточні локальні навчальні дані у відомих таблицях
БД `securelab`, але не видаляє саму БД або її схему.

## 4. Перевірка

Коли PostgreSQL уже працює:

```bash
dotnet build src/SecureLab.Api/SecureLab.Api.csproj --configuration Release
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
```

**Коли всі тести червоні.** Якщо падають усі чотири baseline-тести, навіть `ClientScript_DoesNotUseDangerousInnerHtmlSink`, і повідомлення містить `Npgsql.NpgsqlException : Failed to connect to 127.0.0.1:54329` та відмову socket-з’єднання, спершу перевірте доступність БД. У macOS/Linux типовий текст — `Connection refused`; у Windows — `System.Net.Sockets.SocketException` і `No connection could be made because the target machine actively refused it` або локалізований відповідник. Спільний орієнтир — `Failed to connect to 127.0.0.1:<port>`, де `<port>` — фактичний порт підключення. [Код WSAECONNREFUSED у документації Microsoft](https://learn.microsoft.com/en-us/windows/win32/winsock/windows-sockets-error-codes-2). Причиною може бути зупинена PostgreSQL або інший порт, а не зміна тестованого коду. Тестова фабрика запускає весь застосунок у Development; під час старту він застосовує migration до навчальної БД. Навіть перевірка JavaScript тому залежить від успішного старту застосунку.

Приклад скороченої діагностики англомовного запуску без доступної БД (формат і час залежать від test runner):

```text
Npgsql.NpgsqlException : Failed to connect to 127.0.0.1:54329
Connection refused
Failed: 4, Passed: 0, Skipped: 0, Total: 4
```

Запустіть PostgreSQL командою першого запуску вище, дочекайтеся `healthy` у `docker compose ... ps` і звірте порт із підключенням тестів. За зміненого порту задайте той самий `ConnectionStrings__SecureLab` у терміналі тестів. Після цього повторіть тести; якщо помилка інша або лишилася, діагностуйте її за фактичним повідомленням.

На macOS/Linux або у Git Bash увесь сценарій підняття PostgreSQL і тестів
виконує одна команда:

```bash
bash scripts/test.sh
```

HTTP-сценарії для ручної перевірки розташовані у `tests/http/incidents.http`.
Вони охоплюють `200`, `400`, `404` та початковий `501` для точки розширення.

## 5. Завдання ЛР 1 у starter

Працюючий близький приклад — фільтр інцидентів за `status` через browser → API
→ EF Core → PostgreSQL → JSON → безпечні DOM sinks. Обмежена точка зміни —
`GET /api/incidents/severity-summary`, який у baseline повертає `501`.

Під час ЛР 1 студент реалізує підсумок кількості інцидентів за severity,
викликає endpoint із клієнта, безпечно відображає результат, додає
відтворюваний HTTP-сценарій або адаптує інтеграційний тест і доповнює
`docs/architecture.md`. Зміна схеми БД для мінімального маршруту не потрібна.

## 6. Структура

```text
src/SecureLab.Api/
  Presentation/   endpoints і зовнішні response contracts
  Application/    сценарії застосунку
  Data/           DbContext, entities, migration, seed/reset
  Client/         HTML/CSS/Vanilla JavaScript
tests/
  SecureLab.Api.Tests/   інтеграційні й security regression приклади
  http/                  ручні HTTP-сценарії
infra/                   локальний PostgreSQL
docs/                    карта архітектури, варіант, матриця доступу, звіт
```

Це не Clean/Onion Architecture, DDD, мікросервіси або вимога TDD. Каталоги
позначають відповідальність коду в одному server project.

## 7. Типові проблеми запуску

| Ознака | Перевірка |
|---|---|
| `Failed to connect` / `connection refused` / Windows `actively refused` до PostgreSQL | `docker compose ... ps`, порт `54329`, стан healthcheck |
| SDK не знайдено | `dotnet --list-sdks` поза проєктом має містити стабільну SDK 10; `dotnet --version` тут — найвищу встановлену `10.0.…`. Якщо `global.json` містить `10.0.302`, оновіть об’єкт `sdk` за інструкцією вище |
| порт API зайнятий | зупиніть непотрібний процес на `5080`; звичайний запуск читає `applicationUrl` із `launchSettings.json`, тому одного `ASPNETCORE_URLS` недостатньо; узгодження іншого URL описано в етапі 1 |
| migration не застосовується | перевірте `ASPNETCORE_ENVIRONMENT=Development` і connection string |
| змінили credentials у compose | узгоджено перевизначте `ConnectionStrings__SecureLab`; не записуйте реальне значення до Git |

Перед commit перегляньте `git status`, `git diff --staged` і переконайтеся, що
до Git не потрапили `.env`, cookies, tokens, реальні passwords, dumps або logs.
