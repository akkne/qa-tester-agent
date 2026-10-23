# Локальная приёмка М3

`DockerM3Executor` проверен через `tools/M3Probe` на отдельных checkout. Для повторной проверки исправного коммита можно передать локальный путь репозитория, `baseSha`, `headSha` и каталог артефактов в `dotnet run --project tools/M3Probe -- SOURCE BASE HEAD ARTIFACT_ROOT`. Команда выполняет те же проверки, что worker после М2.

| Сценарий | SHA | Результат |
| --- | --- | --- |
| Исправный код | `00720cd9492b0b4b8e43dfa04061c47c9cce3dc2` | 44/44 теста, сборка и форматирование прошли; Coverlet 989/2197 (45,02%) исполняемых строк и 1/1 изменённая исполняемая строка; Chromium desktop/mobile прошёл |
| Контролируемый дефект: удалена проверка владельца `/cabinet/{id}` | `8f44e6cd9adfbb6794ad6e617147a1922e383d6c` в отдельном локальном репозитории | 2 теста упали; desktop/mobile получили HTTP 200 вместо ожидаемого 403; оба сохранили screenshot, trace, шаги и диагностику. Итог `FAILED` |

Артефакты этой приёмки лежат в игнорируемом Git каталоге `artifacts/m3-acceptance/`: полные логи исправного и сломанного прогонов, а для дефекта — `fault/browser/{desktop,mobile}.png`, `*-trace.zip` и `*-diagnostics.json`. Контролируемый дефект воспроизводится командой `bash scripts/check_m3_fault.sh`; она создаёт временный коммит и проверяет падение тестов и браузера.

Для браузерного профиля отсутствующий стенд не принимается за успех: `M3Outcome` возвращает `BLOCKED`, а CLI браузера возвращает `BLOCKED` при недоступном `/healthz`. Их состояния покрыты автоматическими тестами. Сквозная приёмка этого профиля с настоящим LLM описана в [М6](m6-evidence.md).

## Профиль LeadQualification

22 октября 2026 года диапазон `f436bd766ad6d04fde360dde58aa980d189fee9e..2d3ed99b565f542e13ec1d9ac8dba7cf7067cd8c` повторён с `ProfileVersion=11`. Прямой `M3Probe --lead-qualification` вернул `Passed`: restore, build, 658 тестов и `host-smoke` завершились с кодом 0. Smoke запустил хост в CI-режиме и получил HTTP 200 от `/health`; его логи сохранены в `/tmp/qa-agent-lead-m3-verify2/625e2c4799b1495d887d749e750e7dbd/`.

Сквозной [запуск `3702ae4a`](http://127.0.0.1:5080/runs/3702ae4a-fcc1-4bbf-b9f2-b627fe9aae49) с настоящим Codex завершился `Passed`. M2 сгенерировал три тестовых файла; M3 выполнил 665 тестов без падений и записал `host-smoke.log` с HTTP 200, а также `host-process.log`. [QA-отчёт](https://github.com/akkne/lead-qualification-bot/blob/qa%2F2d3ed99b565f-3702ae4afcc1/revision/commit_2d3ed99b565f.md) опубликован в commit `6d4a3ef852fff1dfa50ae30deb838f6871d65493` с родителем `2d3ed99b565f542e13ec1d9ac8dba7cf7067cd8c`; в нём только три теста и отчёт. Полные локальные артефакты находятся в `/tmp/qa-agent-artifacts/3702ae4afcc14bbfb9f2b627fe9aae49/`.
