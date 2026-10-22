# QA result for `5de282f49e18`

- **Status:** Passed
- **Repository:** akkne/qa-tester-agent
- **Range:** `2d8a8d69fe2aee683d42171bd181834c1d7c6104..5de282f49e185f5d99d97a3d547b5b5a931d03df`
- **Run ID:** `eda299fc-c6a3-4047-8f2d-445b05cb4cf8`
- **Result page:** http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8
- **Duration:** 190 seconds (at report creation)
- **LLM tokens:** 103014 input, 4449 output; 0 generation fixes

## Requirements and changed files

- `DEMO-REG-01`
- `DEMO-REG-02`
- `DEMO-REG-03`
- `DEMO-REG-04`
- `DEMO-REG-05`
- `DEMO-REG-06`
- `DEMO-REG-07`
- `M src/QaAgent.Infrastructure/Analysis/DiffSecretScanner.cs`
- `M src/QaAgent.Infrastructure/Analysis/M2ContextReader.cs`
- `M tests/QaAgent.IntegrationTests/M2ContextAndGuardTests.cs`

## Verification plan

- `DEMO-REG-01` positive: Создана одна учётная запись с email alice@example.com. — Через API и форму зарегистрировать нового пользователя с email '  Alice@Example.COM  ' и допустимым паролем; проверить результат и сохранённый email.
- `DEMO-REG-01` negative: Регистрация отклонена с понятной ошибкой; учётная запись не создана. — Через API и форму отдельно отправить пустой email, email из пробелов, неверный email и пароль длиной 11 символов; проверять количество записей после каждой попытки.
- `DEMO-REG-02` negative: Вторая запись не создаётся; сообщение понятно объясняет, что email уже зарегистрирован. — Создать пользователя, затем через API и форму повторить регистрацию с тем же email, другим регистром и пробелами по краям; проверить количество записей.
- `DEMO-REG-03` positive: В хранилище находится только стойкий хеш; исходный пароль и хеш отсутствуют в ответах API и страницах. — Зарегистрировать пользователя с известным синтетическим паролем; проверить сохранённую запись и реализацию хеширования, включая алгоритм и параметры. Проверить ответы регистрации, входа, получения профиля и страницы кабинета на отсутствие пароля и хеша.
- `DEMO-REG-03` negative: Ошибочные ответы и страницы не раскрывают пароль или сохранённый хеш. — Вызвать ошибки регистрации, повторного email и входа через API и формы; проверить тела ответов и HTML на отсутствие отправленного пароля и хеша существующей записи.
- `DEMO-REG-04` positive: Вход успешен; кабинет содержит данные вошедшего пользователя. — Зарегистрировать пользователя; в новой сессии войти с правильными email и паролем через API и форму; открыть кабинет и проверить принадлежность данных.
- `DEMO-REG-04` negative: Вход отклонён; доступ к кабинету и данным профиля не предоставлен. — В независимых анонимных сессиях попробовать существующий email с неверным паролем и незарегистрированный email; после каждой попытки запросить кабинет и /api/me.
- `DEMO-REG-05` access-control: Анонимный пользователь не получает кабинет или данные зарегистрированного пользователя. — Создать пользователя; без авторизации запросить /cabinet, /cabinet/{id}, /api/me и /api/accounts/{id}; проверить отказ в доступе и отсутствие данных пользователя в ответах.
- `DEMO-REG-05` access-control: Каждый пользователь видит собственные данные; подмена URL или ID не раскрывает чужие данные. — Создать A и B и войти в отдельных сессиях. Проверить собственные кабинеты; затем из сессии A запросить /cabinet/{B.id} и /api/accounts/{B.id}, повторить в обратном направлении; проверить отсутствие чужих данных.
- `DEMO-REG-06` positive: Поля имеют связанные подписи; основные элементы доступны по устойчивым data-testid; регистрация и вход работают в обоих вариантах Chromium. — В desktop и mobile Chromium пройти регистрацию и вход, используя data-testid. Проверить подписи email и пароля, элементы отправки и отображение email в кабинете; повторить загрузку страниц и проверить стабильность селекторов.
- `DEMO-REG-06` negative: Ошибки видимы, понятны и указывают на проблему; элемент ошибки доступен по устойчивому data-testid. — В desktop и mobile Chromium отправить формы с неверным email, недопустимым паролем, повторным email и неверными данными входа; проверить сообщения и селектор form-error.
- `DEMO-REG-07` boundary: Пароли длиной 12 и 128 символов принимаются, включая варианты с единственным непробельным символом; созданный пользователь может войти с исходным паролем. — Через API и форму зарегистрировать отдельные email с паролями длиной 12 и 128 символов: полностью непробельными и содержащими один непробельный символ в начале или конце. Проверить создание записи и вход с точным исходным паролем.
- `DEMO-REG-07` boundary: Пароли длиной 0, 11 и 129 символов отклонены с понятной ошибкой; записи не созданы. — Через API и форму отправить пустой пароль и непробельные пароли длиной 11 и 129 символов; проверить ошибку, неизменность количества записей и отсутствие email в хранилище.
- `DEMO-REG-07` negative: Пароль из одних пробельных символов отклонён с понятным объяснением; учётная запись не создана. — Через API и форму проверить пароли из 12 и 128 пробелов; через API дополнительно проверить смесь табуляции и переводов строк общей длиной 12. Проверить ошибку и отсутствие новых записей.
- `DEMO-REG-07` access-control: Отклонённая регистрация не создаёт пользователя и не предоставляет доступ к существующему аккаунту. — Создать владельца аккаунта. В новой сессии попытаться зарегистрировать другой email с недопустимыми паролями, затем войти с отклонёнными данными и запросить кабинет, профиль и аккаунт владельца по ID; проверить отказ и отсутствие данных владельца.

## Findings

- **hypothesis / high** `src/QaAgent.Infrastructure/Analysis/M2ContextReader.cs:146` (DEMO-REG-03): expected Password assignments are redacted before source, specifications, tests, or diffs enter model context, including short invalid passwords.; observed The replacement regex requires at least eight characters in either value alternative. Previously, SecretAssignmentRegex matched shorter values. Consequently, an assignment such as password="short" remains in context.Text. Registration tests explicitly include short invalid passwords; rejecting a password during registration does not make its disclosure appropriate.; reproduce: Pass a snapshot whose diff contains +password="short" to M2ContextReader.ReadAsync with an existing specification directory. Inspect context.Text for the literal password. This is a code-based hypothesis; ReadAsync was not executed.
- **hypothesis / medium** `src/QaAgent.Infrastructure/Analysis/DiffSecretScanner.cs:61` (DEMO-REG-03): expected Added plaintext credential assignments remain detectable when followed by ordinary configuration delimiters.; observed The new unquoted-value alternative accepts only comma, semicolon, hash, or end-of-input after the value. For an added line such as {password=abcdefgh1234}, the closing brace prevents a match. The previous regex matched this assignment. M2ContextReader uses the same restriction, so the value can also remain unredacted in model context.; reproduce: Call DiffSecretScanner.ScanAsync with a diff containing +++ b/config.txt, a valid hunk header, and +{password=abcdefgh1234}. Check whether findings contains the added line. Then pass the same diff through M2ContextReader.ReadAsync and inspect context.Text for abcdefgh1234. These outcomes are inferred from the regexes; neither method was executed.

## Candidate tests and execution

- `demo/Demo.Tests/AdditionalRegistrationPositiveTests.cs` — positive; DEMO-REG-01, DEMO-REG-04, DEMO-REG-07
- `demo/Demo.Tests/AdditionalRegistrationNegativeTests.cs` — negative; DEMO-REG-01, DEMO-REG-07
- `demo/Demo.Tests/AdditionalRegistrationAccessControlTests.cs` — access-control; DEMO-REG-05
- **Tests:** 75 total; 0 failed
- **restore:** passed (exit 0);   Determining projects to restore...   Restored /src/src/QaAgent.Domain/QaAgent.Domain.csproj (in 52 ms).   Restored /src/src/QaAgent.Application/QaAgent.Application.csproj (in 55 ms).   Restored /src/demo/Demo.Web/Demo.Web.csproj (in 3.31 sec).   Restored /src/tools/M3Probe/M3Probe.csproj (in 3.41 sec).   Restored /src/tests/QaAgent.IntegrationTests/QaAgent.IntegrationTests.csproj (in 4.37 sec).   Restored /src/tests/QaAgent.UnitTests/QaAgent.UnitTests.csproj (in 4.44 sec).   Restored /src/demo/Demo.Tests/Demo.Tests.csproj (in 4.56 sec).   Restored /src/src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj (in 5.21 sec).   Restored /src/src/QaAgent.Host/QaAgent.Host.csproj (in 5.21 sec). 
- **format-whitespace:** passed (exit 0); 
- **format-style:** passed (exit 0); 
- **build:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   M3Probe -&gt; /src/tools/M3Probe/bin/Debug/net10.0/M3Probe.dll   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll  Build succeeded.     0 Warning(s)     0 Error(s)  Time Elapsed 00:00:04.94 
- **test:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll Test run for /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll (.NETCoreApp,Version=v10.0) Test run for /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern.A total of 1 test files matched the specified pattern.    QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll Test run for /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern. Results File: /artifacts/test-results/_d97519a897b4_2026-10-22_07_32_13_net10.0.trx  Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 25 ms - QaAgent.UnitTests.dll (net10.0) Results File: /artifacts/test-results/_d97519a897b4_2026-10-22_07_32_13_net10.0\[1\].trx  Passed!  - Failed:     0, Passed:    37, Skipped:     0, Total:    37, Duration: 1 s - Demo.Tests.dll (net10.0) Results File: /artifacts/test-results/_d97519a897b4_2026-10-22_07_32_16_net10.0.trx  Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 3 s - QaAgent.IntegrationTests.dll (net10.0)  Attachments:   /artifacts/test-results/bfa1edfd-48fe-4d8d-9843-8804132d83b5/coverage.cobertura.xml   /artifacts/test-results/4ea49b65-d6f5-4f35-9ac4-caac96ffafe8/coverage.cobertura.xml   /artifacts/test-results/8b1f8505-f7a8-4071-950d-de5f535d62cf/coverage.cobertura.xml 
- **Stand:** available; 
- **Browser:** True; {"status":"PASSED","scenarios":\[{"name":"desktop","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]},{"name":"mobile","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]}\],"artifacts":{"directory":"/tmp/qa-agent-artifacts/eda299fcc6a340478f2d445b05cb4cf8/browser"}}
- **Whole project line coverage:** 46,73% (1272/2722)
- **Changed executable line coverage:** unavailable: No executable changed production lines were measured.

## Local evidence

- [browser-install.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/f474d87795a0e69522708183) (77 bytes)
- [browser.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/f89a7bda22cde16a3167fa9b) (837 bytes)
- [build.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/906be301a25b186e911bc2e6) (834 bytes)
- [checks-container.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/6bb301c1540ee4a6155a1b32) (1 bytes)
- [chromium-install.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/8da813fec6aafb2a592ad9a5) (1 bytes)
- [format-style.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/803825e78272808b9d672002) (0 bytes)
- [format-whitespace.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/44dc5ca935d13876287540bd) (0 bytes)
- [restore.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/bed0d23857bfe905296ea8d3) (701 bytes)
- [source-sha.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/4f4d908e9e9a08246a97f342) (42 bytes)
- [stand-start.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/5d18a495ed0dc083dd1283ff) (2155 bytes)
- [stand-stop.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/ccf017a5d2949367ce416808) (446 bytes)
- [test-results/4ea49b65-d6f5-4f35-9ac4-caac96ffafe8/coverage.cobertura.xml](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/ce95063b698e18ce042d45e4) (45232 bytes)
- [test-results/8b1f8505-f7a8-4071-950d-de5f535d62cf/coverage.cobertura.xml](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/04bb147e0170fe417942b7a8) (1494195 bytes)
- [test-results/_d97519a897b4_2026-10-22_07_32_13/In/d97519a897b4/coverage.cobertura.xml](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/d1c0feab5e32149dcfe05856) (118449 bytes)
- [test-results/_d97519a897b4_2026-10-22_07_32_13_net10.0.trx](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/86fa8ef12ac730b56440079d) (24282 bytes)
- [test-results/_d97519a897b4_2026-10-22_07_32_13_net10.0\[1\].trx](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/a192feb4384447da8f42ddec) (198973 bytes)
- [test-results/_d97519a897b4_2026-10-22_07_32_16/In/d97519a897b4/coverage.cobertura.xml](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/797d9ed5a7d321a631f95031) (1494195 bytes)
- [test-results/_d97519a897b4_2026-10-22_07_32_16_net10.0.trx](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/7862be1316fb13b2f6c58cff) (29220 bytes)
- [test-results/bfa1edfd-48fe-4d8d-9843-8804132d83b5/coverage.cobertura.xml](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/3749923e028d4ef4aa8d7a58) (118449 bytes)
- [test.log](http://127.0.0.1:5080/runs/eda299fc-c6a3-4047-8f2d-445b05cb4cf8/artifacts/a39ae344e68a9095d2cea01f) (2079 bytes)

## Context gaps

- Diff изменяет сканирование секретов и редактирование контекста; для этих компонентов нет соответствующих requirement IDs. Их проверки нельзя привязать к требованиям регистрации без расширения спецификации.
- Реализация Demo.Web и конфигурация хеширования не предоставлены; существующие тесты сами по себе не подтверждают стойкость хранения паролей.
- В предоставленных тестах нет запуска desktop и mobile Chromium; проверки HTML через HttpClient не подтверждают выполнение DEMO-REG-06 в браузерах.
- Не определено, как считать символы пароля для Unicode и какие именно символы считать пробельными; дополнительные Unicode-границы требуют уточнения.
