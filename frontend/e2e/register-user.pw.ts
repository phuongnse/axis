import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;
const maildevURL = process.env.E2E_MAILDEV_URL;
const expectedVerificationOrigin = process.env.E2E_VERIFY_ORIGIN;
const password = 'maple river sunrise';

interface LegalVersions {
  termsVersion: string;
  privacyVersion: string;
}

interface MaildevRecipient {
  address: string;
  name?: string;
}

interface MaildevMessage {
  from?: MaildevRecipient[] | MaildevRecipient;
  html?: string;
  subject?: string;
  text?: string;
  to?: MaildevRecipient[] | MaildevRecipient;
}

interface VerificationEmailLink {
  token: string;
  url: URL;
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
      fullName: 'Duplicate User',
      email,
      password,
      passwordConfirmation: password,
      acceptedTermsVersion: legalVersions.termsVersion,
      acceptedPrivacyVersion: legalVersions.privacyVersion,
    },
  });
  expect(response.ok()).toBe(true);
}

function verificationLinkFrom(message?: MaildevMessage): VerificationEmailLink | null {
  const match = message?.text?.match(/(https?:\/\/\S+\/auth\/verify\?token=([A-Za-z0-9_-]+))/);
  if (!match) return null;

  return {
    token: match[2],
    url: new URL(match[1]),
  };
}

async function findVerificationMessage(
  request: APIRequestContext,
  email: string,
  subject: string,
): Promise<MaildevMessage | undefined> {
  const response = await request.get(`${maildevURL}/email`);
  if (!response.ok()) return undefined;

  const messages = (await response.json()) as MaildevMessage[];
  return messages.find(
    (item) =>
      item.subject === subject &&
      maildevAddresses(item.to).some(
        (recipient) => recipient.address.toLowerCase() === email.toLowerCase(),
      ),
  );
}

function maildevAddresses(
  addresses: MaildevRecipient[] | MaildevRecipient | undefined,
): MaildevRecipient[] {
  if (!addresses) return [];
  return Array.isArray(addresses) ? addresses : [addresses];
}

async function waitForVerificationMessage(
  request: APIRequestContext,
  email: string,
  subject = 'Verify your email address',
): Promise<MaildevMessage> {
  await expect
    .poll(
      async () => {
        const message = await findVerificationMessage(request, email, subject);
        return verificationLinkFrom(message)?.url.toString() ?? '';
      },
      {
        message: `verification email for ${email}`,
        timeout: 30_000,
      },
    )
    .not.toBe('');

  const message = await findVerificationMessage(request, email, subject);
  if (!message) {
    throw new Error(`Verification email was not found for ${email}.`);
  }
  return message;
}

async function waitForVerificationLink(
  request: APIRequestContext,
  email: string,
  subject = 'Verify your email address',
): Promise<VerificationEmailLink> {
  const message = await waitForVerificationMessage(request, email, subject);
  const link = verificationLinkFrom(message);
  if (!link) {
    throw new Error(`Verification link was not found for ${email}.`);
  }
  if (expectedVerificationOrigin) {
    expect(link.url.origin).toBe(expectedVerificationOrigin);
  }
  return link;
}

async function fillRegisterForm(page: Page, email: string): Promise<void> {
  await page.getByLabel('Full name').fill('Alex Rivers');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByLabel('Confirm password', { exact: true }).fill(password);
  await page.getByRole('checkbox', { name: /terms of service/i }).check();
}

async function fillVietnameseRegisterForm(page: Page, email: string): Promise<void> {
  await page.getByLabel('Họ và tên').fill('Alex Rivers');
  await page.getByLabel('Địa chỉ email').fill(email);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(password);
  await page.getByLabel('Xác nhận mật khẩu', { exact: true }).fill(password);
  await page.getByRole('checkbox', { name: /điều khoản dịch vụ/i }).check();
}

async function expectAuthenticatedFrame(page: Page, userName: string): Promise<void> {
  await expect(page.getByRole('banner')).toContainText('Dashboard');
  await expect(page.getByRole('navigation')).toHaveCount(0);
  await expect(page.getByRole('main')).toHaveText('');
  await page.getByRole('button', { name: 'Account menu' }).click();
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

test.describe('register user', () => {
  test.skip(!apiURL, 'Set E2E_API_URL to run register-user API setup.');

  test.beforeEach(async ({ request }) => {
    await clearMaildev(request);
  });

  test('AT-001 user registers, opens verification email, and reaches dashboard', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run register-user email verification.');

    const email = uniqueEmail('reg001');
    const languageWrites = watchLanguagePreferenceWrites(page);
    const themeWrites = watchThemePreferenceWrites(page);
    const mainFramePaths: string[] = [];
    page.on('framenavigated', (frame) => {
      if (frame !== page.mainFrame()) return;
      try {
        mainFramePaths.push(new URL(frame.url()).pathname);
      } catch {
        // Ignore initial browser URLs that are not valid absolute URLs.
      }
    });

    await page.goto('/register');
    await fillRegisterForm(page, email);
    await page.getByRole('button', { name: /create account/i }).click();

    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(page.getByText(`Sent to ${email}`)).toBeVisible();

    const verificationLink = await waitForVerificationLink(request, email);
    await page.goto(`/auth/verify?token=${verificationLink.token}`);

    await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible();
    await expect(
      page.getByText(
        "Your email is verified and your account is ready. Continue now, or we'll take you to the dashboard in a few seconds.",
      ),
    ).toBeVisible();
    await expect(page.getByRole('button', { name: 'Continue to dashboard' })).toBeVisible();
    await page.waitForTimeout(2_500);
    await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible();
    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    expect(mainFramePaths).not.toContain('/callback');
    await expectAuthenticatedFrame(page, 'Alex Rivers');
    expect(languageWrites()).toBe(0);
    expect(themeWrites()).toBe(0);
  });

  test('AT-011 selected language is persisted and used for verification email', async ({
    page,
    request,
  }) => {
    test.skip(!maildevURL, 'Set E2E_MAILDEV_URL to run register-user email verification.');

    const email = uniqueEmail('reg011');
    const languageWrites = watchLanguagePreferenceWrites(page);

    await page.goto('/register');
    await page.getByRole('button', { name: 'Preferences' }).click();
    await page.getByRole('button', { name: 'Vietnamese' }).click();

    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await fillVietnameseRegisterForm(page, email);
    await page.getByRole('button', { name: /tạo tài khoản/i }).click();

    await expect(page.getByRole('heading', { name: 'Kiểm tra email của bạn' })).toBeVisible();
    await expect(page.getByText(`Đã gửi đến ${email}`)).toBeVisible();

    const message = await waitForVerificationMessage(request, email, 'Xác minh email của bạn');
    const from = maildevAddresses(message.from)[0];

    expect(from?.address).toBe('noreply@axis.localhost');
    expect(from?.name).toBe('Axis Platform');
    expect(message.text).toContain('Chào mừng bạn đến với Axis Platform.');
    expect(message.text).toContain('Liên kết này hết hạn sau 24 giờ.');
    expect(message.html ?? '').toContain('data-template="axis-transactional-email"');
    expect(message.html ?? '').toContain('/axis-logo.svg');
    expect(message.html ?? '').toContain('letter-spacing:0.18em');
    expect(message.html ?? '').toContain('background:#c75f1e');

    const verificationLink = verificationLinkFrom(message);
    if (!verificationLink) {
      throw new Error(`Verification link was not found for ${email}.`);
    }
    if (expectedVerificationOrigin) {
      expect(verificationLink.url.origin).toBe(expectedVerificationOrigin);
    }

    await page.goto(`/auth/verify?token=${verificationLink.token}`);
    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });

    await page.evaluate(() => localStorage.removeItem('axis.language'));
    await page.reload();

    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 30_000 });
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi', { timeout: 30_000 });
    await expect(page.getByRole('button', { name: 'Menu tài khoản' })).toBeVisible();
    expect(languageWrites()).toBe(0);
  });

  test('AT-002 duplicate email shows an inline error', async ({ page, request }) => {
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
