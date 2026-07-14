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
      'Idempotency-Key': `e2e-language-${crypto.randomUUID()}`,
    },
    data: {
      fullName: 'Language User',
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

async function expectOptionListLayout(page: Page): Promise<void> {
  const group = page.locator('[data-slot="toggle-group"][aria-label="Language"]');
  const option = page.getByRole('button', { name: 'Vietnamese' });
  const [groupBox, optionBox] = await Promise.all([group.boundingBox(), option.boundingBox()]);

  if (!groupBox || !optionBox) throw new Error('Language option layout was not measurable.');

  expect(Math.abs(groupBox.width - optionBox.width)).toBeLessThanOrEqual(1);
  expect(await option.evaluate((node) => getComputedStyle(node).justifyContent)).toBe('flex-start');
}

async function openPreferencesWithoutMovingForm(page: Page): Promise<void> {
  const form = page.locator('form');
  const topBefore = await form.evaluate((node) => node.getBoundingClientRect().top);
  const trigger = page.getByRole('button', { name: 'Preferences' });
  const triggerBox = await trigger.boundingBox();

  await trigger.click();
  await expect(page.getByRole('button', { name: 'Vietnamese' })).toBeVisible();
  await expectOptionListLayout(page);

  const topAfter = await form.evaluate((node) => node.getBoundingClientRect().top);
  const menuBox = await page
    .locator('[data-slot="popover-content"][aria-label="Preferences"]')
    .boundingBox();
  if (!triggerBox || !menuBox) {
    throw new Error('Preferences trigger/menu layout was not measurable.');
  }
  expect(topAfter).toBeCloseTo(topBefore, 1);
  expect(Math.abs(triggerBox.x + triggerBox.width - menuBox.x - menuBox.width)).toBeLessThanOrEqual(
    1,
  );
}

async function openAuthenticatedPreferences(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Account menu' }).click();
  await expect(page.getByRole('button', { name: 'Vietnamese' })).toBeVisible();
}

test.describe('select site language', () => {
  test.beforeEach(async ({ request }) => {
    await clearMaildev(request);
  });

  test('AT-001 visitor selects a supported language and keeps it after reload', async ({
    page,
  }) => {
    await page.goto('/sign-in');

    await openPreferencesWithoutMovingForm(page);
    await page.getByRole('button', { name: 'Vietnamese' }).click();

    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(page.getByRole('heading', { name: 'Đăng nhập' })).toBeVisible();
    await expect(page.getByLabel('Địa chỉ email')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Đăng nhập' })).toBeVisible();

    await page.reload();

    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(page.getByRole('heading', { name: 'Đăng nhập' })).toBeVisible();
    await expect(page.getByLabel('Địa chỉ email')).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('axis.language'))).toBe('vi');
  });

  test('AT-002 authenticated language preference is restored from the server after reload', async ({
    page,
    request,
  }) => {
    test.skip(!apiURL, 'Set E2E_API_URL to run authenticated language setup.');
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run authenticated language verification.');

    const email = uniqueEmail('lang002');
    await createVerifiedUser(request, email);

    await page.goto('/sign-in');
    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expect(page.getByRole('button', { name: 'Account menu' })).toBeVisible();

    await openAuthenticatedPreferences(page);
    await page.getByRole('button', { name: 'Vietnamese' }).click();

    await expect(page.getByText('Đã lưu ngôn ngữ')).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(page.getByRole('button', { name: 'Menu tài khoản' })).toBeVisible();

    await page.evaluate(() => localStorage.removeItem('axis.language'));
    await page.reload();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi', { timeout: 30_000 });
    await expect(page.getByRole('button', { name: 'Menu tài khoản' })).toBeVisible();
    await expect.poll(() => page.evaluate(() => localStorage.getItem('axis.language'))).toBe('vi');
  });
});
