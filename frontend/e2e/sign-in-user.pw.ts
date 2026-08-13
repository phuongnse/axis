import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;
const maildevURL = process.env.E2E_MAILDEV_URL;
const password = 'maple river sunrise';

interface LegalVersions {
  termsVersion: string;
  privacyVersion: string;
}

interface BrowserSession {
  csrfToken?: string;
}

interface MaildevRecipient {
  address: string;
}

interface MaildevMessage {
  subject?: string;
  text?: string;
  to?: MaildevRecipient[];
}

function uniqueEmail(prefix: string): string {
  return `${prefix}.${Date.now()}.${Math.random().toString(36).slice(2, 8)}@test.com`;
}

async function clearMaildev(request: APIRequestContext): Promise<void> {
  if (!maildevURL) return;
  await request.delete(`${maildevURL}/email/all`);
}

async function getLegalVersions(request: APIRequestContext): Promise<LegalVersions> {
  const response = await request.get(`${apiURL}/api/legal/versions`);
  expect(response.ok()).toBe(true);
  return response.json();
}

async function bootstrapAntiforgery(request: APIRequestContext): Promise<string> {
  const response = await request.get(`${apiURL}/api/auth/session`);
  expect(response.ok()).toBe(true);
  const session = (await response.json()) as BrowserSession;
  if (!session.csrfToken) throw new Error('The browser session did not return a CSRF token.');
  return session.csrfToken;
}

async function registerUserViaApi(request: APIRequestContext, email: string): Promise<void> {
  const legalVersions = await getLegalVersions(request);
  const response = await request.post(`${apiURL}/api/users/register`, {
    headers: {
      'Idempotency-Key': `e2e-sign-in-${crypto.randomUUID()}`,
      'X-CSRF-TOKEN': await bootstrapAntiforgery(request),
    },
    data: {
      fullName: 'Sign In User',
      email,
      password,
      passwordConfirmation: password,
      acceptedTermsVersion: legalVersions.termsVersion,
      acceptedPrivacyVersion: legalVersions.privacyVersion,
    },
  });
  expect(
    response.ok(),
    `registration E2E seed failed (${response.status()}): ${await response.text()}`,
  ).toBe(true);
}

async function waitForVerificationToken(
  request: APIRequestContext,
  email: string,
): Promise<string> {
  await expect
    .poll(
      async () => {
        const response = await request.get(`${maildevURL}/email`);
        if (!response.ok()) return '';

        const messages = (await response.json()) as MaildevMessage[];
        const message = messages.find(
          (item) =>
            item.subject === 'Verify your email address' &&
            item.to?.some((recipient) => recipient.address.toLowerCase() === email.toLowerCase()),
        );
        const match = message?.text?.match(/token=([A-Za-z0-9_-]+)/);
        return match?.[1] ?? '';
      },
      {
        message: `verification email for ${email}`,
        timeout: 30_000,
      },
    )
    .not.toBe('');

  const response = await request.get(`${maildevURL}/email`);
  const messages = (await response.json()) as MaildevMessage[];
  const message = messages.find(
    (item) =>
      item.subject === 'Verify your email address' &&
      item.to?.some((recipient) => recipient.address.toLowerCase() === email.toLowerCase()),
  );
  const match = message?.text?.match(/token=([A-Za-z0-9_-]+)/);
  const token = match?.[1];
  if (!token) {
    throw new Error(`Verification token was not found for ${email}.`);
  }
  return token;
}

async function createVerifiedUser(request: APIRequestContext, email: string): Promise<void> {
  await registerUserViaApi(request, email);
  const token = await waitForVerificationToken(request, email);
  const response = await request.post(`${apiURL}/api/auth/verify-email`, {
    headers: { 'X-CSRF-TOKEN': await bootstrapAntiforgery(request) },
    data: { token },
  });
  expect(
    response.ok(),
    `verification E2E seed failed (${response.status()}): ${await response.text()}`,
  ).toBe(true);
}

async function fillSignInForm(page: Page, email: string): Promise<void> {
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
}

async function expectAuthenticatedFrame(page: Page, userName: string): Promise<void> {
  await expect(page.getByRole('banner')).toContainText('Dashboard');
  await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
  await expect(page.getByRole('main')).toHaveText('');
  await page.getByRole('button', { name: /Account menu/ }).click();
  await expect(page.getByText(userName).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
  await page.keyboard.press('Escape');
}

function watchLanguagePreferenceWrites(page: Page): () => number {
  let writes = 0;
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (request.method() === 'PUT' && url.pathname === '/api/users/me/preferences/language') {
      writes += 1;
    }
  });
  return () => writes;
}

function watchThemePreferenceWrites(page: Page): () => number {
  let writes = 0;
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (request.method() === 'PUT' && url.pathname === '/api/users/me/preferences/theme') {
      writes += 1;
    }
  });
  return () => writes;
}

function watchBrowserSessionActivity(page: Page) {
  let sessionRequests = 0;
  const statuses: number[] = [];
  const origins: string[] = [];
  const consoleErrors: string[] = [];

  page.on('request', (request) => {
    const url = new URL(request.url());
    if (request.method() === 'GET' && url.pathname === '/api/auth/session') {
      sessionRequests += 1;
    }
    if (url.pathname === '/connect/authorize') {
      origins.push(url.origin);
    }
  });
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === '/connect/authorize') {
      statuses.push(response.status());
    }
  });
  page.on('console', (message) => {
    if (
      message.type() === 'error' &&
      (message.location().url.includes('/connect/authorize') || message.text().includes('401'))
    ) {
      consoleErrors.push(message.text());
    }
  });

  return {
    browserSessionRequests: () => sessionRequests,
    consoleErrors: () => consoleErrors,
    oauthOrigins: () => origins,
    oauthStatuses: () => statuses,
  };
}

function installVisitedPathRecorder() {
  const target = window as Window & { __axisVisitedPaths?: string[] };
  if (target.__axisVisitedPaths) return;

  const visitedPaths = [window.location.pathname];
  const recordPath = () => {
    visitedPaths.push(window.location.pathname);
  };
  const originalPushState = window.history.pushState.bind(window.history);
  const originalReplaceState = window.history.replaceState.bind(window.history);

  window.history.pushState = (...args) => {
    const result = originalPushState(...args);
    recordPath();
    return result;
  };
  window.history.replaceState = (...args) => {
    const result = originalReplaceState(...args);
    recordPath();
    return result;
  };
  window.addEventListener('popstate', recordPath);
  Object.defineProperty(window, '__axisVisitedPaths', {
    configurable: true,
    value: visitedPaths,
  });
}

async function recordVisitedPaths(page: Page): Promise<void> {
  await page.addInitScript(installVisitedPathRecorder);
  await page.evaluate(installVisitedPathRecorder);
}

async function getVisitedPaths(page: Page): Promise<string[]> {
  return page.evaluate(
    () => (window as Window & { __axisVisitedPaths?: string[] }).__axisVisitedPaths ?? [],
  );
}

test.describe('sign in user', () => {
  test.skip(!apiURL, 'Set E2E_API_URL to run sign-in-user API setup.');

  test.beforeEach(async ({ request }) => {
    await clearMaildev(request);
  });

  test('AT-001 verified standalone user signs in and reaches dashboard', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run sign-in-user email verification.');

    const email = uniqueEmail('sign001');
    const sessionActivity = watchBrowserSessionActivity(page);
    const languageWrites = watchLanguagePreferenceWrites(page);
    const themeWrites = watchThemePreferenceWrites(page);
    await createVerifiedUser(request, email);

    await page.goto('/sign-in');
    await recordVisitedPaths(page);
    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expectAuthenticatedFrame(page, 'Sign In User');
    await expect.poll(() => getVisitedPaths(page)).not.toContain('/callback');
    expect(sessionActivity.browserSessionRequests()).toBe(2);
    expect(sessionActivity.oauthOrigins()).toEqual([]);
    expect(sessionActivity.oauthStatuses()).toEqual([]);
    expect(sessionActivity.consoleErrors()).toEqual([]);
    expect(languageWrites()).toBe(0);
    expect(themeWrites()).toBe(0);
  });

  test('AT-002 unauthenticated dashboard access routes to sign-in with registration link', async ({
    page,
  }) => {
    const sessionActivity = watchBrowserSessionActivity(page);
    await page.goto('/dashboard');

    await expect(page).toHaveURL(/\/sign-in$/);
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await page.getByRole('link', { name: /create account/i }).click();
    await expect(page).toHaveURL(/\/register$/);
    const sessionCountBeforeHover = sessionActivity.browserSessionRequests();
    await page.getByRole('link', { name: 'Sign in' }).hover();

    expect(sessionCountBeforeHover).toBe(1);
    expect(sessionActivity.browserSessionRequests()).toBe(sessionCountBeforeHover);
    expect(sessionActivity.oauthOrigins()).toEqual([]);
    expect(sessionActivity.oauthStatuses()).toEqual([]);
    expect(sessionActivity.consoleErrors()).toEqual([]);
  });

  test('AT-006 unverified sign-in separates warning, resend action, and feedback', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const email = uniqueEmail('sign006');
    await page.route('**/api/auth/sign-in', async (route) => {
      await route.fulfill({
        status: 422,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'identity.signIn.verificationRequired' }),
      });
    });
    await page.route('**/api/auth/resend-verification', async (route) => {
      await route.fulfill({ status: 204, body: '' });
    });

    await page.goto('/sign-in');
    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();

    const verificationNotice = page.getByRole('alert');
    await expect(verificationNotice).toContainText('Email not verified');
    await expect(verificationNotice).toContainText(
      'Email verification is required before sign-in.',
    );
    await expect(verificationNotice.getByRole('button')).toHaveCount(0);
    await expect(page.getByText("Didn't receive it?")).toBeVisible();
    const resendAction = page.getByRole('button', {
      name: /resend verification email/i,
    });
    await expect(resendAction).toHaveText('Resend email');
    await expect(resendAction).toHaveCSS('text-decoration-line', 'none');
    const resendAppearance = await resendAction.evaluate((element) => {
      const style = getComputedStyle(element);
      const rowStyle = element.parentElement ? getComputedStyle(element.parentElement) : null;
      return {
        fontSize: style.fontSize,
        fontWeight: style.fontWeight,
        height: style.height,
        iconCount: element.querySelectorAll('svg').length,
        rowColumnGap: rowStyle?.columnGap ?? null,
        paddingInlineStart: style.paddingInlineStart,
      };
    });
    expect(resendAppearance).toEqual({
      fontSize: '12px',
      fontWeight: '500',
      height: '16px',
      iconCount: 0,
      rowColumnGap: '4px',
      paddingInlineStart: '0px',
    });
    const createAccountAppearance = await page
      .getByRole('link', { name: 'Create account' })
      .evaluate((element) => {
        const style = getComputedStyle(element);
        return {
          fontSize: style.fontSize,
          fontWeight: style.fontWeight,
          height: style.height,
          paddingInlineStart: style.paddingInlineStart,
        };
      });
    expect(createAccountAppearance).toEqual({
      fontSize: resendAppearance.fontSize,
      fontWeight: resendAppearance.fontWeight,
      height: resendAppearance.height,
      paddingInlineStart: resendAppearance.paddingInlineStart,
    });
    await resendAction.hover();
    await expect(resendAction).toHaveCSS('text-decoration-line', 'underline');

    await resendAction.click();
    const feedback = page.getByRole('status');
    await expect(feedback).toHaveText('Verification email sent.');
    expect(
      await feedback.evaluate((element) => ({
        fontSize: getComputedStyle(element).fontSize,
        successTone: element.classList.contains('text-success'),
      })),
    ).toEqual({ fontSize: '12px', successTone: true });
    await expect(verificationNotice).not.toContainText('Verification email sent.');
    await expect(page.getByRole('alert')).toHaveCount(1);
  });

  test('AT-013 protected route reload restores from the browser authorization session', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run sign-in-user email verification.');

    const email = uniqueEmail('sign013');
    await createVerifiedUser(request, email);

    await page.goto('/sign-in');
    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expectAuthenticatedFrame(page, 'Sign In User');

    await page.reload();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expectAuthenticatedFrame(page, 'Sign In User');
  });

  test('AT-014 authenticated user is routed away from public auth and registration routes', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run sign-in-user email verification.');

    const email = uniqueEmail('sign014');
    await createVerifiedUser(request, email);

    await page.goto('/sign-in');
    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expectAuthenticatedFrame(page, 'Sign In User');

    for (const path of [
      '/sign-in',
      '/register',
      '/register/confirmation',
      '/auth/verify?token=already-authenticated',
    ]) {
      await page.goto(path);
      await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
      await expectAuthenticatedFrame(page, 'Sign In User');
    }
  });

  test('AT-015 app root routes by the current session state without a guest-route hop', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run sign-in-user email verification.');

    const email = uniqueEmail('sign015');
    const sessionActivity = watchBrowserSessionActivity(page);
    await createVerifiedUser(request, email);

    await page.goto('/');
    await expect(page).toHaveURL(/\/sign-in$/);
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    expect(sessionActivity.browserSessionRequests()).toBe(1);
    expect(sessionActivity.oauthOrigins()).toEqual([]);
    expect(sessionActivity.oauthStatuses()).toEqual([]);
    expect(sessionActivity.consoleErrors()).toEqual([]);

    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expectAuthenticatedFrame(page, 'Sign In User');

    await recordVisitedPaths(page);
    await page.goto('/');

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expectAuthenticatedFrame(page, 'Sign In User');
    await expect.poll(() => getVisitedPaths(page)).not.toContain('/sign-in');
    expect(sessionActivity.browserSessionRequests()).toBe(3);
    expect(sessionActivity.oauthOrigins()).toEqual([]);
    expect(sessionActivity.oauthStatuses()).toEqual([]);
    expect(sessionActivity.consoleErrors()).toEqual([]);
  });
});
