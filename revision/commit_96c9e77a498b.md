# QA result for `96c9e77a498b`

- **Status:** Passed
- **Repository:** akkne/qa-tester-agent
- **Range:** `881952f2040ee116105d2b90ad8cbcd089b088d6..96c9e77a498b3f44a0c338f379f15a486885dccf`
- **Run ID:** `4e00578a-8ad1-4452-a13d-7f9f42df045c`
- **Result page:** http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c
- **Duration:** 195 seconds (at report creation)
- **LLM tokens:** 84211 input, 4551 output; 0 generation fixes

## Requirements and changed files

- `DEMO-REG-01`
- `DEMO-REG-02`
- `DEMO-REG-03`
- `DEMO-REG-04`
- `DEMO-REG-05`
- `DEMO-REG-06`
- `DEMO-REG-07`
- `M src/QaAgent.Infrastructure/Analysis/CoberturaCoverageCalculator.cs`
- `M tests/QaAgent.IntegrationTests/CoberturaCoverageCalculatorTests.cs`

## Verification plan

- `DEMO-REG-01` positive: Создаётся одна учётная запись с email alice@example.com. — Через API и форму отдельно зарегистрировать новый email '  Alice@Example.COM  ' с допустимым паролем; проверить результат и сохранённый email.
- `DEMO-REG-01` negative: Пустой email, email из пробелов и явно неверный email отклоняются; записи не создаются. — Через API и форму отправить каждый вариант email с допустимым паролем; проверить ошибку и неизменность числа записей.
- `DEMO-REG-01` boundary: Пароль длиной 11 символов отклоняется, длиной 12 непробельных символов принимается. — Для разных новых email отправить пароли длиной 11 и 12 символов через API и форму; проверить результат и наличие записи.
- `DEMO-REG-02` negative: Повторная запись не создаётся; пользователь получает понятное сообщение о существующем email. — Создать alice@example.com; повторить регистрацию с тем же email, другим регистром и пробелами по краям через API и форму; проверить сообщение, количество записей и сохранность исходной записи.
- `DEMO-REG-03` positive: Пароль хранится только как стойкий хеш и успешно проверяется штатным механизмом проверки пароля. — Зарегистрировать пользователя с известным синтетическим паролем; проверить сохраняемые поля и реализацию хеширования, включая алгоритм, соль и параметры; проверить правильный и неверный пароль штатным верификатором.
- `DEMO-REG-03` negative: Ответы API и страницы не содержат пароль или сохранённый хеш. — Проверить тела ответов регистрации, входа, получения своего аккаунта и кабинета, включая ошибки регистрации и входа; искать известный пароль и фактический сохранённый хеш в JSON и HTML.
- `DEMO-REG-04` positive: Пользователь входит и видит данные своего кабинета. — Зарегистрировать пользователя; в новой сессии войти через API и отдельно через форму с правильным паролем; проверить /api/me и /cabinet. Повторить вход с регистром и пробелами по краям email.
- `DEMO-REG-04` negative: Неверные данные не создают авторизованную сессию и не дают доступ к кабинету. — В отдельных анонимных сессиях попробовать существующий email с неверным паролем и неизвестный email; после каждого отказа запросить /api/me и /cabinet.
- `DEMO-REG-05` access-control: Анонимный пользователь не получает кабинет или данные существующего аккаунта. — Создать аккаунт; из новой анонимной сессии запросить /cabinet, /cabinet/{id}, /api/me и /api/accounts/{id}; проверить отказ в доступе и отсутствие данных аккаунта в ответах.
- `DEMO-REG-05` access-control: Каждый пользователь получает свои данные; подмена ID не раскрывает данные другого пользователя. — Создать A и B и войти в независимых сессиях; проверить собственные кабинеты и API; в сессии A заменить ID на ID B в /cabinet/{id} и /api/accounts/{id}, затем повторить в обратном направлении; проверить отказ и отсутствие чужих данных.
- `DEMO-REG-06` positive: Формы имеют связанные с полями подписи; основные элементы доступны по устойчивым data-testid в обоих вариантах Chromium. — В desktop и mobile Chromium пройти регистрацию, вход и открытие кабинета; проверить подписи email и пароля, доступность основных элементов и селекторы register-form, register-submit, login-form, login-submit, account-email; повторить после перезагрузки.
- `DEMO-REG-06` negative: Ошибки видимы, понятны и доступны через устойчивый селектор form-error на desktop и mobile. — В обоих вариантах Chromium отправить неверный email, недопустимые пароли, повторный email и неверные данные входа; проверить текст ошибки и возможность исправить данные и повторить отправку.
- `DEMO-REG-07` boundary: Пароли длиной 12 и 128 символов с хотя бы одним непробельным символом принимаются и позволяют войти. — Через API и форму отдельно зарегистрировать новые аккаунты с паролями длиной 12 и 128: полностью непробельными и содержащими один непробельный символ среди пробелов; проверить создание ровно одной записи и вход с исходным паролем.
- `DEMO-REG-07` boundary: Пароли длиной 11 и 129 символов отклоняются с понятной ошибкой; запись не создаётся. — Через API и форму отправить непробельные пароли длиной 11 и 129 для новых email; проверить ошибку с соответствующим ограничением и отсутствие записи.
- `DEMO-REG-07` negative: Пароли только из пробельных символов отклоняются с понятной ошибкой; записи не создаются. — Через API и форму отправить 12 и 128 пробелов; через API также отправить 12 символов из табуляций и переводов строк; проверить объяснение проблемы и неизменность хранилища.
- `DEMO-REG-07` access-control: Отклонённая регистрация не позволяет войти или получить защищённые данные. — После регистрации с паролем длиной 11, 129 либо только из 12 или 128 пробелов попробовать войти с теми же данными; запросить собственный кабинет и данные заранее созданного другого пользователя; проверить отсутствие авторизованной сессии и раскрытия данных.

## Findings

No LLM findings.

## Candidate tests and execution

- `demo/Demo.Tests/NormalizedFormLoginPositiveTests.cs` — positive; DEMO-REG-01, DEMO-REG-04
- `demo/Demo.Tests/DuplicateRegistrationIntegrityNegativeTests.cs` — negative; DEMO-REG-02, DEMO-REG-03, DEMO-REG-04
- `demo/Demo.Tests/SymmetricAccountIsolationAccessControlTests.cs` — access-control; DEMO-REG-04, DEMO-REG-05
- **Tests:** 79 total; 0 failed
- **restore:** passed (exit 0);   Determining projects to restore...   Restored /src/src/QaAgent.Domain/QaAgent.Domain.csproj (in 73 ms).   Restored /src/src/QaAgent.Application/QaAgent.Application.csproj (in 73 ms).   Restored /src/demo/Demo.Web/Demo.Web.csproj (in 3.01 sec).   Restored /src/tools/M3Probe/M3Probe.csproj (in 3.1 sec).   Restored /src/tests/QaAgent.UnitTests/QaAgent.UnitTests.csproj (in 4.83 sec).   Restored /src/tests/QaAgent.IntegrationTests/QaAgent.IntegrationTests.csproj (in 4.83 sec).   Restored /src/demo/Demo.Tests/Demo.Tests.csproj (in 4.83 sec).   Restored /src/src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj (in 5.23 sec).   Restored /src/src/QaAgent.Host/QaAgent.Host.csproj (in 5.32 sec). 
- **format-whitespace:** passed (exit 0); 
- **format-style:** passed (exit 0); 
- **build:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   M3Probe -&gt; /src/tools/M3Probe/bin/Debug/net10.0/M3Probe.dll   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll  Build succeeded.     0 Warning(s)     0 Error(s)  Time Elapsed 00:00:05.02 
- **test:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll Test run for /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll (.NETCoreApp,Version=v10.0)   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll Test run for /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern. A total of 1 test files matched the specified pattern.   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll Test run for /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern. Results File: /artifacts/test-results/_4074324dfb95_2026-10-22_07_49_29_net10.0.trx  Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 35 ms - QaAgent.UnitTests.dll (net10.0) Results File: /artifacts/test-results/_4074324dfb95_2026-10-22_07_49_29_net10.0\[1\].trx  Passed!  - Failed:     0, Passed:    37, Skipped:     0, Total:    37, Duration: 1 s - Demo.Tests.dll (net10.0) Results File: /artifacts/test-results/_4074324dfb95_2026-10-22_07_49_32_net10.0.trx  Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 3 s - QaAgent.IntegrationTests.dll (net10.0)  Attachments:   /artifacts/test-results/56dfe6b2-897c-42e7-a0e9-60d525be3163/coverage.cobertura.xml   /artifacts/test-results/9365269f-0044-4b04-b58a-a43b131f0b74/coverage.cobertura.xml   /artifacts/test-results/5c276ae9-024c-4c4a-86dc-f8a614710384/coverage.cobertura.xml 
- **Stand:** available; 
- **Browser:** True; {"status":"PASSED","scenarios":\[{"name":"desktop","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]},{"name":"mobile","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]}\],"artifacts":{"directory":"/tmp/qa-agent-artifacts/4e00578a8ad14452a13d7f9f42df045c/browser"}}
- **Whole project line coverage:** 47,35% (1303/2752)
- **Changed executable line coverage:** 100% (1/1)

## Local evidence

- [browser-install.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/f474d87795a0e69522708183) (77 bytes)
- [browser.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/f89a7bda22cde16a3167fa9b) (837 bytes)
- [build.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/906be301a25b186e911bc2e6) (834 bytes)
- [checks-container.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/6bb301c1540ee4a6155a1b32) (1 bytes)
- [chromium-install.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/8da813fec6aafb2a592ad9a5) (1 bytes)
- [format-style.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/803825e78272808b9d672002) (0 bytes)
- [format-whitespace.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/44dc5ca935d13876287540bd) (0 bytes)
- [restore.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/bed0d23857bfe905296ea8d3) (700 bytes)
- [source-sha.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/4f4d908e9e9a08246a97f342) (42 bytes)
- [stand-start.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/5d18a495ed0dc083dd1283ff) (2147 bytes)
- [stand-stop.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/ccf017a5d2949367ce416808) (446 bytes)
- [test-results/56dfe6b2-897c-42e7-a0e9-60d525be3163/coverage.cobertura.xml](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/d82cf3a5dda52536571f21a0) (1588616 bytes)
- [test-results/5c276ae9-024c-4c4a-86dc-f8a614710384/coverage.cobertura.xml](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/b71fe8bf4a57ac7f2258202b) (45202 bytes)
- [test-results/9365269f-0044-4b04-b58a-a43b131f0b74/coverage.cobertura.xml](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/6508919e8962a0ef52f788a9) (127161 bytes)
- [test-results/_4074324dfb95_2026-10-22_07_49_29/In/4074324dfb95/coverage.cobertura.xml](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/628a6fe59cea42d3847fa330) (127161 bytes)
- [test-results/_4074324dfb95_2026-10-22_07_49_29_net10.0.trx](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/9752158cb122e697bdce6236) (28707 bytes)
- [test-results/_4074324dfb95_2026-10-22_07_49_29_net10.0\[1\].trx](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/fb96f3728d740364a6629424) (217096 bytes)
- [test-results/_4074324dfb95_2026-10-22_07_49_32/In/4074324dfb95/coverage.cobertura.xml](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/0aed7e75f71f932055bcbec8) (1588616 bytes)
- [test-results/_4074324dfb95_2026-10-22_07_49_32_net10.0.trx](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/f004918408eec779b5aae1ff) (30627 bytes)
- [test.log](http://127.0.0.1:5080/runs/4e00578a-8ad1-4452-a13d-7f9f42df045c/artifacts/a39ae344e68a9095d2cea01f) (2079 bytes)

## Context gaps

- Diff меняет расчёт Cobertura, а требования DEMO-REG-01–DEMO-REG-07 описывают регистрацию. Требований к расчёту покрытия нет; привязать проверку этого изменения к указанным ID нельзя.
- Реализация Demo.Web и постоянного хранилища не предоставлена; существующие тесты используют MemoryAccountStore и сами по себе не подтверждают фактическое хранение паролей.
- Критерии стойкости хеша и единица подсчёта длины пароля для Unicode не определены; для граничных проверок используются ASCII-символы.
- Точный Git SHA и настройки запуска desktop и mobile Chromium не указаны; перед выполнением плана нужно зафиксировать ревизию и конфигурации.
