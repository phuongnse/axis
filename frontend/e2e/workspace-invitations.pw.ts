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
  user?: { workspaceId?: string | null } | null;
}

interface MaildevMessage {
  subject?: string;
  text?: string;
  to?: Array<{ address: string }> | { address: string };
}

async function csrf(request: APIRequestContext): Promise<string> {
  const response = await request.get(`${apiURL}/api/auth/session`);
  expect(response.ok()).toBe(true);
  const session = (await response.json()) as BrowserSession;
  if (!session.csrfToken) throw new Error('The browser session did not return a CSRF token.');
  return session.csrfToken;
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
            addresses(candidate).some(
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

async function invitationLink(request: APIRequestContext, email: string): Promise<string> {
  let link = '';
  await expect
    .poll(
      async () => {
        const response = await request.get(`${maildevURL}/email`);
        if (!response.ok()) return '';
        const messages = (await response.json()) as MaildevMessage[];
        const message = messages.find(
          (candidate) =>
            candidate.subject?.startsWith('Invitation to join ') &&
            addresses(candidate).some(
              (recipient) => recipient.address.toLowerCase() === email.toLowerCase(),
            ),
        );
        link =
          message?.text?.match(/https?:\/\/\S+\/invitations\/accept#token=[a-fA-F0-9]{64}/)?.[0] ??
          '';
        return link;
      },
      { message: `Workspace invitation email for ${email}`, timeout: 30_000 },
    )
    .not.toBe('');
  return link;
}

function addresses(message: MaildevMessage): Array<{ address: string }> {
  if (!message.to) return [];
  return Array.isArray(message.to) ? message.to : [message.to];
}

async function registerAndVerifyViaApi(
  request: APIRequestContext,
  email: string,
  fullName: string,
): Promise<void> {
  const legalResponse = await request.get(`${apiURL}/api/legal/versions`);
  expect(legalResponse.ok()).toBe(true);
  const legal = (await legalResponse.json()) as LegalVersions;
  const registration = await request.post(`${apiURL}/api/users/register`, {
    headers: {
      'Idempotency-Key': `invitation-e2e-${crypto.randomUUID()}`,
      'X-CSRF-TOKEN': await csrf(request),
    },
    data: {
      fullName,
      email,
      password,
      passwordConfirmation: password,
      acceptedTermsVersion: legal.termsVersion,
      acceptedPrivacyVersion: legal.privacyVersion,
    },
  });
  expect(registration.ok(), await registration.text()).toBe(true);
  const verification = await request.post(`${apiURL}/api/auth/verify-email`, {
    headers: { 'X-CSRF-TOKEN': await csrf(request) },
    data: { token: await verificationToken(request, email) },
  });
  expect(verification.ok(), await verification.text()).toBe(true);
}

async function signIn(page: Page, email: string): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
}

async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: /Account menu/ }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in$/);
}

async function createOrganizationAndEnter(page: Page, organizationName: string): Promise<void> {
  await page.getByRole('button', { name: /Account menu/ }).click();
  await page.getByRole('button', { name: 'Create Organization' }).click();
  const createDialog = page.getByRole('dialog', { name: 'Create Organization' });
  await createDialog.getByRole('textbox', { name: 'Organization name' }).fill(organizationName);
  await createDialog.getByRole('button', { name: 'Create Organization' }).click();
  await page
    .getByRole('dialog', { name: 'Organization created' })
    .getByRole('button', { name: 'Enter Workspace' })
    .click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('button', { name: /Account menu/ })).toContainText(organizationName, {
    timeout: 30_000,
  });
}

async function inviteMember(page: Page, email: string): Promise<void> {
  await page.goto('/memberships');
  const table = page.getByRole('region', { name: 'Workspace invitation outcomes' });
  await table.getByRole('button', { name: 'Invite member' }).click();
  const invitation = page.getByRole('dialog', { name: 'Invite member' });
  await invitation.getByLabel('Recipient email').fill(email);
  await invitation.getByRole('button', { name: 'Invite member' }).click();
  await expect(invitation.getByText('Invitation outcome confirmed')).toBeVisible();
  await expect(table).toContainText(email);
}

async function openInvitationWithoutRetainingToken(page: Page, link: string): Promise<string> {
  const token = new URL(link).hash.slice('#token='.length);
  await page.goto(link);
  await expect(page).toHaveURL(/\/invitations\/accept$/);
  expect(page.url()).not.toContain('#');
  const retained = await page.evaluate(async (secret) => {
    const databases = typeof indexedDB.databases === 'function' ? await indexedDB.databases() : [];
    return {
      html: document.documentElement.innerHTML.includes(secret),
      local: Object.values(localStorage).some((value) => value.includes(secret)),
      session: Object.values(sessionStorage).some((value) => value.includes(secret)),
      indexedDatabaseCount: databases.length,
    };
  }, token);
  expect(retained).toEqual({ html: false, local: false, session: false, indexedDatabaseCount: 0 });
  return token;
}

async function expectReviewAndAccept(
  page: Page,
  organizationName: string,
  inviterName: string,
): Promise<void> {
  await expect(page.getByRole('heading', { name: 'Review Workspace invitation' })).toBeVisible();
  await expect(page.getByText(organizationName, { exact: true })).toHaveCount(2);
  await expect(page.getByText(inviterName, { exact: true })).toBeVisible();
  await expect(page.getByText('Workspace member', { exact: true })).toBeVisible();

  const sourceWorkspace = await activeWorkspaceId(page);
  await page.getByRole('button', { name: 'Accept invitation' }).click();
  await expect(page.getByRole('heading', { name: 'Invitation accepted' })).toBeVisible();
  expect(await activeWorkspaceId(page)).toBe(sourceWorkspace);

  await page.setViewportSize({ width: 375, height: 720 });
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1))
    .toBe(true);
  await page.getByRole('button', { name: 'Enter Workspace' }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  expect(await activeWorkspaceId(page)).not.toBe(sourceWorkspace);
}

async function activeWorkspaceId(page: Page): Promise<string> {
  const response = await page.request.get('/api/auth/session');
  expect(response.ok()).toBe(true);
  const session = (await response.json()) as BrowserSession;
  if (!session.user?.workspaceId) throw new Error('The browser session has no active Workspace.');
  return session.user.workspaceId;
}

test.describe('Workspace invitation governance', () => {
  test.describe.configure({ timeout: 90_000 });
  test.skip(!apiURL || !maildevURL, 'Set E2E_API_URL and E2E_MAILDEV_URL for invitation evidence.');

  test.beforeEach(async ({ request }) => {
    await request.delete(`${maildevURL}/email/all`);
  });

  test('AT-001 existing verified recipient accepts from a memory-only handoff', async ({
    page,
    request,
  }) => {
    const suffix = `${Date.now()}.${Math.random().toString(36).slice(2, 8)}`;
    const administratorEmail = `invite.admin.${suffix}@test.com`;
    const recipientEmail = `invite.existing.${suffix}@test.com`;
    const organizationName = `Invitation Operations ${suffix}`;
    await registerAndVerifyViaApi(request, recipientEmail, 'Existing Recipient');
    await registerAndVerifyViaApi(request, administratorEmail, 'Invitation Administrator');

    await signIn(page, administratorEmail);
    await createOrganizationAndEnter(page, organizationName);
    await inviteMember(page, recipientEmail);
    const link = await invitationLink(request, recipientEmail);
    await signOut(page);

    await openInvitationWithoutRetainingToken(page, link);
    await expect(
      page.getByRole('heading', { name: 'Continue with the invited account' }),
    ).toBeVisible();
    await page.getByRole('link', { name: 'Sign in' }).click();
    await page.getByLabel('Email address').fill(recipientEmail);
    await page.getByLabel('Password').fill(password);
    await page.getByRole('button', { name: /sign in/i }).click();

    await expectReviewAndAccept(page, organizationName, 'Invitation Administrator');
  });

  test('AT-002 new recipient registers, verifies, resumes, and accepts', async ({
    page,
    request,
  }) => {
    const suffix = `${Date.now()}.${Math.random().toString(36).slice(2, 8)}`;
    const administratorEmail = `invite.new-admin.${suffix}@test.com`;
    const recipientEmail = `invite.new-recipient.${suffix}@test.com`;
    const organizationName = `New Recipient Operations ${suffix}`;
    await registerAndVerifyViaApi(request, administratorEmail, 'Invitation Administrator');

    await signIn(page, administratorEmail);
    await createOrganizationAndEnter(page, organizationName);
    await inviteMember(page, recipientEmail);
    const link = await invitationLink(request, recipientEmail);
    await signOut(page);

    await openInvitationWithoutRetainingToken(page, link);
    await page.getByRole('link', { name: 'Create account' }).click();
    await page.getByLabel('Full name').fill('New Invitation Recipient');
    await page.getByLabel('Email address').fill(recipientEmail);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByLabel('Confirm password', { exact: true }).fill(password);
    await page.getByRole('checkbox', { name: /terms of service/i }).check();
    await page.getByRole('button', { name: /create account/i }).click();
    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();

    const token = await verificationToken(request, recipientEmail);
    await page.goto(`/auth/verify?token=${token}`);
    await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible({
      timeout: 30_000,
    });
    await page.getByRole('button', { name: 'Continue to dashboard' }).click();
    await expect(page.getByRole('heading', { name: 'Review Workspace invitation' })).toBeVisible({
      timeout: 30_000,
    });

    await expectReviewAndAccept(page, organizationName, 'Invitation Administrator');
  });
});
