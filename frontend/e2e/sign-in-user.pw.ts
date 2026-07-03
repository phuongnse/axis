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
      'Idempotency-Key': `e2e-sign-in-${crypto.randomUUID()}`,
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
    await createVerifiedUser(request, email);

    await page.goto('/sign-in');
    await fillSignInForm(page, email);
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expect(page.getByRole('heading', { name: 'Sign In User', level: 1 })).toBeVisible();
    await expect(page.getByRole('definition').filter({ hasText: email })).toBeVisible();
    await expect(page.getByText('Account ready')).toBeVisible();
  });

  test('AT-002 unauthenticated dashboard access routes to sign-in with registration link', async ({
    page,
  }) => {
    await page.goto('/dashboard');

    await expect(page).toHaveURL(/\/sign-in$/);
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await page.getByRole('link', { name: /create an account/i }).click();
    await expect(page).toHaveURL(/\/register$/);
  });
});
