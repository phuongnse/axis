import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type BrowserContext, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { sparePort } from '../activate'
import { runAdmin } from '../admin-tool'
import { startAgentHost } from '../agent-host'
import { csrfHeaders } from '../csrf'
import { confirmDialog, waitForDialog } from '../dialogs'
import { startDirectoryServer } from '../directory-server'
import { submitCode, submitPassword } from '../keycloak'
import { platformSecretKey, required } from '../server-settings'
import { liveTotp } from '../totp'
import {
  agentHostSettings,
  issueTenantCertificate,
  addAuthoringAccount,
  keycloakAdmin,
  prepareAuthoringRealm,
  startTunnelProxy,
  tenantHost,
} from '../../../scripts/lib/ai-eval-stack.mjs'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak. The agent host starts
// later, and planning the request takes a moment more, so the spec needs the longer limit.
test.setTimeout(300_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'ask-ai-agent'
const host = tenantHost(tenant)
const application = 'AskAiAgent'
const request = 'Add a cost centre field to the request.'
const secondRequest = 'Add a supplier field to the request.'
// The scripted answers come in this order: the first request's plan, the second request's plan and its new plan.
const plan = 'Add a text field costCentre to the Request entity and show it on its form.'
const secondPlan = 'Add a required text field supplier to the Request entity.'
const newPlan = 'Add a list field supplier to the Request entity, with the suppliers the company buys from.'
const usage = { input_tokens: 1, output_tokens: 1 }

let folder: string
let trustDir: string
let answersFile: string
let author: { username: string; password: string; secret: string }
let colleague: { username: string; password: string; secret: string }
let server: Awaited<ReturnType<typeof startDirectoryServer>>
let proxy: Awaited<ReturnType<typeof startTunnelProxy>>
let browser: Browser
let agent: { stop(): Promise<void> } | undefined
// A TOTP code cannot be reused within its period, so the author signs in once and the tests share this context.
let authorContext: BrowserContext
// The address of the first request, which the later tests open again.
let firstRequestUrl: string

/** Signs `account` in on the tenant's host in `context` and leaves the page on the tenant's home. */
async function signIn(context: BrowserContext, account: { username: string; password: string; secret: string }) {
  const page = await context.newPage()
  await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
  await submitPassword(page, account.username, account.password)
  await submitCode(page, await liveTotp(account.secret))
  await page.waitForURL(`https://${host}/`)
  return page
}

/** Sends `text` from the Ask AI panel of the application's Studio and waits until the page of the new request opens. */
async function askAi(page: Page, text: string) {
  await page.goto(`https://${host}/studio?app=${application}`)
  await page.getByTestId('studio-ask-ai').click()
  await page.getByRole('textbox', { name: 'Your request', exact: true }).fill(text)
  await page.getByTestId('studio-ask-ai-send').click()
  await page.waitForURL(/view=requests&request=/)
}

test.beforeAll(async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  const admin = await keycloakAdmin({
    keycloakUrl: required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak'),
    adminSecret: required('AXIS_E2E_IDENTITY_ADMIN_SECRET', 'the secret of the axis-admin client'),
  })
  author = await prepareAuthoringRealm({
    admin,
    realm: tenant,
    redirectUris: [`https://${host}/*`],
    roles: ['author'],
    username: `author@${tenant}.test`,
  })
  colleague = await addAuthoringAccount({ admin, realm: tenant, roles: ['author'], username: `colleague@${tenant}.test` })

  // The spec's own certificate for `*.eval.localhost`: OpenSSL in the agent host refuses the run's `*.localhost` wildcard, see tenantDomain.
  folder = await mkdtemp(path.join(os.tmpdir(), 'axis-ask-ai-agent-'))
  const tls = issueTenantCertificate(folder)
  trustDir = tls.trustDir
  // The scripted answers are plans with no questions, shaped as Messages API responses.
  answersFile = path.join(folder, 'answers.json')
  await writeFile(
    answersFile,
    JSON.stringify(
      [plan, secondPlan, newPlan].map((text) => ({ content: [{ type: 'text', text }], stop_reason: 'end_turn', usage })),
    ),
  )

  // Offset 9, because the other specs that start a server use offsets 0 to 6 and 8, and offset 7 is the port of the control plane.
  const applications = path.join(folder, 'applications')
  await mkdir(applications)
  server = await startDirectoryServer(9, {
    Studio__AuthorRole: 'author',
    Studio__ApplicationsPath: applications,
    Ai__DefaultBinding: 'scripted',
    Kestrel__Certificates__Default__Path: path.join(tls.serverDir, 'server.crt'),
    Kestrel__Certificates__Default__KeyPath: path.join(tls.serverDir, 'server.key'),
  })
  proxy = await startTunnelProxy(server.httpsPort)
  browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
})

test.afterAll(async () => {
  await agent?.stop()
  await authorContext?.close()
  proxy?.close()
  await browser?.close()
  await server?.stop()
  if (folder) {
    await rm(folder, { recursive: true, force: true })
  }
})

test('an author sends a request from Ask AI, sees it in Planning and then in Awaiting approval with its plan', async () => {
  authorContext = await browser.newContext({ ignoreHTTPSErrors: true })
  const page = await signIn(authorContext, author)
  try {
    // The call runs in the page, because the browser maps the tenant host to the server and the request context does not.
    const made = await page.evaluate(
      async ({ headers, name }) => {
        const response = await fetch('/api/studio/apps', {
          method: 'POST',
          headers: { ...headers, 'Content-Type': 'application/json' },
          body: JSON.stringify({ name }),
        })
        return response.status
      },
      { headers: csrfHeaders, name: application },
    )
    expect(made).toBe(201)

    await askAi(page, request)
    firstRequestUrl = page.url()
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Planning')
    await expect(
      page.getByText('AI is reading the application and writing a plan. Nothing changes until the plan is approved.', {
        exact: true,
      }),
    ).toBeVisible()

    // The agent host starts only now. Running before the page's first read, it could plan the request before Planning shows.
    agent = await startAgentHost(
      agentHostSettings({
        healthUrl: `http://127.0.0.1:${await sparePort()}`,
        platform: required('AXIS_E2E_DATABASE', 'the PostgreSQL connection string of the runtime role'),
        secretKey: platformSecretKey,
        proxyUrl: proxy.url,
        trustDir,
        answersFile,
      }),
    )
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval', { timeout: 120_000 })
    await expect(page.getByText(plan, { exact: true })).toBeVisible()
    await expect(page.getByText('Plan ready for your approval', { exact: true })).toBeVisible()
  } finally {
    await page.close()
  }
})

test('another author sees the plan and whose approval it waits for, with no actions', async () => {
  const context = await browser.newContext({ ignoreHTTPSErrors: true })
  try {
    const page = await signIn(context, colleague)
    await page.goto(firstRequestUrl)
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval')
    await expect(page.getByText(plan, { exact: true })).toBeVisible()
    await expect(page.getByText("Waiting for its author's approval", { exact: true })).toBeVisible()
    for (const name of ['Approve plan', 'Change the plan…', 'Drop…']) {
      await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0)
    }
  } finally {
    await context.close()
  }
})

test('an author asks for another plan with a comment, sees the new plan, and drops the request', async () => {
  const page = await authorContext.newPage()
  try {
    await askAi(page, secondRequest)
    await expect(page.getByText(secondPlan, { exact: true })).toBeVisible({ timeout: 120_000 })
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval')

    await page.getByRole('button', { name: 'Change the plan…', exact: true }).click()
    const dialog = await waitForDialog(page, 'Change the plan')
    await dialog
      .getByRole('textbox', { name: 'What should the plan do differently?', exact: true })
      .fill('Let the author pick the supplier from a list.')
    await confirmDialog(page, 'Ask for a new plan')
    await expect(page.getByText(newPlan, { exact: true })).toBeVisible({ timeout: 120_000 })
    await expect(page.getByText(secondPlan, { exact: true })).toHaveCount(0)
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval')

    await page.getByRole('button', { name: 'Drop…', exact: true }).click()
    await waitForDialog(page, 'Drop this request?')
    await confirmDialog(page, 'Drop')
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Dropped')
    await expect(page.getByText('Dropped. Nothing changed', { exact: true })).toBeVisible()
  } finally {
    await page.close()
  }
})

test('an author approves the plan and the request is Working', async () => {
  // The agent host stops first: running, it would carry the approved request past Working.
  await agent?.stop()
  agent = undefined
  const page = await authorContext.newPage()
  try {
    await page.goto(firstRequestUrl)
    await page.getByRole('button', { name: 'Approve plan', exact: true }).click()
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Working')
    await expect(page.getByText('AI is working on it', { exact: true })).toBeVisible()
  } finally {
    await page.close()
  }
})
