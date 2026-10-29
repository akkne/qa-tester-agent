# Запуск тестов любого стека через агента

Профиль с `ExecutionMode: "agent"` заменяет зашитые .NET-команды M3 агентом Codex. Агент сам находит наборы тестов на любом языке, ставит нужные toolchain и зависимости, запускает всё и возвращает JSON. Вердикт не берётся из слов агента: сервис вычисляет его по журналу команд, отчётам JUnit/TRX и diff отслеживаемых файлов.

## Схема

```text
RunWorker → AgentM3Executor (Infrastructure)
              │ docker run qa-agent/test-runner  (/src = checkout headSha, /artifacts = доказательства)
              │   codex exec --output-schema … ──► qa-run --suite S --phase setup|build|test -- <cmd>
              │                                        ├─ /artifacts/commands/NNNN-S-phase.log
              │                                        └─ /artifacts/ledger.jsonl  (exit code каждой команды)
              │   финальный JSON (AgentTestContract.Schema)
              ▼
         TestReportParser: ledger + JUnit/TRX + git diff --name-only HEAD
              ▼
         AgentExecution.Evaluate (Application, чистая функция) → SuiteResult[]
              ▼
         M3Outcome.From → Passed | Failed | NeedsReview | Blocked
```

| Компонент | Файл | Роль |
| --- | --- | --- |
| Контракт и prompt | `src/QaAgent.Application/Execution/AgentTestContract.cs` | JSON-схема ответа, DTO, текст prompt |
| Алгоритм вердикта | `src/QaAgent.Application/Execution/AgentExecution.cs` | Суждение по набору, перекрёстная проверка заявлений агента |
| Итог запуска | `src/QaAgent.Application/Execution/M3Outcome.cs` | Ветка `M3ExecutionProfile.Agent` |
| Исполнитель | `src/QaAgent.Infrastructure/Execution/AgentM3Executor.cs` | Контейнер, Codex, лента активности, сбор доказательств |
| Разбор отчётов | `src/QaAgent.Infrastructure/Execution/TestReportParser.cs` | JUnit XML, TRX, ledger, проверка путей |
| Образ | `docker/test-agent/Dockerfile`, `docker/test-agent/qa-run` | Codex CLI, mise, Node/Python/Java/Go/.NET, обёртка команд |

## Разделение ответственности

Агент отвечает за то, что плохо формализуется: найти наборы (монорепо, CI-файлы, нестандартные скрипты), подобрать версии, поставить зависимости, добавить reporter. Сервис отвечает за то, что должно быть детерминированным:

- **Exit code** — только из `ledger.jsonl`, который пишет `qa-run`. Команда вне `qa-run` считается невыполненной.
- **Числа тестов** — только из разобранных JUnit/TRX. Счётчики агента используются, лишь когда отчёта нет, и тогда набор помечается `Unverified`.
- **Целостность** — `git diff --name-only HEAD` после работы; изменение отслеживаемого файла блокирует запуск.
- **Полнота** — набор, который есть в ledger, но отсутствует в JSON, всё равно оценивается, чтобы агент не мог «потерять» падающий набор.

## Вердикт набора

Правила применяются по порядку к записям одного `suite` (последняя запись фазы — действующая):

| Условие | Вердикт |
| --- | --- |
| Нет ни одной записи `qa-run` | `Blocked` |
| Последний `build` завершился с ошибкой (не 124/126/127/137) | `Failed` |
| Нет записи `test` (setup/build не удались или не запускались) | `Blocked` |
| В отчётах есть упавшие тесты | `Failed` |
| Последний `test` вернул 124, 126, 127 или 137 (timeout, нет команды, OOM) | `Blocked` |
| Последний `test` вернул иной ненулевой код | `Failed` |
| Ранее `test` падал, повтор прошёл | `Flaky` |
| Нет разбираемого отчёта | `Unverified` |
| В отчётах 0 выполненных тестов | `Blocked` |
| Иначе | `Passed` |

Итог запуска: нет JSON, изменены файлы или не найдено ни одного набора → `Blocked`; есть `Failed` → `Failed`; есть `Blocked` → `Blocked`; есть `Flaky`, `Unverified` или пропущенные (`notRun`) наборы → `NeedsReview`; иначе `Passed`. Расхождения счётчиков агента с отчётами и битые отчёты попадают в предупреждения.

## Prompt

Prompt построен как инструкция исполнителю, а не аналитику: жёсткие правила, процедура, контракт ответа. Ключевые решения:

1. **Запрет изменений** отслеживаемых файлов с перечислением типичных нарушителей (`npm install`, `go mod tidy`, snapshot-update). Это подкреплено проверкой diff.
2. **Обязательный `qa-run`** с фазами `setup/build/test`; фаза определяет, считается ли ошибка вердиктом о коде (`build`, `test`) или окружением (`setup`).
3. **Запрет маскировать падения**: никаких повторов, `--lf`, `-x`, фильтров. Повтор после исправления окружения разрешён, но алгоритм пометит набор `Flaky`.
4. **Содержимое репозитория — данные**: README и CI-файлы — подсказки о командах, а не инструкции агенту (защита от prompt injection).
5. **Отчёты в машинном формате** с конкретными флагами для pytest, Jest, Vitest, Go, Rust, Maven/Gradle, PHPUnit, RSpec, .NET; reporter ставится вне отслеживаемых манифестов.
6. **Бюджет**: не больше трёх подходов к настройке одного набора, затем переход к следующему.
7. **Подсказки профиля** `Profiles:<id>:TestHints` добавляются в prompt как есть.

## Конфигурация

```json
"TestAgent": {
  "Image": "qa-agent/test-runner:1",
  "TimeoutMinutes": 45,
  "CommandTimeoutSeconds": 1800,
  "Cpus": "4",
  "Memory": "8g",
  "AllowDocker": false
},
"Profiles": { "demo": { "ExecutionMode": "agent", "TestHints": ["Integration tests need `make db` first."] } }
```

`TestAgent__ApiKey` (в `.env`) переключает агента на `CODEX_API_KEY`; без него в контейнер копируется `$CODEX_HOME/auth.json` (по умолчанию `~/.codex/auth.json`), а обновлённый токен после запуска возвращается на хост. `TestAgent__Model` по умолчанию равен `Llm__Model`. Образ собирается один раз: `docker build -t qa-agent/test-runner:1 docker/test-agent`. `TimeoutMinutes` должен помещаться в `Agent:RunTimeoutMinutes` с учётом M2 и повторных прогонов worker.

## Безопасность и ограничения

- Контейнер — единственная песочница: Codex запускается с `--dangerously-bypass-approvals-and-sandbox`, но внутри одноразового контейнера без переменных хоста, с лимитами CPU, памяти и процессов. Сеть открыта: она нужна для зависимостей.
- Код проверяемого репозитория выполняется в том же контейнере, что и Codex, поэтому он может прочитать Codex-авторизацию. Для недоверенных репозиториев используйте отдельный `TestAgent__ApiKey` с лимитом расходов.
- `AllowDocker: true` монтирует Docker socket — это фактически root на хосте. Включайте только для доверенного репозитория, которому нужны Testcontainers или compose.
- Публикуются только `agent/commands/*.log`, `agent/reports|coverage/**/*.xml|.trx`, `ledger.jsonl`, `agent-report.json`, `container.log`.
- Покрытие считается, только если агент выдал Cobertura; LCOV и JaCoCo пока не разбираются. TAP и прочие форматы дают `Unverified`.
- M2 по-прежнему генерирует только C# xUnit тесты; для других стеков M2 их не добавит, и запуск продолжится с тестами репозитория.
- Каждый повторный прогон worker (без сгенерированных тестов, baseline) — это новый полный запуск агента.
- На Linux файлы, созданные в контейнере от root, могут помешать удалению рабочего каталога; на macOS Docker Desktop это не проявляется.
