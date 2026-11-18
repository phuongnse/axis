import { randomUUID } from 'node:crypto'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type BrowserContext, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { sparePort } from '../activate'
import { runAdmin, startPublisher } from '../admin-tool'
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
const failingRequest = 'Add a cost centre that refers to a department.'
const failingPlan = 'Add a CostCentre entity with an owner that refers to the Department entity.'
// The second application comes from the approvals template, which pins the organization module.
const approvalsApplication = 'AskAiApprovals'
const layerRequest = 'Add a cost centre field to the department.'
const layerPlan = 'Add a text field costCentre to the Department entity of the organization module.'
const usage = { input_tokens: 1, output_tokens: 1 }

let folder: string
let trustDir: string
let answersFile: string
let author: { username: string; password: string; secret: string }
let colleague: { username: string; password: string; secret: string }
let server: Awaited<ReturnType<typeof startDirectoryServer>>
let proxy: Awaited<ReturnType<typeof startTunnelProxy>>
let browser: Browser
let agent: Awaited<ReturnType<typeof startAgentHost>> | undefined
// The folder of the server's applications, which the spec's own publisher serves too.
let applications: string
// The publisher that the layer test runs for its checks, see there.
let publisher: Awaited<ReturnType<typeof startPublisher>> | undefined
// A TOTP code cannot be reused within its period, so the author signs in once and the tests share this context.
let authorContext: BrowserContext
// The address of the first request, which the later tests open again.
let firstRequestUrl: string
// The address of the layer test's ready request. The Accept tests that follow it open it.
let readyRequestUrl: string

/** Signs `account` in on the tenant's host in `context` and leaves the page on the tenant's home. */
async function signIn(context: BrowserContext, account: { username: string; password: string; secret: string }) {
  const page = await context.newPage()
  await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
  await submitPassword(page, account.username, account.password)
  await submitCode(page, await liveTotp(account.secret))
  await page.waitForURL(`https://${host}/`)
  return page
}

/** Sends `text` from the Ask AI panel of the Studio of `app` and waits until the page of the new request opens. */
async function askAi(page: Page, text: string, app = application) {
  await page.goto(`https://${host}/studio?app=${app}`)
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
  applications = path.join(folder, 'applications')
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
  await publisher?.stop()
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

test('a request whose compile check fails in every fix round ends Failed, with why it failed and its draft\'s diff', async () => {
  // The first request's agent host is stopped, and the request is dropped, so the new host claims only this request.
  const dropping = await authorContext.newPage()
  try {
    await dropping.goto(firstRequestUrl)
    await dropping.getByRole('button', { name: 'Drop…', exact: true }).click()
    await waitForDialog(dropping, 'Drop this request?')
    await confirmDialog(dropping, 'Drop')
    await expect(dropping.getByTestId('studio-ai-request-state')).toHaveText('Dropped')
  } finally {
    await dropping.close()
  }

  // The plan, then the work writes an entity that refers to a department the application lacks, then its end. Each fix round
  // ends with no change, so the compile fails again. The one fix round stops the request as Failed.
  const failingAnswers = path.join(folder, 'answers-failing.json')
  const failingEntity = {
    id: randomUUID(),
    kind: 'entity',
    name: 'CostCentre',
    formatVersion: 1,
    fields: [{ name: 'owner', type: 'reference', target: 'Department' }],
  }
  await writeFile(
    failingAnswers,
    JSON.stringify([
      { content: [{ type: 'text', text: failingPlan }], stop_reason: 'end_turn', usage },
      {
        content: [
          {
            type: 'tool_use',
            id: 'entity-1',
            name: 'write_resource',
            input: { application, content: failingEntity },
          },
        ],
        stop_reason: 'tool_use',
        usage,
      },
      { content: [{ type: 'text', text: 'Done.' }], stop_reason: 'end_turn', usage },
      { content: [{ type: 'text', text: 'Nothing to change.' }], stop_reason: 'end_turn', usage },
    ]),
  )
  agent = await startAgentHost({
    ...agentHostSettings({
      healthUrl: `http://127.0.0.1:${await sparePort()}`,
      platform: required('AXIS_E2E_DATABASE', 'the PostgreSQL connection string of the runtime role'),
      secretKey: platformSecretKey,
      proxyUrl: proxy.url,
      trustDir,
      answersFile: failingAnswers,
    }),
    Ai__FixRounds: '1',
  })

  const page = await authorContext.newPage()
  try {
    await askAi(page, failingRequest)
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval', { timeout: 120_000 })
    await expect(page.getByText(failingPlan, { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Approve plan', exact: true }).click()

    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Failed', { timeout: 120_000 })
    const alert = page.getByTestId('studio-ai-request-failure')
    await expect(alert).toContainText('AI could not finish this request.')
    await expect(alert).toContainText('The checks still failed after the last fix round.')

    const checks = page.getByTestId('studio-ai-request-checks')
    await expect(checks).toContainText('Failed')
    await expect(checks).toContainText('It does not compile')
    await expect(page.getByTestId('studio-ai-request-review')).toHaveCount(0)

    // The draft's diff lists the entity the work wrote, folded until its author asks for the diff.
    const written = page.getByTestId('studio-changes-file').filter({ hasText: 'CostCentre' })
    await expect(written).toHaveCount(1)
    await expect(written.getByTestId('json-diff-after')).toHaveCount(0)
    await written.getByRole('button', { name: 'Show the diff of CostCentre', exact: true }).click()
    await expect(written.getByTestId('json-diff-after')).toContainText('"CostCentre"')
  } finally {
    await page.close()
  }
})

// The layer test runs its own publisher, because the check's scenarios run as `test` publish requests, which only a
// publisher that reads the tenant directory serves. The run's shared publisher reads the configured tenants. That
// publisher claims the queued `platform.jobs` and publish requests of every directory tenant, whatever their kind. With
// no cell settings, it would fail a cell spec's `create`, `purge` and `export` jobs, and a cell spec's publisher would
// claim this request's `test` request and look for the application under its own folder, where it is not. So
// `startPublisher` holds a lock for each publisher's whole life: this one holds it alone, the cell specs' publishers share
// it, and each side waits for the other. This publisher runs only from the approval until the request reads Ready.
test("a request whose work adds a field to a module entity's layer reaches Ready with its checks passed and the layer marked in its diff", async () => {
  // The scenarios of the module's contract run in the check, and a cell spec's publisher may hold the lock for a while.
  test.setTimeout(600_000)
  // The first request's host would plan this request with the wrong answers.
  await agent?.stop()
  agent = undefined

  // The module's own Department, with a text field added: the layer holds the difference to it.
  const department = JSON.parse(
    await readFile(
      path.resolve(import.meta.dirname, '..', '..', '..', 'modules', 'organization', 'entities', 'department.json'),
      'utf8',
    ),
  )
  const layerAnswers = path.join(folder, 'answers-layer.json')
  await writeFile(
    layerAnswers,
    JSON.stringify([
      {
        content: [{ type: 'text', text: layerPlan }],
        stop_reason: 'end_turn',
        usage,
      },
      {
        content: [
          {
            type: 'tool_use',
            id: 'layer-1',
            name: 'write_layer',
            input: {
              application: approvalsApplication,
              module: 'organization',
              id: department.id,
              content: {
                ...department,
                fields: [...department.fields, { name: 'costCentre', type: 'text' }],
              },
            },
          },
        ],
        stop_reason: 'tool_use',
        usage,
      },
      {
        content: [{ type: 'text', text: 'Done.' }],
        stop_reason: 'end_turn',
        usage,
      },
      {
        content: [{ type: 'text', text: 'PASSED\nThe draft does the request.' }],
        stop_reason: 'end_turn',
        usage,
      },
    ]),
  )
  agent = await startAgentHost(
    agentHostSettings({
      healthUrl: `http://127.0.0.1:${await sparePort()}`,
      platform: required('AXIS_E2E_DATABASE', 'the PostgreSQL connection string of the runtime role'),
      secretKey: platformSecretKey,
      proxyUrl: proxy.url,
      trustDir,
      answersFile: layerAnswers,
    }),
  )

  const page = await authorContext.newPage()
  try {
    // The call runs in a page of the tenant's host, as the first test's does.
    await page.goto(`https://${host}/`)
    const made = await page.evaluate(
      async ({ headers, name, template }) => {
        const response = await fetch('/api/studio/apps', {
          method: 'POST',
          headers: { ...headers, 'Content-Type': 'application/json' },
          body: JSON.stringify({ name, template }),
        })
        return response.status
      },
      {
        headers: csrfHeaders,
        name: approvalsApplication,
        template: 'approvals',
      },
    )
    expect(made).toBe(201)

    await askAi(page, layerRequest, approvalsApplication)
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval', { timeout: 120_000 })
    await expect(page.getByText(layerPlan, { exact: true })).toBeVisible()

    publisher = await startPublisher(
      {
        Studio__ApplicationsPath: applications,
        Studio__AuthorRole: 'author',
        Scenarios__ServerConnectionString: required(
          'AXIS_E2E_SCENARIO_DATABASE',
          'the PostgreSQL connection string of the scenario role',
        ),
      },
      { alone: true },
    )
    try {
      await page.getByRole('button', { name: 'Approve plan', exact: true }).click()
      try {
        await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Ready', { timeout: 300_000 })
      } catch (error) {
        // The cause is in what the host and the publisher printed and in the request's steps.
        const steps = await page.evaluate(
          async (url) => (await fetch(url)).text(),
          `/api/studio/apps/${approvalsApplication}/authoring-requests/${new URL(page.url()).searchParams.get('request')}`,
        )
        await test.info().attach('agent-host-output', {
          body: agent?.output() ?? '',
          contentType: 'text/plain',
        })
        await test.info().attach('publisher-output', {
          body: publisher.output(),
          contentType: 'text/plain',
        })
        await test.info().attach('request', { body: steps, contentType: 'application/json' })
        throw error
      }
    } finally {
      await publisher.stop()
      publisher = undefined
    }

    await expect(page.getByTestId('studio-ai-request-checks')).toContainText('Passed')
    // The diff lists the module's Department once, with the field the layer added marked.
    const changed = page.getByTestId('studio-changes-file').filter({ hasText: 'Department' })
    await expect(changed).toHaveCount(1)
    await expect(
      changed.getByRole('note', {
        name: 'costCentre, added by this application',
        exact: true,
      }),
    ).toHaveText('Added')

    // The Accept tests that follow rely on this request, which stays Ready and belongs to the author.
    readyRequestUrl = page.url()
  } finally {
    await page.close()
  }
})
