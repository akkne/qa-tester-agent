# QA result for `97b85697adfb`

- **Status:** Failed
- **Repository:** akkne/qa-tester-agent
- **Range:** `c34e317004b111bcb7c41bf2746742651f4c6cf2..97b85697adfb4af81f0ead7319da05da21c8f131`
- **Run ID:** `d3491fb6-04cf-4db8-930d-a213c6dafcef`
- **Result page:** http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef
- **Duration:** 302 seconds (at report creation)
- **LLM tokens:** 161097 input, 4181 output; 0 generation fixes

## Requirements and changed files

- `DEMO-REG-01`
- `DEMO-REG-02`
- `DEMO-REG-03`
- `DEMO-REG-04`
- `DEMO-REG-05`
- `DEMO-REG-06`
- `DEMO-REG-07`
- `M .env.demo.example`
- `M .env.development.example`
- `M .env.production.example`
- `M TODO.md`
- `M demo/Demo.Web/Accounts.cs`
- `M demo/docs/specs/registration.md`
- `A docs/codex-exec.md`
- `M docs/configuration.md`
- `M docs/m5-evidence.md`
- `M docs/phases.md`
- `M docs/security.md`
- `M src/QaAgent.Host/Composition/LlmServiceCollectionExtensions.cs`
- `M src/QaAgent.Host/Composition/ServiceCollectionExtensions.cs`
- `M src/QaAgent.Host/Worker/RunWorker.cs`
- `M src/QaAgent.Host/appsettings.json`
- `A src/QaAgent.Infrastructure/Llm/CodexExecLlmClient.cs`
- `A src/QaAgent.Infrastructure/Llm/CodexExecOptions.cs`
- `A src/QaAgent.Infrastructure/Llm/CodexExecRunner.cs`
- `D src/QaAgent.Infrastructure/Llm/LlmClientOptions.cs`
- `D src/QaAgent.Infrastructure/Llm/OpenAiCompatibleLlmClient.cs`
- `M tests/QaAgent.IntegrationTests/LlmClientTests.cs`

## Verification plan

- `DEMO-REG-07` boundary: Passwords of 12 and 128 characters containing a non-whitespace character are accepted; exactly one account is created per unique email. — Using fresh emails, register with ASCII passwords of lengths 12 and 128 through the API and registration form. Check success and account creation.
- `DEMO-REG-07` boundary: Passwords of 11 and 129 characters are rejected with understandable errors; no account is created. — Submit each length with a fresh valid email through the API and form. Compare account counts before and after; verify the submitted credentials cannot authenticate.
- `DEMO-REG-07` negative: Whitespace-only passwords are rejected with an understandable error and no account creation. — Register with fresh valid emails and passwords consisting of 12 spaces and 128 spaces, then whitespace-only tab/newline combinations through the API. Check errors and unchanged account counts. Repeat space cases through the form. This specifically probes the missing whitespace validation in the supplied AccountService.
- `DEMO-REG-07` positive: A password within the length range containing just one non-whitespace character satisfies the stated password rule. — Register separate accounts using 11 spaces followed by one ASCII letter and 127 spaces followed by one ASCII letter. Verify account creation and successful login with each original password.
- `DEMO-REG-01` positive: Registration succeeds and stores the email without surrounding spaces and in lowercase. — Register using '  Alice@Example.COM  ' and a valid password. Inspect the stored account and returned email; verify they equal 'alice@example.com'.
- `DEMO-REG-01` negative: Empty or invalid emails and passwords shorter than 12 characters are rejected without account creation. — Submit empty, whitespace-only, and malformed emails with a valid password; separately submit empty and 11-character passwords with valid emails. Exercise API and form validation and inspect account counts.
- `DEMO-REG-02` negative: Duplicate registration creates no second account and displays an understandable duplicate-email message. — Create an account, then repeat registration with the same email, a different case, and surrounding spaces. Check that only the original account remains and each attempt explains the duplicate.
- `DEMO-REG-03` positive: Only a strong password hash is persisted; API responses and rendered pages expose neither password nor hash. — Register a synthetic account and inspect persisted fields and password-hasher configuration. Verify the hash using the configured verifier and review algorithm/work-factor strength. Inspect registration, login, account API responses, and cabinet HTML for the original password and stored hash.
- `DEMO-REG-04` positive: A registered user can log in with correct credentials and view their own cabinet. — Register a user, log in through the API and form in separate sessions, and open the cabinet. Verify that the displayed identity belongs to that user.
- `DEMO-REG-04` negative: Incorrect credentials do not grant cabinet access. — In fresh unauthenticated sessions, try a known email with a wrong password and an unknown email. After each failed login, request the cabinet and account API and verify access remains denied.
- `DEMO-REG-05` access-control: Anonymous requests cannot view cabinet or account data. — Without session cookies, request '/cabinet', '/cabinet/{existingId}', '/api/me', and '/api/accounts/{existingId}'. Disable automatic redirects and verify that no protected data is returned.
- `DEMO-REG-05` access-control: User A can access their own cabinet but cannot retrieve user B's data by changing URL or ID. — Create users A and B with separate sessions. As A, confirm access to A's cabinet, then substitute B's ID in cabinet and account API URLs. Verify denial and absence of B's data; repeat with the users reversed.
- `DEMO-REG-06` positive: Registration and login forms have associated labels and stable data-testid attributes usable in desktop and mobile Chromium. — At desktop and mobile viewports, locate form inputs by their labels and primary controls by data-testid. Complete registration and login using the same selectors at both sizes.
- `DEMO-REG-06` negative: Validation and login errors are visible and understandable at both viewport sizes. — In desktop and mobile Chromium, submit an invalid email, a short password, a whitespace-only password, an oversized password, duplicate registration, and incorrect login credentials. Check visible error text and stable error selectors.

## Findings

- **hypothesis / medium** `demo/Demo.Web/Accounts.cs:68` (DEMO-REG-07): expected Reject passwords consisting entirely of whitespace with a clear error and without creating an account.; observed RegisterAsync checks only null and length bounds before hashing the password and calling store.TryCreateAsync. A password containing 12 spaces passes both length checks; no check requires a non-whitespace character. This is a static code inference, not an executed reproduction.; reproduce: Submit POST /api/register with a fresh valid email and a password containing exactly 12 spaces. Verify that the response rejects registration and that the account store remains unchanged. The inspected code predicts successful account creation instead. Repeat through the registration form to check the same validation path.

## Candidate tests and execution

- `demo/Demo.Tests/PasswordPolicyPositiveTests.cs` — positive; DEMO-REG-07, DEMO-REG-04
- `demo/Demo.Tests/PasswordPolicyNegativeTests.cs` — negative; DEMO-REG-07
- `demo/Demo.Tests/PasswordPolicyAccessControlTests.cs` — access-control; DEMO-REG-07, DEMO-REG-04, DEMO-REG-05
- **Tests:** 67 total; 7 failed
- **restore:** passed (exit 0);   Determining projects to restore...   Restored /src/src/QaAgent.Domain/QaAgent.Domain.csproj (in 65 ms).   Restored /src/src/QaAgent.Application/QaAgent.Application.csproj (in 69 ms).   Restored /src/demo/Demo.Web/Demo.Web.csproj (in 3.15 sec).   Restored /src/tools/M3Probe/M3Probe.csproj (in 3.21 sec).   Restored /src/tests/QaAgent.UnitTests/QaAgent.UnitTests.csproj (in 4.82 sec).   Restored /src/tests/QaAgent.IntegrationTests/QaAgent.IntegrationTests.csproj (in 4.82 sec).   Restored /src/demo/Demo.Tests/Demo.Tests.csproj (in 4.74 sec).   Restored /src/src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj (in 5.32 sec).   Restored /src/src/QaAgent.Host/QaAgent.Host.csproj (in 5.32 sec). 
- **format-whitespace:** passed (exit 0); 
- **format-style:** passed (exit 0); 
- **build:** passed (exit 0);   QaAgent.Domain -&gt; /src/src/QaAgent.Domain/bin/Debug/net10.0/QaAgent.Domain.dll   Demo.Web -&gt; /src/demo/Demo.Web/bin/Debug/net10.0/Demo.Web.dll   QaAgent.Application -&gt; /src/src/QaAgent.Application/bin/Debug/net10.0/QaAgent.Application.dll   QaAgent.UnitTests -&gt; /src/tests/QaAgent.UnitTests/bin/Debug/net10.0/QaAgent.UnitTests.dll   Demo.Tests -&gt; /src/demo/Demo.Tests/bin/Debug/net10.0/Demo.Tests.dll   QaAgent.Infrastructure -&gt; /src/src/QaAgent.Infrastructure/bin/Debug/net10.0/QaAgent.Infrastructure.dll   M3Probe -&gt; /src/tools/M3Probe/bin/Debug/net10.0/M3Probe.dll   QaAgent.Host -&gt; /src/src/QaAgent.Host/bin/Debug/net10.0/QaAgent.Host.dll   QaAgent.IntegrationTests -&gt; /src/tests/QaAgent.IntegrationTests/bin/Debug/net10.0/QaAgent.IntegrationTests.dll  Build succeeded.     0 Warning(s)     0 Error(s)  Time Elapsed 00:00:04.84 
- **test:** failed (exit 1); 28, whitespaceOnly: True) \[470 ms\]   Error Message:    Assert.Equal() Failure: Values differ Expected: Unauthorized Actual:   OK   Stack Trace:      at Demo.Tests.PasswordPolicyAccessControlTests.Rejected_credentials_cannot_authenticate_or_access_existing_account(Int32 length, Boolean whitespaceOnly) in /src/demo/Demo.Tests/PasswordPolicyAccessControlTests.cs:line 33 --- End of stack trace from previous location ---   Failed Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(password: "\\t\\r\\n\\t\\r\\n\\t\\r\\n\\t\\r\\n", useForm: False, expectedLimit: "") \[65 ms\]   Error Message:    Assert.Equal() Failure: Values differ Expected: BadRequest Actual:   Created   Stack Trace:      at Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(String password, Boolean useForm, String expectedLimit) in /src/demo/Demo.Tests/PasswordPolicyNegativeTests.cs:line 53 --- End of stack trace from previous location --- \[xUnit.net 00:00:00.70\]     Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(password: "            ", useForm: False, expectedLimit: "") \[FAIL\]   Failed Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(password: "            ", useForm: False, expectedLimit: "") \[54 ms\]   Error Message:    Assert.Equal() Failure: Values differ Expected: BadRequest Actual:   Created   Stack Trace:      at Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(String password, Boolean useForm, String expectedLimit) in /src/demo/Demo.Tests/PasswordPolicyNegativeTests.cs:line 53 --- End of stack trace from previous location --- \[xUnit.net 00:00:00.81\]     Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(password: "                                                  "···, useForm: True, expectedLimit: "") \[FAIL\] \[xUnit.net 00:00:00.84\]     Demo.Tests.PasswordPolicyAccessControlTests.Rejected_credentials_cannot_authenticate_or_access_existing_account(length: 12, whitespaceOnly: True) \[FAIL\]   Failed Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(password: "                                                  "···, useForm: True, expectedLimit: "") \[58 ms\]   Error Message:    Assert.Equal() Failure: Values differ Expected: BadRequest Actual:   Found   Stack Trace:      at Demo.Tests.PasswordPolicyNegativeTests.Invalid_password_has_clear_error_and_never_creates_account(String password, Boolean useForm, String expectedLimit) in /src/demo/Demo.Tests/PasswordPolicyNegativeTests.cs:line 53 --- End of stack trace from previous location ---   Failed Demo.Tests.PasswordPolicyAccessControlTests.Rejected_credentials_cannot_authenticate_or_access_existing_account(length: 12, whitespaceOnly: True) \[154 ms\]   Error Message:    Assert.Equal() Failure: Values differ Expected: Unauthorized Actual:   OK   Stack Trace:      at Demo.Tests.PasswordPolicyAccessControlTests.Rejected_credentials_cannot_authenticate_or_access_existing_account(Int32 length, Boolean whitespaceOnly) in /src/demo/Demo.Tests/PasswordPolicyAccessControlTests.cs:line 33 --- End of stack trace from previous location --- Results File: /artifacts/test-results/_6cec4a746fb8_2026-10-22_07_21_43_net10.0\[1\].trx  Failed!  - Failed:     7, Passed:    23, Skipped:     0, Total:    30, Duration: 1 s - Demo.Tests.dll (net10.0) Results File: /artifacts/test-results/_6cec4a746fb8_2026-10-22_07_21_46_net10.0.trx  Passed!  - Failed:     0, Passed:    19, Skipped:     0, Total:    19, Duration: 3 s - QaAgent.IntegrationTests.dll (net10.0)  Attachments:   /artifacts/test-results/201f8275-3b9b-417b-9e0b-46f09fd155c3/coverage.cobertura.xml   /artifacts/test-results/92e31d8c-f183-4262-ac77-a1332b61ff67/coverage.cobertura.xml   /artifacts/test-results/2902d595-7e98-468c-b052-2aefa42d413a/coverage.cobertura.xml 
- **Stand:** available; 
- **Browser:** True; {"status":"PASSED","scenarios":\[{"name":"desktop","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]},{"name":"mobile","status":"PASSED","steps":\["Anonymous user is redirected from /cabinet to /login","Register using the form","Invalid credentials do not open the cabinet","Valid credentials open the owned cabinet","Changing the cabinet ID cannot reveal another user","Duplicate email produces a clear form error","Logout removes access to the cabinet"\]}\],"artifacts":{"directory":"/tmp/qa-agent-artifacts/d3491fb604cf4db8930da213c6dafcef/browser"}}
- **Whole project line coverage:** 46,69% (1270/2720)
- **Changed executable line coverage:** unavailable: Cobertura has no line mapping for src/QaAgent.Infrastructure/Llm/CodexExecRunner.cs:10.

## Local evidence

- [browser-install.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/f474d87795a0e69522708183) (25 bytes)
- [browser.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/f89a7bda22cde16a3167fa9b) (837 bytes)
- [build.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/906be301a25b186e911bc2e6) (834 bytes)
- [checks-container.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/6bb301c1540ee4a6155a1b32) (1 bytes)
- [chromium-install.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/8da813fec6aafb2a592ad9a5) (1 bytes)
- [format-style.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/803825e78272808b9d672002) (0 bytes)
- [format-whitespace.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/44dc5ca935d13876287540bd) (0 bytes)
- [restore.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/bed0d23857bfe905296ea8d3) (701 bytes)
- [source-sha.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/4f4d908e9e9a08246a97f342) (42 bytes)
- [stand-start.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/5d18a495ed0dc083dd1283ff) (2160 bytes)
- [stand-stop.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/ccf017a5d2949367ce416808) (446 bytes)
- [test-results/201f8275-3b9b-417b-9e0b-46f09fd155c3/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/ba1634a2b819b31c14b082fe) (44614 bytes)
- [test-results/2902d595-7e98-468c-b052-2aefa42d413a/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/341bca28989ba5145f4f0fd2) (118449 bytes)
- [test-results/92e31d8c-f183-4262-ac77-a1332b61ff67/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/0a6db0df27fe81f80b5c9550) (1462516 bytes)
- [test-results/_6cec4a746fb8_2026-10-22_07_21_43/In/6cec4a746fb8/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/570561be3ec3e72c12baa350) (118449 bytes)
- [test-results/_6cec4a746fb8_2026-10-22_07_21_43_net10.0.trx](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/ccc48b94f16685e55c2ea4c9) (24282 bytes)
- [test-results/_6cec4a746fb8_2026-10-22_07_21_43_net10.0\[1\].trx](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/a23ec064849f37e545722cf7) (161745 bytes)
- [test-results/_6cec4a746fb8_2026-10-22_07_21_46/In/6cec4a746fb8/coverage.cobertura.xml](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/d07557969e7efa5095c2f248) (1462516 bytes)
- [test-results/_6cec4a746fb8_2026-10-22_07_21_46_net10.0.trx](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/471a0388427cc070d94941c3) (27855 bytes)
- [test.log](http://127.0.0.1:5080/runs/d3491fb6-04cf-4db8-930d-a213c6dafcef/artifacts/a39ae344e68a9095d2cea01f) (7519 bytes)

## Context gaps

- The CLI adapter and worker changes have no corresponding requirement IDs in the supplied context, so they cannot be assigned verification scenarios under the requested ID constraint.
- The context does not define whether password length counts UTF-16 code units, Unicode code points, or grapheme clusters, or precisely which characters count as whitespace. ASCII boundaries and ordinary spaces provide unambiguous initial cases.
- Endpoint implementations and browser test configuration are not supplied; routes shown in existing tests should be confirmed against the fixed target SHA.
- DEMO-REG-03 does not specify an approved hash algorithm or minimum work factor; these acceptance criteria are needed for an objective strength assessment.
