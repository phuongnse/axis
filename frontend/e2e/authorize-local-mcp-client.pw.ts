import { createHash, randomBytes } from 'node:crypto';
import { createServer, type Server } from 'node:http';
import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;
const maildevURL = process.env.E2E_MAILDEV_URL;
const password = 'maple river sunrise';
const callbackPort = 48123;

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

interface TokenResponse {
  access_token?: string;
}

interface LoopbackCallbackListener {
  callback: Promise<URL>;
  close: () => Promise<void>;
}

function uniqueEmail(prefix: string): string {
  return `${prefix}.${Date.now()}.${Math.random().toString(36).slice(2, 8)}@test.com`;
}

function createPkceVerifier(): string {
  return randomBytes(32).toString('base64url');
}

function createPkceChallenge(verifier: string): string {
  return createHash('sha256').update(verifier).digest('base64url');
}

async function clearMaildev(request: APIRequestContext): Promise<void> {
  await request.delete(`${maildevURL}/email/all`);
}

async function bootstrapAntiforgery(request: APIRequestContext): Promise<string> {
  const sessionResponse = await request.get(`${apiURL}/api/auth/session`);
  expect(sessionResponse.ok()).toBe(true);
  const session = (await sessionResponse.json()) as BrowserSession;
  if (!session.csrfToken) throw new Error('The browser session did not return a CSRF token.');
  return session.csrfToken;
}

async function createVerifiedUser(request: APIRequestContext, email: string): Promise<void> {
  const legalVersionsResponse = await request.get(`${apiURL}/api/legal/versions`);
  expect(legalVersionsResponse.ok()).toBe(true);
  const legalVersions = (await legalVersionsResponse.json()) as LegalVersions;

  const registrationResponse = await request.post(`${apiURL}/api/users/register`, {
    headers: {
      'Idempotency-Key': `e2e-mcp-authorize-${crypto.randomUUID()}`,
      'X-CSRF-TOKEN': await bootstrapAntiforgery(request),
    },
    data: {
      fullName: 'Local MCP Client',
      email,
      password,
      passwordConfirmation: password,
      acceptedTermsVersion: legalVersions.termsVersion,
      acceptedPrivacyVersion: legalVersions.privacyVersion,
    },
  });
  expect(
    registrationResponse.ok(),
    `Registration failed with ${registrationResponse.status()}: ${await registrationResponse.text()}`,
  ).toBe(true);

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
        return message?.text?.match(/token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
      },
      { message: `verification email for ${email}`, timeout: 30_000 },
    )
    .not.toBe('');

  const messagesResponse = await request.get(`${maildevURL}/email`);
  const messages = (await messagesResponse.json()) as MaildevMessage[];
  const message = messages.find(
    (item) =>
      item.subject === 'Verify your email address' &&
      item.to?.some((recipient) => recipient.address.toLowerCase() === email.toLowerCase()),
  );
  const token = message?.text?.match(/token=([A-Za-z0-9_-]+)/)?.[1];
  if (!token) throw new Error(`Verification token was not found for ${email}.`);

  const verificationResponse = await request.post(`${apiURL}/api/auth/verify-email`, {
    headers: { 'X-CSRF-TOKEN': await bootstrapAntiforgery(request) },
    data: { token },
  });
  expect(verificationResponse.ok()).toBe(true);
}

function startLoopbackCallbackListener(): Promise<LoopbackCallbackListener> {
  return new Promise((resolve, reject) => {
    let server: Server;
    let resolveCallback: (url: URL) => void;
    let rejectCallback: (error: Error) => void;
    const callback = new Promise<URL>((resolveCallbackPromise, rejectCallbackPromise) => {
      resolveCallback = resolveCallbackPromise;
      rejectCallback = rejectCallbackPromise;
    });

    server = createServer((request, response) => {
      const callbackUrl = new URL(request.url ?? '/', `http://127.0.0.1:${callbackPort}`);
      if (callbackUrl.pathname !== '/callback') {
        response.writeHead(404).end();
        return;
      }

      response.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' }).end('Authorized');
      resolveCallback(callbackUrl);
    });
    server.once('error', (error) => {
      rejectCallback(error);
      reject(error);
    });
    server.listen(callbackPort, '127.0.0.1', () => {
      resolve({
        callback,
        close: () =>
          new Promise((resolveClose, rejectClose) => {
            server.close((error) => (error ? rejectClose(error) : resolveClose()));
          }),
      });
    });
  });
}

async function signIn(page: Page, email: string): Promise<void> {
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /^sign in$/i }).click();
}

test.describe('authorize local MCP client', () => {
  test.skip(
    !apiURL || !maildevURL,
    'Set E2E_API_URL and E2E_MAILDEV_URL for local MCP authorization.',
  );

  test('AT-003 completes browser PKCE authorization through the real loopback callback', async ({
    page,
    request,
  }, testInfo) => {
    const baseURL = testInfo.project.use.baseURL;
    if (typeof baseURL !== 'string') throw new Error('Playwright baseURL is required.');

    await clearMaildev(request);
    const email = uniqueEmail('mcp-authorize');
    await createVerifiedUser(request, email);

    const state = randomBytes(32).toString('base64url');
    const codeVerifier = createPkceVerifier();
    const listener = await startLoopbackCallbackListener();
    try {
      const authorizationUrl = new URL('/connect/authorize', baseURL);
      authorizationUrl.search = new URLSearchParams({
        response_type: 'code',
        client_id: 'axis_mcp',
        redirect_uri: `http://127.0.0.1:${callbackPort}/callback`,
        scope: 'openid email profile',
        code_challenge: createPkceChallenge(codeVerifier),
        code_challenge_method: 'S256',
        state,
      }).toString();

      await page.goto(authorizationUrl.toString());
      await expect(page).toHaveURL(/\/sign-in\?authorization_request=/);
      const signInUrl = new URL(page.url());
      expect(signInUrl.searchParams.get('authorization_request')).toBeTruthy();
      expect(signInUrl.searchParams.get('authorization_client')).toBe('axis_mcp');
      await signIn(page, email);

      const callbackUrl = await listener.callback;
      expect(callbackUrl.searchParams.get('state')).toBe(state);
      const code = callbackUrl.searchParams.get('code');
      expect(code).toBeTruthy();

      const tokenResponse = await fetch(new URL('/connect/token', baseURL), {
        method: 'POST',
        headers: { 'content-type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type: 'authorization_code',
          client_id: 'axis_mcp',
          redirect_uri: `http://127.0.0.1:${callbackPort}/callback`,
          code_verifier: codeVerifier,
          code: code ?? '',
        }),
      });
      expect(tokenResponse.ok).toBe(true);
      const token = (await tokenResponse.json()) as TokenResponse;
      expect(token.access_token).toBeTruthy();

      const meResponse = await fetch(new URL('/api/users/me', baseURL), {
        headers: { authorization: `Bearer ${token.access_token}` },
      });
      expect(meResponse.ok).toBe(true);
      expect(await meResponse.json()).toMatchObject({ email });
    } finally {
      await listener.close();
    }
  });
});
