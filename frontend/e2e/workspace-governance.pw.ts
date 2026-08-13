import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;
const maildevURL = process.env.E2E_MAILDEV_URL;
const password = 'maple river sunrise';
const personalTriggerLabel = 'Workspace Governance User';
const personalWorkspaceOption = 'Personal';

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

async function createVerifiedUser(
  request: APIRequestContext,
  email: string,
  fullName = 'Workspace Governance User',
): Promise<void> {
  const legalResponse = await request.get(`${apiURL}/api/legal/versions`);
  expect(legalResponse.ok()).toBe(true);
  const legal = (await legalResponse.json()) as LegalVersions;
  const registration = await request.post(`${apiURL}/api/users/register`, {
    headers: {
      'Idempotency-Key': `workspace-e2e-${crypto.randomUUID()}`,
      'X-CSRF-TOKEN': await bootstrapAntiforgery(request),
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
            candidate.to?.some(
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

async function signIn(page: Page, email: string): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
}

async function expectControlLabel(page: Page, label: string): Promise<void> {
  await expect(page.getByRole('button', { name: /Account menu/ })).toContainText(label, {
    timeout: 30_000,
  });
}

async function expectProductBuilderNavigation(page: Page): Promise<void> {
  await expect(page.getByRole('link', { name: 'Business objects', exact: true })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByRole('link', { name: 'Rules', exact: true })).toBeVisible({
    timeout: 30_000,
  });
}

async function expectNoProductBuilderNavigation(page: Page): Promise<void> {
  await expect(page.getByRole('link', { name: 'Business objects', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Rules', exact: true })).toHaveCount(0);
}

async function activeWorkspaceId(page: Page): Promise<string> {
  const response = await page.request.get('/api/auth/session');
  expect(response.ok()).toBe(true);
  const session = (await response.json()) as BrowserSession;
  if (!session.user?.workspaceId) throw new Error('The browser session has no active Workspace.');
  return session.user.workspaceId;
}

async function switchWorkspace(
  page: Page,
  workspaceName: string,
  expectedControlLabel = workspaceName,
): Promise<string> {
  await page.getByRole('button', { name: /Account menu/ }).click();
  await page.getByRole('button', { name: workspaceName, exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expectControlLabel(page, expectedControlLabel);
  return activeWorkspaceId(page);
}

async function createOrganizationAndEnter(page: Page, organizationName: string): Promise<string> {
  await page.getByRole('button', { name: /Account menu/ }).click();
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

async function inviteWorkspaceMember(page: Page, email: string): Promise<void> {
  await page.goto('/memberships');
  const table = page.getByRole('region', { name: 'Workspace invitation outcomes' });
  await table.getByRole('button', { name: 'Invite member' }).click();
  const dialog = page.getByRole('dialog', { name: 'Invite member' });
  await dialog.getByLabel('Recipient email').fill(email);
  await dialog.getByRole('button', { name: 'Invite member' }).click();
  await expect(dialog.getByText('Invitation outcome confirmed')).toBeVisible();
}

async function acceptWorkspaceInvitation(page: Page, link: string, email: string): Promise<void> {
  await page.goto(link);
  await expect(
    page.getByRole('heading', { name: 'Continue with the invited account' }),
  ).toBeVisible();
  await page.getByRole('link', { name: 'Sign in' }).click();
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page.getByRole('heading', { name: 'Review Workspace invitation' })).toBeVisible({
    timeout: 30_000,
  });
  await page.getByRole('button', { name: 'Accept invitation' }).click();
  await expect(page.getByRole('heading', { name: 'Invitation accepted' })).toBeVisible();
  await page.getByRole('button', { name: 'Enter Workspace' }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
}

async function openMemberAuthority(page: Page, memberName: string): Promise<void> {
  await page.goto('/memberships');
  await page.getByRole('tab', { name: 'Members' }).click();
  const table = page.getByRole('region', { name: 'Active Workspace member authoring authority' });
  await table.getByRole('button', { name: memberName }).click();
  await expect(page.getByRole('dialog', { name: memberName })).toBeVisible();
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
  test.describe.configure({ timeout: 120_000 });
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

    await expectControlLabel(page, personalTriggerLabel);
    await expectProductBuilderNavigation(page);
    const personalWorkspaceId = await activeWorkspaceId(page);
    const control = page.getByRole('button', { name: /Account menu/ });
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
    await expectProductBuilderNavigation(page);

    await control.click();
    await page.getByRole('button', { name: personalWorkspaceOption, exact: true }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expectControlLabel(page, personalTriggerLabel);
    await expectProductBuilderNavigation(page);

    await page.setViewportSize({ width: 375, height: 720 });
    await control.click();
    await expect(page.getByRole('region', { name: 'Workspace', exact: true })).toBeVisible();
    await expect(
      page.getByRole('button', { name: personalWorkspaceOption, exact: true }),
    ).toBeVisible();
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

    expect(await switchWorkspace(page, personalWorkspaceOption, personalTriggerLabel)).toBe(
      personalWorkspaceId,
    );
    await expectWorkspaceDefinitions(page, personalDefinitionName, organizationDefinitionName);

    const sourceSearchKey = personalDefinitionName;
    let releaseBegin!: () => void;
    const beginReleased = new Promise<void>((resolve) => {
      releaseBegin = resolve;
    });
    let beginCaptured!: () => void;
    const beginIsHeld = new Promise<void>((resolve) => {
      beginCaptured = resolve;
    });
    await page.route('**/api/workspace-context/begin', async (route) => {
      beginCaptured();
      await beginReleased;
      await route.continue();
    });

    let releaseHeldSourceResponse!: () => void;
    const heldSourceResponseReleased = new Promise<void>((resolve) => {
      releaseHeldSourceResponse = resolve;
    });
    let sourceResponseHeld!: () => void;
    const sourceResponseIsHeld = new Promise<void>((resolve) => {
      sourceResponseHeld = resolve;
    });
    let sourceResponseDelivered!: () => void;
    const sourceResponseIsDelivered = new Promise<void>((resolve) => {
      sourceResponseDelivered = resolve;
    });
    let sourceResponseCaptured = false;
    await page.route('**/api/business-object-definitions?*', async (route) => {
      if (sourceResponseCaptured) {
        await route.continue();
        return;
      }

      expect(new URL(route.request().url()).searchParams.get('query')).toBe(sourceSearchKey);
      sourceResponseCaptured = true;
      const sourceResponse = await route.fetch();
      const sourcePayload = (await sourceResponse.json()) as {
        items?: Array<{ name?: string }>;
      };
      expect(
        sourcePayload.items?.some((definition) => definition.name === personalDefinitionName),
      ).toBe(true);
      sourceResponseHeld();
      await heldSourceResponseReleased;
      await route.fulfill({ response: sourceResponse });
      sourceResponseDelivered();
    });

    await page.getByRole('button', { name: /Account menu/ }).click();
    await page.getByRole('button', { name: organizationName, exact: true }).click();
    await beginIsHeld;

    await page.getByLabel('Search business objects').fill(sourceSearchKey);
    await sourceResponseIsHeld;

    releaseBegin();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expectControlLabel(page, organizationName);
    expect(await activeWorkspaceId(page)).toBe(organizationWorkspaceId);
    releaseHeldSourceResponse();
    await sourceResponseIsDelivered;

    await page.goto(`/business-objects?page=1&query=${encodeURIComponent(sourceSearchKey)}`);
    await expect(page).toHaveURL(/\/business-objects\?page=1&query=/);
    const organizationDefinitions = page.getByLabel('Definitions');
    await expect(
      organizationDefinitions.getByText(organizationDefinitionName, { exact: true }),
    ).toHaveCount(0);
    await expect(
      organizationDefinitions.getByText(personalDefinitionName, { exact: true }),
    ).toHaveCount(0);
    await expect(page.getByLabel('Search business objects')).toHaveValue(sourceSearchKey);
    await page.unroute('**/api/business-object-definitions?*');
    await page.unroute('**/api/workspace-context/begin');

    expect(await switchWorkspace(page, personalWorkspaceOption, personalTriggerLabel)).toBe(
      personalWorkspaceId,
    );
    await expectWorkspaceDefinitions(page, personalDefinitionName, organizationDefinitionName);
  });

  test('AT-009 grants and revokes Product Builder navigation for an organization member', async ({
    baseURL,
    browser,
    page,
    request,
  }) => {
    const suffix = `${Date.now()}.${Math.random().toString(36).slice(2, 8)}`;
    const administratorEmail = `builder.admin.${suffix}@test.com`;
    const memberEmail = `builder.member.${suffix}@test.com`;
    const organizationName = `Builder Operations ${suffix}`;
    const memberName = 'Product Builder Member';
    await createVerifiedUser(request, memberEmail, memberName);
    await createVerifiedUser(request, administratorEmail, 'Product Builder Administrator');

    await signIn(page, administratorEmail);
    await createOrganizationAndEnter(page, organizationName);
    await inviteWorkspaceMember(page, memberEmail);
    const link = await invitationLink(request, memberEmail);

    const memberContext = await browser.newContext({ baseURL });
    const memberPage = await memberContext.newPage();
    try {
      await acceptWorkspaceInvitation(memberPage, link, memberEmail);
      await expectNoProductBuilderNavigation(memberPage);

      await openMemberAuthority(page, memberName);
      const authority = page.getByRole('dialog', { name: memberName });
      await authority.getByRole('button', { name: 'Grant Product Builder' }).click();
      await expect(authority.getByText('Product Builder granted')).toBeVisible();

      await memberPage.reload();
      await expectProductBuilderNavigation(memberPage);

      await authority.getByRole('button', { name: 'Revoke Product Builder' }).click();
      const confirmation = page.getByRole('alertdialog', { name: 'Revoke Product Builder?' });
      await confirmation.getByRole('button', { name: 'Revoke Product Builder' }).click();
      await expect(authority.getByText('Product Builder revoked')).toBeVisible();

      await memberPage.reload();
      await expectNoProductBuilderNavigation(memberPage);
    } finally {
      await memberContext.close();
    }
  });
});
