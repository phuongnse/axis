import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;
const maildevURL = process.env.E2E_MAILDEV_URL;
const password = 'maple river sunrise';
const personalWorkspaceName = 'Workspace Governance User';

interface LegalVersions {
  termsVersion: string;
  privacyVersion: string;
}

interface BrowserSession {
  csrfToken?: string;
  user?: {
    workspaceId?: string | null;
  } | null;
}

interface MaildevMessage {
  subject?: string;
  text?: string;
  to?: { address: string }[];
}

async function bootstrapAntiforgery(request: APIRequestContext): Promise<string> {
  const response = await request.get(`${apiURL}/api/auth/session`);
  expect(response.ok()).toBe(true);
  const session = (await response.json()) as BrowserSession;
  if (!session.csrfToken) throw new Error('The browser session did not return a CSRF token.');
  return session.csrfToken;
}

async function createVerifiedUser(request: APIRequestContext, email: string): Promise<void> {
  const legalResponse = await request.get(`${apiURL}/api/legal/versions`);
  expect(legalResponse.ok()).toBe(true);
  const legal = (await legalResponse.json()) as LegalVersions;
  const registration = await request.post(`${apiURL}/api/users/register`, {
    headers: {
      'Idempotency-Key': `workspace-e2e-${crypto.randomUUID()}`,
      'X-CSRF-TOKEN': await bootstrapAntiforgery(request),
    },
    data: {
      fullName: 'Workspace Governance User',
      email,
      password,
      passwordConfirmation: password,
      acceptedTermsVersion: legal.termsVersion,
      acceptedPrivacyVersion: legal.privacyVersion,
    },
  });
  expect(
    registration.ok(),
    `Registration failed with ${registration.status()}: ${await registration.text()}`,
  ).toBe(true);

  const token = await verificationToken(request, email);
  const verification = await request.post(`${apiURL}/api/auth/verify-email`, {
    headers: { 'X-CSRF-TOKEN': await bootstrapAntiforgery(request) },
    data: { token },
  });
  expect(verification.ok()).toBe(true);
}

async function verificationToken(request: APIRequestContext, email: string): Promise<string> {
  let token = '';
  await expect
    .poll(
      async () => {
        const response = await request.get(`${maildevURL}/email`);
        if (!response.ok()) return '';
        const messages = (await response.json()) as MaildevMessage[];
        const message = messages.find(
          (candidate) =>
            candidate.subject === 'Verify your email address' &&
            candidate.to?.some(
              (recipient) => recipient.address.toLowerCase() === email.toLowerCase(),
            ),
        );
        token = message?.text?.match(/token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
        return token;
      },
      { message: `verification email for ${email}`, timeout: 30_000 },
    )
    .not.toBe('');
  return token;
}

async function signIn(page: Page, email: string): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
}

async function expectControlLabel(page: Page, label: string): Promise<void> {
  await expect(page.getByRole('button', { name: 'Workspace control' })).toContainText(label, {
    timeout: 30_000,
  });
}

async function activeWorkspaceId(page: Page): Promise<string> {
  const response = await page.request.get('/api/auth/session');
  expect(response.ok()).toBe(true);
  const session = (await response.json()) as BrowserSession;
  if (!session.user?.workspaceId) throw new Error('The browser session has no active Workspace.');
  return session.user.workspaceId;
}

async function switchWorkspace(page: Page, workspaceName: string): Promise<string> {
  await page.getByRole('button', { name: 'Workspace control' }).click();
  await page.getByRole('button', { name: workspaceName, exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expectControlLabel(page, workspaceName);
  return activeWorkspaceId(page);
}

async function createOrganizationAndEnter(page: Page, organizationName: string): Promise<string> {
  await page.getByRole('button', { name: 'Workspace control' }).click();
  await page.getByRole('button', { name: 'Create Organization' }).click();

  const createDialog = page.getByRole('dialog', { name: 'Create Organization' });
  await createDialog.getByRole('textbox', { name: 'Organization name' }).fill(organizationName);
  await createDialog.getByRole('button', { name: 'Create Organization' }).click();

  const resultDialog = page.getByRole('dialog', { name: 'Organization created' });
  await expect(resultDialog.getByRole('heading', { name: 'Organization created' })).toBeVisible();
  await resultDialog.getByRole('button', { name: 'Enter Workspace' }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expectControlLabel(page, organizationName);
  return activeWorkspaceId(page);
}

async function createWorkspaceDefinition(page: Page, name: string): Promise<void> {
  await page.goto('/business-objects');
  await expect(page).toHaveURL(/\/business-objects\?page=1$/);

  await page.getByRole('button', { name: 'New definition' }).click();
  const dialog = page.locator('[data-slot="dialog-content"]');
  await dialog.getByLabel('Name', { exact: true }).fill(name);
  await dialog.getByRole('button', { name: 'Start definition' }).click();
  await expect(dialog.getByRole('heading', { name, exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: 'Close dialog' }).click();

  await expect(page.getByLabel('Definitions').getByText(name, { exact: true })).toBeVisible();
}

async function expectWorkspaceDefinitions(
  page: Page,
  expectedName: string,
  priorWorkspaceName: string,
): Promise<void> {
  await page.goto('/business-objects');
  await expect(page).toHaveURL(/\/business-objects\?page=1$/);
  const definitions = page.getByLabel('Definitions');
  await expect(definitions.getByText(expectedName, { exact: true })).toBeVisible();
  await expect(definitions.getByText(priorWorkspaceName, { exact: true })).toHaveCount(0);
}

test.describe('organization creation and Workspace switching', () => {
  test.skip(!apiURL || !maildevURL, 'Set E2E_API_URL and E2E_MAILDEV_URL for Workspace evidence.');

  test.beforeEach(async ({ request }) => {
    await request.delete(`${maildevURL}/email/all`);
  });

  test('AT-001 creates an Organization, enters its Workspace, and returns to Personal', async ({
    page,
    request,
  }) => {
    const email = `workspace.${Date.now()}.${Math.random().toString(36).slice(2, 8)}@test.com`;
    const organizationName = `Acme Operations ${Date.now()}`;
    await createVerifiedUser(request, email);
    await signIn(page, email);

    await expectControlLabel(page, personalWorkspaceName);
    const personalWorkspaceId = await activeWorkspaceId(page);
    const control = page.getByRole('button', { name: 'Workspace control' });
    await control.focus();
    await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Create Organization' }).click();

    const createDialog = page.getByRole('dialog', { name: 'Create Organization' });
    await createDialog.getByRole('textbox', { name: 'Organization name' }).fill(organizationName);
    await createDialog.getByRole('button', { name: 'Create Organization' }).click();

    const resultDialog = page.getByRole('dialog', { name: 'Organization created' });
    await expect(resultDialog.getByRole('heading', { name: 'Organization created' })).toBeVisible();
    await expect(resultDialog).toContainText('current Workspace stays active');
    expect(await activeWorkspaceId(page)).toBe(personalWorkspaceId);

    await resultDialog.getByRole('button', { name: 'Enter Workspace' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expectControlLabel(page, organizationName);

    await control.click();
    await page.getByRole('button', { name: personalWorkspaceName }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expectControlLabel(page, personalWorkspaceName);

    await page.setViewportSize({ width: 375, height: 720 });
    await control.click();
    await expect(page.getByRole('region', { name: 'Personal' })).toBeVisible();
    await expect
      .poll(() =>
        page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
      )
      .toBe(true);
  });

  test('AT-007 repeatedly switches Workspace-scoped reads and mutations without context leakage', async ({
    page,
    request,
  }) => {
    const suffix = `${Date.now()}.${Math.random().toString(36).slice(2, 8)}`;
    const email = `workspace.isolation.${suffix}@test.com`;
    const organizationName = `Isolation Operations ${suffix}`;
    const personalDefinitionName = `Personal definition ${suffix}`;
    const organizationDefinitionName = `Organization definition ${suffix}`;

    await createVerifiedUser(request, email);
    await signIn(page, email);
    const personalWorkspaceId = await activeWorkspaceId(page);

    await createWorkspaceDefinition(page, personalDefinitionName);

    const organizationWorkspaceId = await createOrganizationAndEnter(page, organizationName);
    expect(organizationWorkspaceId).not.toBe(personalWorkspaceId);
    await page.goto('/business-objects');
    await expect(
      page.getByLabel('Definitions').getByText(personalDefinitionName, { exact: true }),
    ).toHaveCount(0);
    await createWorkspaceDefinition(page, organizationDefinitionName);

    expect(await switchWorkspace(page, personalWorkspaceName)).toBe(personalWorkspaceId);
    await expectWorkspaceDefinitions(page, personalDefinitionName, organizationDefinitionName);

    expect(await switchWorkspace(page, organizationName)).toBe(organizationWorkspaceId);
    await expectWorkspaceDefinitions(page, organizationDefinitionName, personalDefinitionName);

    expect(await switchWorkspace(page, personalWorkspaceName)).toBe(personalWorkspaceId);
    await expectWorkspaceDefinitions(page, personalDefinitionName, organizationDefinitionName);
  });
});
