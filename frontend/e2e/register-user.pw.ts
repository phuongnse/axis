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
      'Idempotency-Key': `e2e-seed-${crypto.randomUUID()}`,
    },
    data: {
      firstName: 'Duplicate',
      lastName: 'User',
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

async function fillRegisterForm(page: Page, email: string): Promise<void> {
  await page.getByLabel('Full name').fill('Alex Rivers');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByLabel('Confirm password', { exact: true }).fill(password);
  await page.getByRole('checkbox', { name: /terms of service/i }).check();
}

test.describe('register user', () => {
  test.skip(!apiURL, 'Set E2E_API_URL to run register-user API setup.');

  test.beforeEach(async ({ request }) => {
    await clearMaildev(request);
  });

  test('REG-001 user registers, opens verification email, and reaches dashboard', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run register-user email verification.');

    const email = uniqueEmail('reg001');

    await page.goto('/register');
    await fillRegisterForm(page, email);
    await page.getByRole('button', { name: /create account/i }).click();

    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(page.getByText(`Sent to ${email}`)).toBeVisible();

    const token = await waitForVerificationToken(request, email);
    await page.goto(`/auth/verify?token=${token}`);

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expect(page.getByRole('heading', { name: 'Alex Rivers', level: 1 })).toBeVisible();
    await expect(page.getByText(email)).toBeVisible();
    await expect(page.getByText('Account ready')).toBeVisible();
  });

  test('REG-002 duplicate email shows an inline error', async ({ page, request }) => {
    const email = uniqueEmail('reg002');
    await registerUserViaApi(request, email);

    await page.goto('/register');
    await fillRegisterForm(page, email);
    await page.getByRole('button', { name: /create account/i }).click();

    await expect(
      page.getByText('An account with this email already exists. Sign in instead.'),
    ).toBeVisible();
    await expect(page.getByLabel('Email address')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByRole('button', { name: /create account/i })).toBeEnabled();
  });
});
