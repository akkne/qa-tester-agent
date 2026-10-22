import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';

function options(args) {
  const result = {};
  for (let index = 0; index < args.length; index += 2) {
    const key = args[index];
    if (!['--base-url', '--artifacts'].includes(key) || !args[index + 1]) {
      throw new Error('Usage: node browser/run.mjs --base-url URL --artifacts DIRECTORY');
    }
    result[key] = args[index + 1];
  }
  if (!result['--base-url'] || !result['--artifacts']) {
    throw new Error('Both --base-url and --artifacts are required');
  }
  return {
    baseUrl: new URL(result['--base-url']).toString().replace(/\/$/, ''),
    artifacts: path.resolve(result['--artifacts'])
  };
}

async function scenario(browser, name, baseUrl, artifacts) {
  const context = await browser.newContext({
    baseURL: baseUrl,
    viewport: name === 'mobile' ? { width: 390, height: 844 } : { width: 1280, height: 800 },
    isMobile: name === 'mobile',
    hasTouch: name === 'mobile',
    deviceScaleFactor: name === 'mobile' ? 2 : 1
  });
  const page = await context.newPage();
  page.setDefaultTimeout(15_000);
  const diagnostics = { steps: [], consoleErrors: [], pageErrors: [], failedRequests: [], serverErrors: [] };
  page.on('console', message => {
    if (message.type() === 'error') diagnostics.consoleErrors.push(message.text());
  });
  page.on('pageerror', error => diagnostics.pageErrors.push(error.message));
  page.on('requestfailed', request => diagnostics.failedRequests.push({
    method: request.method(), url: request.url(), failure: request.failure()?.errorText
  }));
  page.on('response', response => {
    if (response.status() >= 500) diagnostics.serverErrors.push({
      status: response.status(), url: response.url()
    });
  });
  await context.tracing.start({ screenshots: true, snapshots: true, sources: true });

  const step = async (label, action) => {
    diagnostics.steps.push(label);
    await action();
  };
  const email = `browser-${name}-${randomUUID()}@example.test`;
  const invalidEmail = `browser-invalid-${name}-${randomUUID()}@example.test`;
  const otherEmail = `browser-other-${randomUUID()}@example.test`;
  const password = 'browser-test-password-123';
  const failureArtifacts = {};
  try {
    await step('Anonymous user is redirected from /cabinet to /login', async () => {
      await page.goto('/cabinet');
      assert.equal(new URL(page.url()).pathname, '/login');
      assert.equal(await page.getByTestId('page-title').textContent(), 'Вход');
    });

    await step('Whitespace-only password is rejected without creating an account', async () => {
      await page.goto('/register');
      await page.getByTestId('email-input').fill(invalidEmail);
      await page.getByTestId('password-input').fill(' '.repeat(12));
      await page.getByTestId('register-submit').click();
      assert.equal(new URL(page.url()).pathname, '/register');
      assert.match(await page.getByTestId('form-error').textContent(), /непробельный/);
      const login = await context.request.post('/api/login', {
        data: { email: invalidEmail, password: ' '.repeat(12) }
      });
      assert.equal(login.status(), 401);
    });

    await step('Register using the form', async () => {
      await page.goto('/register');
      assert.equal(await page.getByTestId('page-title').textContent(), 'Регистрация');
      assert.equal(await page.locator('label[for="email"]').count(), 1);
      assert.equal(await page.locator('label[for="password"]').count(), 1);
      await page.getByTestId('email-input').fill(email);
      await page.getByTestId('password-input').fill(password);
      await page.getByTestId('register-submit').click();
      await page.waitForURL('**/login?registered=1');
      assert.match(await page.getByTestId('success-message').textContent(), /Регистрация завершена/);
    });

    await step('Invalid credentials do not open the cabinet', async () => {
      await page.getByTestId('email-input').fill(email);
      await page.getByTestId('password-input').fill('incorrect-password');
      await page.getByTestId('login-submit').click();
      assert.match(await page.getByTestId('form-error').textContent(), /Неверный email или пароль/);
      assert.equal(new URL(page.url()).pathname, '/login');
    });

    await step('Valid credentials open the owned cabinet', async () => {
      await page.getByTestId('email-input').fill(email);
      await page.getByTestId('password-input').fill(password);
      await page.getByTestId('login-submit').click();
      await page.waitForURL('**/cabinet');
      assert.equal(await page.getByTestId('account-email').textContent(), email);
    });

    await step('Changing the cabinet ID cannot reveal another user', async () => {
      const response = await context.request.post('/api/register', {
        data: { email: otherEmail, password }
      });
      assert.equal(response.status(), 201);
      const other = await response.json();
      const denied = await page.goto(`/cabinet/${other.id}`);
      assert.equal(denied?.status(), 403);
      assert.equal((await page.locator('body').textContent()).includes(otherEmail), false);
      await page.goto('/cabinet');
      assert.equal(await page.getByTestId('account-email').textContent(), email);
    });

    await step('Duplicate email produces a clear form error', async () => {
      await page.goto('/register');
      await page.getByTestId('email-input').fill(email.toUpperCase());
      await page.getByTestId('password-input').fill(password);
      await page.getByTestId('register-submit').click();
      assert.match(await page.getByTestId('form-error').textContent(), /уже зарегистрирован/);
    });

    await step('Logout removes access to the cabinet', async () => {
      await page.goto('/cabinet');
      await page.getByTestId('logout-submit').click();
      await page.waitForURL(baseUrl + '/');
      await page.goto('/cabinet');
      assert.equal(new URL(page.url()).pathname, '/login');
    });

    assert.deepEqual(diagnostics.pageErrors, []);
    assert.deepEqual(diagnostics.failedRequests, []);
    assert.deepEqual(diagnostics.serverErrors, []);
    await context.tracing.stop();
    return { name, status: 'PASSED', steps: diagnostics.steps };
  } catch (error) {
    const screenshot = path.join(artifacts, `${name}.png`);
    const trace = path.join(artifacts, `${name}-trace.zip`);
    const log = path.join(artifacts, `${name}-diagnostics.json`);
    try {
      await page.screenshot({ path: screenshot, fullPage: true });
      failureArtifacts.screenshot = screenshot;
    } catch (screenshotError) {
      diagnostics.screenshotError = String(screenshotError);
    }
    try {
      await context.tracing.stop({ path: trace });
      failureArtifacts.trace = trace;
    } catch (traceError) {
      diagnostics.traceError = String(traceError);
    }
    diagnostics.error = error.stack || String(error);
    await writeFile(log, JSON.stringify(diagnostics, null, 2));
    failureArtifacts.diagnostics = log;
    return { name, status: 'FAILED', error: String(error), steps: diagnostics.steps, artifacts: failureArtifacts };
  } finally {
    await context.close();
  }
}

async function main() {
  let config;
  try {
    config = options(process.argv.slice(2));
    await mkdir(config.artifacts, { recursive: true });
    const response = await fetch(new URL('/healthz', config.baseUrl), { signal: AbortSignal.timeout(10_000) });
    if (!response.ok) throw new Error(`Demo stand returned HTTP ${response.status} from /healthz`);
  } catch (error) {
    console.log(JSON.stringify({ status: 'BLOCKED', reason: String(error), scenarios: [], artifacts: {} }));
    process.exitCode = 1;
    return;
  }

  let browser;
  try {
    browser = await chromium.launch({ headless: true });
    const scenarios = [];
    for (const name of ['desktop', 'mobile']) {
      scenarios.push(await scenario(browser, name, config.baseUrl, config.artifacts));
    }
    const status = scenarios.every(item => item.status === 'PASSED') ? 'PASSED' : 'FAILED';
    console.log(JSON.stringify({ status, scenarios, artifacts: { directory: config.artifacts } }));
    if (status !== 'PASSED') process.exitCode = 1;
  } catch (error) {
    console.log(JSON.stringify({ status: 'BLOCKED', reason: String(error), scenarios: [], artifacts: {} }));
    process.exitCode = 1;
  } finally {
    await browser?.close();
  }
}

await main();
