# Конфигурация окружений

## Источники и приоритет

Хост читает `src/QaAgent.Host/appsettings.json`, затем `appsettings.{Environment}.json`, затем переменные окружения .NET с разделителем `__`. `ASPNETCORE_ENVIRONMENT` выбирает `Development`, `Demo` или `Production`. Файлы `.env.<environment>` предназначены для локального запуска или Docker Compose: launcher экспортирует их как переменные процесса **до** старта .NET; стандартный ASP.NET Core не загружает `.env` автоматически. На сервере те же переменные задаются менеджером секретов/окружением процесса.

В Git хранятся `.env.<environment>.example` без значений секретов. Локальные `.env.development`, `.env.demo`, `.env.production` игнорируются и создаются из соответствующего примера. Не нужно коммитить заполненные файлы.

## Разделение настроек

| В `appsettings` | В `.env` / переменных окружения |
| --- | --- |
| Лимиты времени; пути артефактов; число worker; параметры контейнера тестового агента; профиль (версия, режим исполнения, каталоги ТЗ, тестов и отчёта) | `ConnectionStrings__Postgres`, ключ локального API, GitHub repository/branch/token, необязательные `Llm__CodexExecutable` и `Llm__Model`, bot/Telegram account/token/admin chat IDs, Bitrix URL/key, тестовые логины/пароли |

Внешние интеграции Telegram, bot и Bitrix оставлены как необязательные параметры для будущих этапов: они не участвуют в MVP и не должны мешать запуску при пустых значениях. При включении интеграции её конфигурация валидируется на старте. Неиспользуемые переменные не передаются в контейнеры тестов и приложения.

Для M2 `ILlmClient` запускает `codex exec` с JSON-схемой. `Llm__CodexExecutable` по умолчанию `codex`; `Llm__Model` необязательна (без неё работает модель по умолчанию CLI); `Llm__TimeoutSeconds` по умолчанию 600. HTTP URL и API-ключ в конфигурации агента больше не используются. CLI должен быть установлен и авторизован в той же учётной записи ОС, что запускает сервис. Подробности запуска, изоляции, ошибок и учёта токенов: [Codex CLI адаптер](codex-exec.md).

Живая лента запусков сохраняется в `Agent__ActivityRoot` (по умолчанию `activity/`, отдельно от публикуемых артефактов). В ней находятся сообщения Codex, в том числе фрагменты исходного кода проверяемого проекта; каталог доступен только учётной записи сервиса на Unix и игнорируется Git.

`appsettings.Demo.json` задаёт профиль `demo` версии 16 для `akkne/lead-qualification-bot`: проверяемые требования в `docs/qa`, разрешённый каталог тестов `tests/LeadQualification.Tests`, режим `lead-qualification` (сборка `LeadQualification.slnx` и xUnit v3 runner выполняются кодом `LeadQualificationM3Executor`, а не командами из конфигурации). Docker Desktop монтирует временный checkout и артефакты из `/tmp` в контейнер проверок. Для технических изменений без нового ID в спецификации M2 использует ID вида `CHANGE-<SHA>` и учитывает тесты из push; если они покрывают изменение, новые тесты не требуются, а имеющиеся проверяются в M3. M3 также запускает хост с `.env.ci.example`, ждёт HTTP 200 от `/health` и сохраняет `host-smoke.log` и `host-process.log` в артефактах. Статус `Passed` требует успешных restore, сборки, тестов и офлайн smoke при наличии измеренного числа тестов. Это соответствует CI-сценарию бота. Набор обязательных проверок зависит от режима исполнения (`ExecutionMode`): для `lead-qualification` `dotnet format`, живой стенд Telegram, браузерный JSON и Coverlet не требуются; см. [проверки и доказательства](quality.md).

Для локальной М1 поднимите PostgreSQL через `QA_POSTGRES_PASSWORD=<локальный пароль> docker compose up -d postgres`. Заполните игнорируемый `.env.demo`: `ConnectionStrings__Postgres` для `127.0.0.1:5433`, `AgentApi__Key`, `GitHub__Repository=owner/repo`, `GitHub__Branch=main` и при локальной проверке `GitHub__SourcePath` с путём к checkout. Без `GitHub__SourcePath` Git CLI клонирует `https://github.com/owner/repo.git`; приватный доступ на runner/хосте нужно заранее настроить через Git credential helper. `ASPNETCORE_URLS=http://127.0.0.1:5080` удерживает API на loopback. Запускайте `python3 scripts/run_agent.py demo`: он читает `.env.demo` без интерпретации как shell-кода и передаёт значения процессу .NET. Уже заданные переменные процесса имеют приоритет.

Хост применяет EF Core миграции при старте. Схема М1 находится в `src/QaAgent.Infrastructure/Persistence/Migrations/`; команда `dotnet tool restore` восстанавливает закреплённый в `dotnet-tools.json` инструмент для следующих миграций. `bash scripts/check.sh` проверяет, что модель БД и последняя миграция совпадают.

Workflow использует GitHub secret `QA_AGENT_API_KEY` и, для первого push с нулевым `before`, repository variable `QA_INITIAL_BASE_SHA`. Значение первого base SHA должно указывать на существующий commit. Сквозная локальная проверка `bash scripts/check_m1.sh` сама создаёт временные PostgreSQL и Git-репозиторий и не использует настоящие ключи.

После первой загрузки workflow можно запустить вручную через `workflow_dispatch`, указав два существующих SHA. Это позволяет проверить доставку без нового коммита; повтор того же диапазона вернёт прежний `runId`.

На демонстрационном MacBook PostgreSQL запущен через `docker compose`, API — через пользовательский LaunchAgent `com.qa-agent.host`, а GitHub runner с метками `[self-hosted, macOS]` — через его `svc.sh`. После изменения кода API перезапускается командой `launchctl kickstart -k gui/$(id -u)/com.qa-agent.host`; готовность проверяется через `http://127.0.0.1:5080/healthz`. Локальные настройки и ключ API находятся только в игнорируемом `.env.demo`. GitHub secret `QA_AGENT_API_KEY` и переменная `QA_INITIAL_BASE_SHA` настроены для приватного репозитория.

Для М4 `GitHub__Token` в `.env.demo` нужен для push QA-ветки, если у Git credential helper нет прав записи. Publisher передаёт токен Git через временный askpass-скрипт, не помещая его в URL или аргументы процесса. `GitHub__PublishRemote` можно задать для отдельного remote; по умолчанию используется `https://github.com/<GitHub__Repository>.git`. `Agent__ResultBaseUrl` задаёт локальный адрес страницы результата в Markdown-отчёте (по умолчанию `http://127.0.0.1:5080`). Страница открывает HTTP Basic запрос: в качестве пароля введите `AgentApi__Key`; логин может быть произвольным. Ссылки на локальные доказательства работают на MacBook с запущенным сервисом.

## Значения и проверка

- Профиль фиксирует версию. Из секции `Profiles:<id>` код читает только `ProfileVersion`, `ExecutionMode`, `SpecificationDirectory`, `AllowedTestDirectories`, `AllowedReportDirectory` (и `TestHints` для режима `agent`). Изменение этих значений требует увеличить `ProfileVersion`: он участвует в ключе идемпотентности, а запуск с другой версией worker отклоняет. Команды сборки, тестов и стенда в конфигурации не хранятся: их выполняет исполнитель выбранного режима.
- Лимиты, которые не настраиваются: до трёх попыток сборки одного набора у агента-писателя (правило prompt), до 30 файлов тестов, префикс QA-ветки `qa/`, каталог отчёта берётся из `AllowedReportDirectory`, viewport браузерного CLI (`browser/run.mjs`) заданы в коде.
- На старте валидируются обязательные переменные активного окружения, положительные лимиты, разрешённые относительные пути и loopback-адрес API.
- Для Production отключаются подробные ошибки, используется отдельная тестовая БД, а runner и сервис получают минимальные права.
- Ключи/токены не пишутся в логи и не попадают в snapshot конфигурации результата.
