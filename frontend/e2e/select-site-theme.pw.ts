import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;
const maildevURL = process.env.E2E_MAILDEV_URL;
const password = 'maple river sunrise';

interface LegalVersions {
  termsVersion: string;
  privacyVersion: string;
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

async function registerUserViaApi(request: APIRequestContext, email: string): Promise<void> {
  const legalVersions = await getLegalVersions(request);
  const response = await request.post(`${apiURL}/api/users/register`, {
    headers: {
      'Idempotency-Key': `e2e-theme-${crypto.randomUUID()}`,
    },
    data: {
      fullName: 'Theme User',
      email,
      password,
      passwordConfirmation: password,
      acceptedTermsVersion: legalVersions.termsVersion,
      acceptedPrivacyVersion: legalVersions.privacyVersion,
    },
  });
  expect(response.ok()).toBe(true);
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
    data: { token },
  });
  expect(response.ok()).toBe(true);
}

async function fillSignInForm(page: Page, email: string): Promise<void> {
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
}

test.describe('select site theme', () => {
  test.beforeEach(async ({ request }) => {
    await clearMaildev(request);
  });

  test('AT-001 visitor selects a supported theme and keeps it after reload', async ({ page }) => {
    await page.goto('/sign-in');

    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'system');
    await page.getByLabel('Email address').fill('theme@example.com');
    await page.getByRole('button', { name: 'Preferences' }).click();
    await page.getByRole('button', { name: 'Dark' }).click();
    const selectedThemeOption = page.getByRole('button', { name: 'Dark' });
    await expect(selectedThemeOption).toHaveAttribute('aria-pressed', 'true');
    expect(
      await selectedThemeOption.evaluate((node) => getComputedStyle(node).backgroundColor),
    ).not.toBe('rgba(0, 0, 0, 0)');

    await expect(page.getByLabel('Email address')).toHaveValue('theme@example.com');
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'dark');
    expect(await page.evaluate(() => localStorage.getItem('axis.theme'))).toBe('dark');
    expect(await page.evaluate(() => document.documentElement.style.colorScheme)).toBe('dark');

    await page.reload();

    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'dark');
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('axis.theme'))).toBe('dark');
  });

  test('AT-002 authenticated theme preference is restored from the server after reload', async ({
    page,
    request,
  }) => {
    test.skip(!apiURL, 'Set E2E_API_URL to run authenticated theme setup.');
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run authenticated theme verification.');

    const email = uniqueEmail('theme002');
    await createVerifiedUser(request, email);

    await page.goto('/sign-in');
    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expect(page.getByRole('heading', { name: 'Theme User', level: 1 })).toBeVisible();

    await page.getByRole('button', { name: 'Preferences' }).click();
    await page.getByRole('button', { name: 'Dark' }).click();

    await expect(page.getByText('Theme saved')).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'dark');

    await page.evaluate(() => localStorage.removeItem('axis.theme'));
    await page.reload();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expect(page.locator('html')).toHaveClass(/dark/, { timeout: 30_000 });
    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'dark');
    await expect.poll(() => page.evaluate(() => localStorage.getItem('axis.theme'))).toBe('dark');
  });
});
