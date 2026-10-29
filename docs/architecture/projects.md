# Объявленные C# проекты

Все проекты включены в `QaAgent.slnx` с начала М0. Проекты `demo/` не ссылаются на код агента: агент проверяет их как внешний репозиторий. В будущих фазах новые классы размещаются в уже объявленных проектах; отдельный проект добавляется только при новой самостоятельной границе ответственности.

| Проект | Тип | Роль | Ссылки на проекты |
| --- | --- | --- | --- |
| `src/QaAgent.Domain/QaAgent.Domain.csproj` | class library | Состояния запуска, инварианты и типы результата | Нет |
| `src/QaAgent.Application/QaAgent.Application.csproj` | class library | Сценарии и порты Git, LLM, хранилища, проверки, публикации, времени | Domain |
| `src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj` | class library | Адаптеры EF/PostgreSQL, Git, LLM, Docker, Playwright, файлов и часов | Application, Domain |
| `src/QaAgent.Host/QaAgent.Host.csproj` | ASP.NET Core | API, страница результата, worker и единственный composition root | Application, Infrastructure, Domain |
| `tests/QaAgent.UnitTests/QaAgent.UnitTests.csproj` | xUnit | Доменные правила и сценарии с рукописными фейками | Application, Domain |
| `tests/QaAgent.IntegrationTests/QaAgent.IntegrationTests.csproj` | xUnit | Контракты и интеграции Git, PostgreSQL, HTTP и исполнения | Host, Infrastructure |
| `tools/M3Probe/M3Probe.csproj` | console | Ручной запуск M3-исполнителя (`M3Probe SOURCE BASE_SHA HEAD_SHA ARTIFACT_ROOT [файл теста|--lead-qualification]`) против локального checkout для диагностики и приёмки | Infrastructure (и транзитивно Application, Domain) |
| `demo/Demo.Web/Demo.Web.csproj` | ASP.NET Core | Проверяемое приложение регистрации и кабинета | Нет |
| `demo/Demo.Tests/Demo.Tests.csproj` | xUnit | Стартовые, новые и браузерные проверки демопроекта | Demo.Web |

Направление зависимостей: `Host → Infrastructure → Application → Domain`. Host также ссылается на Application и Domain для сборки HTTP-контрактов; Infrastructure реализует порты Application. Сервисы регистрируются только в `Host` через отдельный метод композиции. Тестовые проекты не входят в production-зависимости.

В М1 Infrastructure содержит PostgreSQL и Git-адаптеры, а также системные часы. Остальные адаптеры вводятся по фазам. Demo.Web содержит запускаемый шаблон и healthcheck; реализация требований регистрации и новые тесты входят в М2. Playwright-пакет и браузерные тесты добавляются в М3.
