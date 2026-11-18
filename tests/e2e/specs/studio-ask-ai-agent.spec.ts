import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser } from '@playwright/test'
import { expect, test } from '../test'
import { sparePort } from '../activate'
import { runAdmin } from '../admin-tool'
import { startAgentHost } from '../agent-host'
import { csrfHeaders } from '../csrf'
import { startDirectoryServer } from '../directory-server'
import { submitCode, submitPassword } from '../keycloak'
import { platformSecretKey, required } from '../server-settings'
import { liveTotp } from '../totp'
import {
  agentHostSettings,
  issueTenantCertificate,
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
const plan = 'Add a cost centre field to the request.'
const usage = { input_tokens: 1, output_tokens: 1 }

let folder: string
let trustDir: string
let answersFile: string
let author: { username: string; password: string; secret: string }
let server: Awaited<ReturnType<typeof startDirectoryServer>>
let proxy: Awaited<ReturnType<typeof startTunnelProxy>>
let browser: Browser
let agent: { stop(): Promise<void> } | undefined

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

  // The spec's own certificate for `*.eval.localhost`: OpenSSL in the agent host refuses the run's `*.localhost` wildcard, see tenantDomain.
  folder = await mkdtemp(path.join(os.tmpdir(), 'axis-ask-ai-agent-'))
  const tls = issueTenantCertificate(folder)
  trustDir = tls.trustDir
  // The scripted answer is a plan with no questions, shaped as a Messages API response.
  answersFile = path.join(folder, 'answers.json')
  await writeFile(
    answersFile,
    JSON.stringify([{ content: [{ type: 'text', text: plan }], stop_reason: 'end_turn', usage }]),
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
  proxy?.close()
  await browser?.close()
  await server?.stop()
  if (folder) {
    await rm(folder, { recursive: true, force: true })
  }
})

test('an author sends a request from Ask AI, sees it in Planning and then in Awaiting approval', async () => {
  const context = await browser.newContext({ ignoreHTTPSErrors: true })
  try {
    const page = await context.newPage()
    await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
    await submitPassword(page, author.username, author.password)
    await submitCode(page, await liveTotp(author.secret))
    await page.waitForURL(`https://${host}/`)

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

    await page.goto(`https://${host}/studio?app=${application}`)
    await page.getByTestId('studio-ask-ai').click()
    await page.getByRole('textbox', { name: 'Your request', exact: true }).fill(request)
    await page.getByTestId('studio-ask-ai-send').click()
    await page.waitForURL(/view=requests&request=/)
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Planning')

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
  } finally {
    await context.close()
  }
})
