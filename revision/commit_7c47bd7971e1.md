# QA result for `7c47bd7971e1`

- **Status:** Passed
- **Repository:** akkne/qa-tester-agent
- **Range:** `6e43bf711e80466c60b4d8d2788e3eba0e13e950..7c47bd7971e1a34334fc69d64b291882aa0d5463`
- **Run ID:** `b25f19d0-6bbf-4eb3-a555-bb8a6ea98604`
- **Result page:** http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604
- **Duration:** 163 seconds (at report creation)
- **LLM tokens:** 112252 input, 3624 output; 0 generation fixes

## Requirements and changed files

- `DEMO-REG-01`
- `DEMO-REG-02`
- `DEMO-REG-03`
- `DEMO-REG-04`
- `DEMO-REG-05`
- `DEMO-REG-06`
- `DEMO-REG-07`
- `M browser/run.mjs`

## Verification plan

- `DEMO-REG-01` positive: Registration creates one account with email normalized to alice@example.com. — Through the registration API and form, independently register a fresh account using '  Alice@Example.COM  ' and a valid password. Inspect the stored email and account count.
- `DEMO-REG-01` negative: Empty, whitespace-only, and malformed email values are rejected with understandable errors; no account is created. — Submit each email value through the API and form with an otherwise valid password. Check the error and verify storage remains unchanged.
- `DEMO-REG-02` negative: Duplicate submissions show an understandable duplicate-email message and leave exactly one account. — Register alice@example.com, then retry through the API and form using the same email, uppercase variants, and surrounding spaces. Inspect account count and original account identity.
- `DEMO-REG-03` positive: Stored credentials use a strong password hash without plaintext storage. — Register a synthetic account and inspect credential persistence and hashing configuration. Verify the algorithm and work factor rather than relying only on a hash prefix. Register another account with the same password and inspect salt handling.
- `DEMO-REG-03` negative: Neither the submitted password nor its stored hash appears in API responses or rendered pages. — Inspect registration, login, current-account, and own-account API responses and registration, login, and cabinet HTML. Include validation and authentication failures; compare responses against the known password and stored hash.
- `DEMO-REG-04` positive: Valid credentials authenticate the registered user and display that user's cabinet. — Register an account, then sign in from a fresh session through the API and form. Verify current-account identity and cabinet email. Repeat with case and surrounding-space variants of the email.
- `DEMO-REG-04` negative: Incorrect credentials do not establish an authenticated session or grant cabinet access. — In separate fresh sessions, attempt login using an existing email with a wrong password and an unknown email. Check login failure, current-account access, and cabinet access.
- `DEMO-REG-05` access-control: Anonymous requests cannot open a cabinet or obtain account data. — Create an owner account, then use a fresh anonymous session to request /cabinet, /cabinet/{ownerId}, /api/me, and /api/accounts/{ownerId}. Inspect response bodies and any redirects for owner-data disclosure.
- `DEMO-REG-05` access-control: Each authenticated user can access their own cabinet but cannot obtain the other user's data. — Create users A and B in isolated sessions. Verify own-account access, then substitute B's ID in A's cabinet and account API URLs. Repeat in the opposite direction and inspect denied response bodies for foreign data.
- `DEMO-REG-06` positive: Registration and login forms have associated labels, and main controls have stable, usable data-testid selectors on desktop and mobile Chromium. — Run registration and login flows in both Chromium profiles. Verify email and password label associations and locate titles, inputs, submits, navigation, and account identity using their data-testid selectors.
- `DEMO-REG-06` negative: Validation and login errors clearly describe the problem and remain readable on both browser profiles. — In desktop and mobile Chromium, submit invalid email, invalid password, duplicate email, and incorrect login credentials. Verify visible understandable errors and stable form-error selection.
- `DEMO-REG-07` boundary: Passwords of exactly 12 and 128 characters are accepted when they contain at least one non-whitespace character; registration creates one account and the exact password authenticates. — Through the API and form, test lengths 12 and 128 using all non-whitespace characters and spaces with exactly one non-whitespace character. Use fresh emails, verify storage increments once, and authenticate in a fresh session.
- `DEMO-REG-07` boundary: Passwords of 11 and 129 characters are rejected with understandable length errors and no account creation. — Through the API and form, submit otherwise valid registration data with non-whitespace passwords of lengths 11 and 129. Inspect errors and verify account count and email lookup remain unchanged.
- `DEMO-REG-07` negative: Empty and whitespace-only passwords are rejected with understandable errors; no account is created. — Through the API and form, submit empty passwords and passwords containing only 12 or 128 spaces. Also submit tabs and line breaks through the API. Verify errors, unchanged account count, and absence of the candidate email.
- `DEMO-REG-07` access-control: Rejected registration credentials cannot authenticate or access an existing owner's cabinet or data. — Create an owner account. In separate fresh candidate sessions, attempt registration with 11-character, 129-character, and whitespace-only passwords. Attempt candidate login, then request current-account, cabinet, owner cabinet, and owner account API routes. Verify denial, no owner-data disclosure, and no candidate record.

## Findings

No LLM findings.

## Candidate tests and execution

- `demo/Demo.Tests/RegistrationWhitespaceFormPositiveTests.cs` — positive; DEMO-REG-07, DEMO-REG-04
- `demo/Demo.Tests/RegistrationWhitespaceFormNegativeTests.cs` — negative; DEMO-REG-07, DEMO-REG-06
- `demo/Demo.Tests/RegistrationWhitespaceFormAccessTests.cs` — access-control; DEMO-REG-07, DEMO-REG-04, DEMO-REG-05
- **Tests:** 78 total; 0 failed
- **restore:** passed (exit 0);   Determining projects to restore...   Restored /src/src/QaAgent.Domain/QaAgent.Domain.csproj (in 57 ms).   Restored /src/src/QaAgent.Application/QaAgent.Application.csproj (in 61 ms).   Restored /src/demo/Demo.Web/Demo.Web.csproj (in 2.93 sec).   Restored /src/tools/M3Probe/M3Probe.csproj (in 3 sec).   Restored /src/tests/QaAgent.UnitTests/QaAgent.UnitTests.csproj (in 4.08 sec).   Restored /src/tests/QaAgent.IntegrationTests/QaAgent.IntegrationTests.csproj (in 4.15 sec).   Restored /src/demo/Demo.Tests/Demo.Tests.csproj (in 4.5 sec).   Restored /src/src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj (in 5.2 sec).   Restored /src/src/QaAgent.Host/QaAgent.Host.csproj (in 5.2 sec). 
- **format-whitespace:** passed (exit 0); 
- **format-style:** passed (exit 0); 
- **build:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   M3Probe -&gt; /src/tools/M3Probe/bin/Debug/net10.0/M3Probe.dll   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll  Build succeeded.     0 Warning(s)     0 Error(s)  Time Elapsed 00:00:04.94 
- **test:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll Test run for /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll (.NETCoreApp,Version=v10.0)   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll Test run for /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern. A total of 1 test files matched the specified pattern.   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll Test run for /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern. Results File: /artifacts/test-results/_57b8f0ed8be3_2026-10-22_07_59_28_net10.0.trx  Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 59 ms - QaAgent.UnitTests.dll (net10.0) Results File: /artifacts/test-results/_57b8f0ed8be3_2026-10-22_07_59_29_net10.0.trx  Passed!  - Failed:     0, Passed:    36, Skipped:     0, Total:    36, Duration: 1 s - Demo.Tests.dll (net10.0) Results File: /artifacts/test-results/_57b8f0ed8be3_2026-10-22_07_59_32_net10.0.trx  Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 4 s - QaAgent.IntegrationTests.dll (net10.0)  Attachments:   /artifacts/test-results/5ba8da39-ac61-4f52-b2fd-af3e685a0e39/coverage.cobertura.xml   /artifacts/test-results/5a3bb36c-a5f3-4b4d-b0fe-a5636a8abe38/coverage.cobertura.xml   /artifacts/test-results/bc622a7b-f08f-4381-9402-4f8e13ed2747/coverage.cobertura.xml 
- **Stand:** available; 
- **Browser:** True; {"status":"PASSED","scenarios":\[{"name":"desktop","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Whitespace-only password is rejected without creating an account","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]},{"name":"mobile","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Whitespace-only password is rejected without creating an account","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]}\],"artifacts":{"directory":"/tmp/qa-agent-artifacts/b25f19d06bbf4eb3a555bb8a6ea98604/browser"}}
- **Whole project line coverage:** 47,27% (1301/2752)
- **Changed executable line coverage:** unavailable: No executable changed production lines were measured.

## Local evidence

- [browser-install.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/f474d87795a0e69522708183) (77 bytes)
- [browser.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/f89a7bda22cde16a3167fa9b) (971 bytes)
- [build.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/906be301a25b186e911bc2e6) (834 bytes)
- [checks-container.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/6bb301c1540ee4a6155a1b32) (1 bytes)
- [chromium-install.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/8da813fec6aafb2a592ad9a5) (1 bytes)
- [format-style.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/803825e78272808b9d672002) (0 bytes)
- [format-whitespace.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/44dc5ca935d13876287540bd) (0 bytes)
- [restore.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/bed0d23857bfe905296ea8d3) (695 bytes)
- [source-sha.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/4f4d908e9e9a08246a97f342) (42 bytes)
- [stand-start.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/5d18a495ed0dc083dd1283ff) (2147 bytes)
- [stand-stop.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/ccf017a5d2949367ce416808) (446 bytes)
- [test-results/5a3bb36c-a5f3-4b4d-b0fe-a5636a8abe38/coverage.cobertura.xml](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/542a3289fab15b83cd8b994f) (1588616 bytes)
- [test-results/5ba8da39-ac61-4f52-b2fd-af3e685a0e39/coverage.cobertura.xml](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/14105da35ce243c581ccb8e9) (45234 bytes)
- [test-results/_57b8f0ed8be3_2026-10-22_07_59_28/In/57b8f0ed8be3/coverage.cobertura.xml](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/777c451010a415fe6d66307d) (127161 bytes)
- [test-results/_57b8f0ed8be3_2026-10-22_07_59_28_net10.0.trx](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/8b8df6233603a1152cc9b1c5) (28707 bytes)
- [test-results/_57b8f0ed8be3_2026-10-22_07_59_29/In/57b8f0ed8be3/coverage.cobertura.xml](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/0cc7b688e95d7a172834bc76) (45234 bytes)
- [test-results/_57b8f0ed8be3_2026-10-22_07_59_29_net10.0.trx](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/1793d1679edc960f3cf88fd9) (192908 bytes)
- [test-results/_57b8f0ed8be3_2026-10-22_07_59_32/In/57b8f0ed8be3/coverage.cobertura.xml](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/4d7ef696acb43c16f5389e9f) (1588616 bytes)
- [test-results/_57b8f0ed8be3_2026-10-22_07_59_32_net10.0.trx](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/3f9df56f08e6c331cfa344e1) (30627 bytes)
- [test-results/bc622a7b-f08f-4381-9402-4f8e13ed2747/coverage.cobertura.xml](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/0bdda81ad49a4d4e4163ebe9) (127161 bytes)
- [test.log](http://127.0.0.1:5080/runs/b25f19d0-6bbf-4eb3-a555-bb8a6ea98604/artifacts/a39ae344e68a9095d2cea01f) (2076 bytes)

## Context gaps

- The fixed Git SHA and runnable application configuration are not provided; bind execution results to the intended SHA.
- Application handlers and production persistence configuration are absent, so supplied tests alone cannot establish actual behavior or strong password hashing.
- Password character-count semantics for Unicode and the precise scope of whitespace are unspecified.
- Exact desktop and mobile Chromium profiles and the complete set of main elements requiring data-testid are unspecified.
