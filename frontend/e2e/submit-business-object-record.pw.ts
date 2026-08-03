import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;
const maildevURL = process.env.E2E_MAILDEV_URL;
const password = 'maple river sunrise';

interface LegalVersions {
  termsVersion: string;
  privacyVersion: string;
}

interface MaildevMessage {
  subject?: string;
  text?: string;
  to?: { address: string }[];
}

function uniqueEmail(): string {
  return `application.${Date.now()}.${Math.random().toString(36).slice(2, 8)}@test.com`;
}

async function clearMaildev(request: APIRequestContext): Promise<void> {
  if (maildevURL) await request.delete(`${maildevURL}/email/all`);
}

async function getLegalVersions(request: APIRequestContext): Promise<LegalVersions> {
  const response = await request.get(`${apiURL}/api/legal/versions`);
  expect(response.ok()).toBe(true);
  return response.json();
}

async function registerUser(request: APIRequestContext, email: string): Promise<void> {
  const legalVersions = await getLegalVersions(request);
  const response = await request.post(`${apiURL}/api/users/register`, {
    headers: { 'Idempotency-Key': `e2e-application-${crypto.randomUUID()}` },
    data: {
      fullName: 'Application User',
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
          (candidate) =>
            candidate.subject === 'Verify your email address' &&
            candidate.to?.some((recipient) => recipient.address.toLowerCase() === email),
        );
        return message?.text?.match(/token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
      },
      { timeout: 30_000, message: `verification email for ${email}` },
    )
    .not.toBe('');

  const response = await request.get(`${maildevURL}/email`);
  const messages = (await response.json()) as MaildevMessage[];
  const message = messages.find(
    (candidate) =>
      candidate.subject === 'Verify your email address' &&
      candidate.to?.some((recipient) => recipient.address.toLowerCase() === email),
  );
  const token = message?.text?.match(/token=([A-Za-z0-9_-]+)/)?.[1];
  if (!token) throw new Error(`Verification token was not found for ${email}.`);
  return token;
}

async function createVerifiedUser(request: APIRequestContext, email: string): Promise<void> {
  await registerUser(request, email);
  const token = await waitForVerificationToken(request, email);
  const response = await request.post(`${apiURL}/api/auth/verify-email`, {
    data: { token },
  });
  expect(response.ok()).toBe(true);
}

async function signIn(page: Page, email: string): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
}

test.describe('submit business object record', () => {
  test.skip(!apiURL, 'Set E2E_API_URL to run application workflow API setup.');

  test.beforeEach(async ({ request }) => {
    await clearMaildev(request);
  });

  test('AT-009 user saves and submits a valid application through the workflow UI', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run application verification.');

    const pageErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') pageErrors.push(message.text());
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    const email = uniqueEmail();
    await createVerifiedUser(request, email);
    await signIn(page, email);

    await page.getByRole('link', { name: 'Applications', exact: true }).click();
    await expect(page).toHaveURL(/\/applications(?:\?|$)/);
    await page.getByRole('button', { name: 'Set up workflow' }).click();
    await expect(page.getByRole('button', { name: 'New application' })).toBeVisible({
      timeout: 30_000,
    });

    await page.getByRole('button', { name: 'New application' }).click();
    const dialog = page.getByRole('dialog', { name: 'Loan application' });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel(/Applicant name/).fill('Ada Lovelace');
    await dialog.getByLabel(/Contact email/).fill('ada@example.com');
    await dialog.getByLabel(/Requested amount/).fill('12000');
    await dialog.getByLabel(/Purpose/).fill('Platform workflow demonstration');

    await dialog.getByRole('button', { name: 'Save draft' }).click();
    await expect(dialog.getByText('Revision 2')).toBeVisible({ timeout: 30_000 });
    await dialog.getByRole('button', { name: 'Submit application' }).click();

    await expect(dialog.getByText('Application submitted')).toBeVisible({ timeout: 30_000 });
    await expect(dialog.getByText('Submitted', { exact: true })).toBeVisible();
    await expect(dialog.getByLabel(/Applicant name/)).toBeDisabled();
    await expect(dialog.getByText('Rule passed', { exact: true })).toHaveCount(6);
    expect(pageErrors).toEqual([]);
  });

  test('AT-009 user recovers from a rule mismatch before submitting', async ({ page, request }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run application verification.');

    const pageErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') pageErrors.push(message.text());
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    const email = uniqueEmail();
    await createVerifiedUser(request, email);
    await signIn(page, email);

    await page.getByRole('link', { name: 'Applications', exact: true }).click();
    await expect(page).toHaveURL(/\/applications(?:\?|$)/);
    await page.getByRole('button', { name: 'Set up workflow' }).click();
    await expect(page.getByRole('button', { name: 'New application' })).toBeVisible({
      timeout: 30_000,
    });

    await page.getByRole('button', { name: 'New application' }).click();
    const dialog = page.getByRole('dialog', { name: 'Loan application' });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel(/Applicant name/).fill('Ada Lovelace');
    await dialog.getByLabel(/Contact email/).fill('ada@example.com');
    await dialog.getByLabel(/Requested amount/).fill('50');
    await dialog.getByLabel(/Purpose/).fill('Recoverable workflow demonstration');

    await dialog.getByRole('button', { name: 'Submit application' }).click();
    await expect(dialog.getByText('Some rules need attention')).toBeVisible({ timeout: 30_000 });
    await expect(dialog.getByText('Needs attention', { exact: true })).toHaveCount(2);
    await expect(dialog.getByLabel(/Requested amount/)).toBeEnabled();

    await dialog.getByLabel(/Requested amount/).fill('12000');
    await dialog.getByRole('button', { name: 'Submit application' }).click();
    await expect(dialog.getByText('Application submitted')).toBeVisible({ timeout: 30_000 });
    await expect(dialog.getByLabel(/Applicant name/)).toBeDisabled();
    await expect(dialog.getByText('Rule passed', { exact: true })).toHaveCount(6);
    expect(pageErrors).toEqual([]);
  });
});
