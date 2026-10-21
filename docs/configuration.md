# Конфигурация окружений

## Источники и приоритет

Хост читает `src/QaAgent.Host/appsettings.json`, затем `appsettings.{Environment}.json`, затем переменные окружения .NET с разделителем `__`. `ASPNETCORE_ENVIRONMENT` выбирает `Development`, `Demo` или `Production`. Файлы `.env.<environment>` предназначены для локального запуска или Docker Compose: launcher экспортирует их как переменные процесса **до** старта .NET; стандартный ASP.NET Core не загружает `.env` автоматически. На сервере те же переменные задаются менеджером секретов/окружением процесса.

В Git хранятся `.env.<environment>.example` без значений секретов. Локальные `.env.development`, `.env.demo`, `.env.production` игнорируются и создаются из соответствующего примера. Не нужно коммитить заполненные файлы.

## Разделение настроек

| В `appsettings` | В `.env` / переменных окружения |
| --- | --- |
| Лимиты времени, LLM-вызовов и исправлений; пути артефактов; число worker; viewport; правила отчёта; команды и healthcheck профиля | `ConnectionStrings__Postgres`, ключ локального API, GitHub repository/branch/token, LLM provider/base URL/model/key, bot/Telegram account/token/admin chat IDs, Bitrix URL/key, тестовые логины/пароли |

Внешние интеграции Telegram, bot и Bitrix оставлены как необязательные параметры для будущих этапов: они не участвуют в MVP и не должны мешать запуску при пустых значениях. При включении интеграции её конфигурация валидируется на старте. Неиспользуемые переменные не передаются в контейнеры тестов и приложения.

`appsettings.Demo.json` задаёт профиль `demo`: разрешённые каталоги тестов и ТЗ, команды сборки/тестов/стенда, healthcheck, desktop и mobile viewport. В М0 `demo/` содержит только запускаемый шаблон. Команды профиля для Compose станут работоспособными после Docker-окружения М1. Для другого репозитория добавляется отдельный профиль с явной проверкой команд и путей, а не автоматическое определение LLM.

Для локальной М1 поднимите PostgreSQL через `QA_POSTGRES_PASSWORD=<локальный пароль> docker compose up -d postgres`. Задайте `ConnectionStrings__Postgres` для `127.0.0.1:5433`, `AgentApi__Key`, `GitHub__Repository=owner/repo`, `GitHub__Branch=main` и при локальной проверке `GitHub__SourcePath` с путём к checkout. Без `GitHub__SourcePath` Git CLI клонирует `https://github.com/owner/repo.git`; приватный доступ на runner/хосте нужно заранее настроить через Git credential helper. `ASPNETCORE_URLS=http://127.0.0.1:5080` удерживает API на loopback. После этого запускайте `dotnet run --project src/QaAgent.Host`.

Workflow использует GitHub secret `QA_AGENT_API_KEY` и, для первого push с нулевым `before`, repository variable `QA_INITIAL_BASE_SHA`. Значение первого base SHA должно указывать на существующий commit. Сквозная локальная проверка `bash scripts/check_m1.sh` сама создаёт временные PostgreSQL и Git-репозиторий и не использует настоящие ключи.

## Значения и проверка

- Профиль фиксирует версию. Изменение команд/путей увеличивает `ProfileVersion` и участвует в ключе идемпотентности.
- На старте валидируются обязательные переменные активного окружения, положительные лимиты, разрешённые относительные пути и loopback-адрес API.
- Для Production отключаются подробные ошибки, используется отдельная тестовая БД, а runner и сервис получают минимальные права.
- Ключи/токены не пишутся в логи и не попадают в snapshot конфигурации результата.
