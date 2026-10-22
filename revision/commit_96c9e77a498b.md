# QA result for `96c9e77a498b`

- **Status:** Passed
- **Repository:** akkne/qa-tester-agent
- **Range:** `97b85697adfb4af81f0ead7319da05da21c8f131..96c9e77a498b3f44a0c338f379f15a486885dccf`
- **Run ID:** `d2a24b9b-1af1-4253-8828-88674aa2c929`
- **Result page:** http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929
- **Duration:** 147 seconds (at report creation)
- **LLM tokens:** 119559 input, 3604 output; 0 generation fixes

## Requirements and changed files

- `DEMO-REG-01`
- `DEMO-REG-02`
- `DEMO-REG-03`
- `DEMO-REG-04`
- `DEMO-REG-05`
- `DEMO-REG-06`
- `DEMO-REG-07`
- `A demo/Demo.Tests/PasswordPolicyAccessControlTests.cs`
- `A demo/Demo.Tests/PasswordPolicyNegativeTests.cs`
- `A demo/Demo.Tests/PasswordPolicyPositiveTests.cs`
- `M demo/Demo.Web/Accounts.cs`
- `M src/QaAgent.Application/Analysis/M2Processor.cs`
- `M src/QaAgent.Application/Analysis/M2Schemas.cs`
- `M src/QaAgent.Infrastructure/Analysis/CoberturaCoverageCalculator.cs`
- `M src/QaAgent.Infrastructure/Analysis/DiffSecretScanner.cs`
- `M src/QaAgent.Infrastructure/Analysis/M2ContextReader.cs`
- `M tests/QaAgent.IntegrationTests/CoberturaCoverageCalculatorTests.cs`
- `M tests/QaAgent.IntegrationTests/M2ContextAndGuardTests.cs`
- `A tests/QaAgent.UnitTests/M2SchemasTests.cs`

## Verification plan

- `DEMO-REG-01` positive: Registration creates one account with the email normalized to lowercase and without surrounding spaces. — Through the registration API and form separately, register a fresh email such as '  Alice@Example.COM  ' with a valid password; inspect the stored email and returned account identity.
- `DEMO-REG-01` negative: Empty or invalid email and passwords shorter than 12 characters are rejected without creating an account. — Using isolated cases through API and form, submit empty, whitespace-only and malformed emails with a valid password; submit a valid email with an empty or 11-character password. Verify clear errors and unchanged account count.
- `DEMO-REG-02` negative: An existing email, including case and surrounding-space variants, cannot create another account; a clear duplicate message is shown. — Create one account, then retry registration with the same email, an uppercase variant and a surrounding-space variant through API and form. Verify only the original account remains and its credentials still work.
- `DEMO-REG-03` positive: Persisted credentials contain a strong password hash rather than plaintext. — Register two accounts with the same valid password; inspect persisted credential fields, review the configured PasswordHasher algorithm and work factor, and verify the hashes using its verifier. Confirm plaintext is absent and separately salted hashes differ.
- `DEMO-REG-03` negative: API responses and pages expose neither the password nor its stored hash. — Capture registration, login, current-account and account-detail responses plus registration, login and cabinet HTML, including validation failures. Compare payloads against the known synthetic password and stored hash; inspect response fields for credential exposure.
- `DEMO-REG-04` positive: A registered user can sign in with the correct email and password and see their own cabinet. — Register a fresh account, sign in from a separate client through API and form independently, then open the cabinet and current-account endpoint. Verify the displayed identity matches the registered account.
- `DEMO-REG-04` negative: Incorrect credentials do not authenticate the client or grant cabinet access. — From fresh unauthenticated clients, attempt login with a known email and wrong password, an unknown email, and empty credentials. After each attempt, request the cabinet and current-account endpoint and verify access remains denied.
- `DEMO-REG-05` access-control: Anonymous clients cannot open a cabinet or retrieve account data. — Create an owner account, then use a separate anonymous client with redirects disabled to request /cabinet, /cabinet/{ownerId}, /api/me and /api/accounts/{ownerId}. Verify denial or login redirection and absence of owner data in response bodies.
- `DEMO-REG-05` access-control: Authenticated user A can access their own cabinet but cannot retrieve user B's data by changing the URL or account ID. — Register and authenticate A and B in separate clients. Confirm each can access their own cabinet and account data; substitute B's ID into A's cabinet and account-detail requests, then repeat in reverse. Verify denied responses contain no other-user data.
- `DEMO-REG-06` positive: Registration and login forms have associated labels and stable data-testid selectors for main controls in desktop and mobile Chromium. — In desktop and mobile Chromium, inspect label associations and locate the registration form, login form, submit controls and account display using data-testid. Complete registration and login with those selectors and verify the controls remain usable across reloads and viewport changes.
- `DEMO-REG-06` negative: Validation and login errors are visible and understandable in desktop and mobile Chromium. — On both Chromium viewport profiles, submit malformed email, invalid password, duplicate email and incorrect login credentials. Verify each displayed error explains the problem and can be located through a stable error selector.
- `DEMO-REG-07` positive: Passwords within the allowed range containing at least one non-whitespace character create an account that can authenticate. — Through API and form independently, register fresh accounts with a 24-character non-whitespace password and a 24-character password containing spaces and one non-whitespace character. Verify exactly one account per registration and successful login with the submitted password.
- `DEMO-REG-07` boundary: Lengths 12 and 128 are accepted when at least one character is non-whitespace; lengths 11 and 129 are rejected without creating an account. — Through API and form, test non-whitespace passwords of lengths 11, 12, 128 and 129 using unique emails. At lengths 12 and 128 also test spaces with exactly one non-whitespace character. Verify account-count changes and successful authentication only for accepted cases.
- `DEMO-REG-07` negative: Whitespace-only passwords are rejected with an understandable error and no account is created. — Through API and form where representable, submit 12 and 128 spaces and a 12-character mixture of tabs, carriage returns and newlines. Verify an error explaining the whitespace restriction, unchanged account count and no stored account for each email.
- `DEMO-REG-05` access-control: A rejected registration does not establish an authenticated session or grant access to an existing user's data. — Create an owner account. From fresh clients, attempt registration with 11-character, 129-character and whitespace-only passwords, then attempt login with those rejected credentials. Request /api/me, /cabinet, the owner's cabinet URL and the owner's account API URL. Verify access is denied, owner data is absent and only the owner account exists.

## Findings

No LLM findings.

## Candidate tests and execution

- `demo/Demo.Tests/RegistrationWhitespacePositiveGeneratedTests.cs` — positive; DEMO-REG-07, DEMO-REG-04, DEMO-REG-03
- `demo/Demo.Tests/RegistrationWhitespaceNegativeGeneratedTests.cs` — negative; DEMO-REG-07
- `demo/Demo.Tests/RegistrationWhitespaceAccessGeneratedTests.cs` — access-control; DEMO-REG-07, DEMO-REG-04, DEMO-REG-05
- **Tests:** 84 total; 0 failed
- **restore:** passed (exit 0);   Determining projects to restore...   Restored /src/src/QaAgent.Application/QaAgent.Application.csproj (in 72 ms).   Restored /src/src/QaAgent.Domain/QaAgent.Domain.csproj (in 1 ms).   Restored /src/demo/Demo.Web/Demo.Web.csproj (in 5.08 sec).   Restored /src/tools/M3Probe/M3Probe.csproj (in 5.5 sec).   Restored /src/demo/Demo.Tests/Demo.Tests.csproj (in 6.65 sec).   Restored /src/tests/QaAgent.UnitTests/QaAgent.UnitTests.csproj (in 6.66 sec).   Restored /src/tests/QaAgent.IntegrationTests/QaAgent.IntegrationTests.csproj (in 6.66 sec).   Restored /src/src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj (in 7.03 sec).   Restored /src/src/QaAgent.Host/QaAgent.Host.csproj (in 7.03 sec). 
- **format-whitespace:** passed (exit 0); 
- **format-style:** passed (exit 0); 
- **build:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   M3Probe -&gt; /src/tools/M3Probe/bin/Debug/net10.0/M3Probe.dll   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll  Build succeeded.     0 Warning(s)     0 Error(s)  Time Elapsed 00:00:04.72 
- **test:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll Test run for /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll (.NETCoreApp,Version=v10.0)   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll Test run for /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern. A total of 1 test files matched the specified pattern.   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll Test run for /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll (.NETCoreApp,Version=v10.0) A total of 1 test files matched the specified pattern. Results File: /artifacts/test-results/_b356b581bb50_2026-10-22_07_52_02_net10.0.trx  Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 32 ms - QaAgent.UnitTests.dll (net10.0) Results File: /artifacts/test-results/_b356b581bb50_2026-10-22_07_52_03_net10.0.trx  Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42, Duration: 1 s - Demo.Tests.dll (net10.0) Results File: /artifacts/test-results/_b356b581bb50_2026-10-22_07_52_06_net10.0.trx  Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 3 s - QaAgent.IntegrationTests.dll (net10.0)  Attachments:   /artifacts/test-results/d7b78e47-871c-4c6d-8231-5043eb3732b3/coverage.cobertura.xml   /artifacts/test-results/6918810e-04e3-4c09-9474-a0a050c28545/coverage.cobertura.xml   /artifacts/test-results/24d91d70-99f8-43bf-a2a9-d82efd99fbe5/coverage.cobertura.xml 
- **Stand:** available; 
- **Browser:** True; {"status":"PASSED","scenarios":\[{"name":"desktop","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]},{"name":"mobile","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]}\],"artifacts":{"directory":"/tmp/qa-agent-artifacts/d2a24b9b1af14253882888674aa2c929/browser"}}
- **Whole project line coverage:** 47,27% (1301/2752)
- **Changed executable line coverage:** 95,65% (22/23)

## Local evidence

- [browser-install.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/f474d87795a0e69522708183) (77 bytes)
- [browser.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/f89a7bda22cde16a3167fa9b) (837 bytes)
- [build.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/906be301a25b186e911bc2e6) (834 bytes)
- [checks-container.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/6bb301c1540ee4a6155a1b32) (1 bytes)
- [chromium-install.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/8da813fec6aafb2a592ad9a5) (1 bytes)
- [format-style.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/803825e78272808b9d672002) (0 bytes)
- [format-whitespace.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/44dc5ca935d13876287540bd) (0 bytes)
- [restore.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/bed0d23857bfe905296ea8d3) (699 bytes)
- [source-sha.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/4f4d908e9e9a08246a97f342) (42 bytes)
- [stand-start.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/5d18a495ed0dc083dd1283ff) (2147 bytes)
- [stand-stop.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/ccf017a5d2949367ce416808) (446 bytes)
- [test-results/24d91d70-99f8-43bf-a2a9-d82efd99fbe5/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/3c3a1fd0f6bec8761624c902) (127161 bytes)
- [test-results/6918810e-04e3-4c09-9474-a0a050c28545/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/9681d65ec21f590354a3fd4a) (1588616 bytes)
- [test-results/_b356b581bb50_2026-10-22_07_52_02/In/b356b581bb50/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/81da814b61fdc105d75e7d1c) (127161 bytes)
- [test-results/_b356b581bb50_2026-10-22_07_52_02_net10.0.trx](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/fe9d0e5eb25d760565f6a3bc) (28707 bytes)
- [test-results/_b356b581bb50_2026-10-22_07_52_03/In/b356b581bb50/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/4c5544c3d78037e50cad5e0f) (45236 bytes)
- [test-results/_b356b581bb50_2026-10-22_07_52_03_net10.0.trx](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/e4634b0536f63327d64ad38b) (231130 bytes)
- [test-results/_b356b581bb50_2026-10-22_07_52_06/In/b356b581bb50/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/7159c190472379c3e1fb1b50) (1588616 bytes)
- [test-results/_b356b581bb50_2026-10-22_07_52_06_net10.0.trx](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/26210b5cad160fc8ba81e40f) (30627 bytes)
- [test-results/d7b78e47-871c-4c6d-8231-5043eb3732b3/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/45daab7c91e3e36b7f6d9fad) (45236 bytes)
- [test.log](http://127.0.0.1:5080/runs/d2a24b9b-1af1-4253-8828-88674aa2c929/artifacts/a39ae344e68a9095d2cea01f) (2076 bytes)

## Context gaps

- Route handlers, authentication configuration and page templates are absent; endpoint authorization and browser behavior need verification against the pinned application build.
- The supplied factory replaces PostgreSQL with an in-memory store; persistence, unique constraints and credential storage also need verification with PostgreSQL.
- The specification does not define whether password length counts UTF-16 code units, Unicode code points or grapheme clusters; non-ASCII length boundaries require clarification.
- Changes to analysis schemas, secret scanning, context redaction and coverage calculation have no corresponding requirement IDs in the supplied specification, so no business verification scenarios are assigned to them.
- The exact Git SHA and desktop/mobile Chromium viewport profiles are not supplied.
