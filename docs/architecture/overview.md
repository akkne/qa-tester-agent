# Архитектура

## Размещение и зависимости

```text
GitHub push → Actions (self-hosted macOS) → HTTP API → PostgreSQL queue
                                                 ↓
                                      BackgroundService (один worker)
                                                 ↓
              Git → Context/Analysis → TestGeneration → Execution → Reporting
                    ↘ LLM adapter       ↘ Docker runner  ↘ GitHub publisher
```

В одном ASP.NET Core процессе находятся API, страница результата и worker. PostgreSQL хранит задания, этапы, выводы и ссылки на артефакты. Рабочие копии Git и браузерные артефакты находятся вне исходного checkout на локальном диске. Контейнеры проверяемого приложения и тестов запускаются сервисом; Docker socket доступен только оркестратору на хосте.

## Слои и каталоги реализации

Полный состав solution и ссылки между проектами перечислены в [реестре C# проектов](projects.md).

| Каталог | Назначение | Разрешённые зависимости |
| --- | --- | --- |
| `src/QaAgent.Domain/` | Сущности, статусы, правила переходов, value objects | BCL |
| `src/QaAgent.Application/` | Сценарий запуска, порты, политика проверок, лимиты | Domain |
| `src/QaAgent.Infrastructure/` | Git CLI/API, EF/Npgsql, LLM, Docker, Playwright, файловые артефакты | Application, Domain |
| `src/QaAgent.Host/` | HTTP API, UI, worker и **composition root** | Все слои |
| `tests/` | Unit, интеграционные и контрактные проверки агента | Соответствующие проекты |
| `demo/` | Проверяемое приложение, ТЗ и стартовые тесты | Не зависит от агента |
| `.github/workflows/` | Передача push в локальный API | Только конфигурация CI |

Ни один модуль Application не вызывает Git CLI, PostgreSQL, Docker, HTTP или LLM напрямую. Сначала задаётся порт и модель данных, затем адаптер; выбор адаптера и его конфигурация происходят только в `QaAgent.Host`.

## Порты Application

| Порт | Обязанность |
| --- | --- |
| `IRunRepository` + `IUnitOfWork` | Получение/изменение запусков; атомарная запись этапа, результата и события очереди |
| `IClock` | Время и дедлайны, управляемые в тестах |
| `IGitWorkspace` | Изолированный checkout строго на `headSha`, diff `baseSha..headSha`, список файлов и история |
| `IProjectProfileProvider` | Разрешённые команды, пути, ограничения, test accounts и healthcheck |
| `ISpecificationReader` | Чтение ТЗ с ID требований из `docs/specs/` |
| `ILlmClient` | Структурированные ответы по заданной JSON-схеме и учёт токенов/стоимости |
| `ITestGenerator` | Запись тестов только в разрешённые пути; до двух исправлений ошибок генерации |
| `ICheckExecutor` | Сборка, полный набор тестов, Coverlet, стенд, Playwright, лимиты времени |
| `ISecretScanner` | Отдельное сканирование секретов в изменении и выходных артефактах |
| `IArtifactStore` | Локальные screenshots/traces и стабильные ссылки |
| `IReportPublisher` | Markdown, commit и push QA-ветки от проверяемого SHA |

Интерфейсы описывают необходимые возможности, а не детали библиотек. DTO между этапами содержат SHA, список требований и доказательства с типом происхождения. Каждый адаптер валидирует вход и возвращает явную ошибку этапа. Прерванный этап не превращается в успешный запуск.

## Перенос на Linux

Пути, команды и адреса задаются профилем; файловые разделители не зашиваются в бизнес-логику. Контейнеры и PostgreSQL остаются теми же. Меняются установка runner, Docker host и путь к хранилищу артефактов. Для нескольких worker потребуется стратегия конкурентного захвата очереди; текущий MVP рассчитан на один.
