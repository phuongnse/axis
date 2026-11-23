import { createHash, randomBytes, randomUUID } from 'node:crypto'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type BrowserContext, type Locator, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { sparePort } from '../activate'
import { runAdmin } from '../admin-tool'
import { startAgentHost } from '../agent-host'
import { askAi, holdAiSpecLock, planAnswer, signIn, startSpecPublisher, type AuthoringAccount } from '../ai-authoring'
import { csrfHeaders } from '../csrf'
import { startDirectoryServer } from '../directory-server'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { platformSecretKey, required } from '../server-settings'
import { chooseTheme } from '../shell'
import { confirmPublish } from '../studio-resources'
import { setRealmPassword } from '../tenant-realm'
import { liveTotp } from '../totp'
import {
  addAuthoringAccount,
  agentHostSettings,
  issueTenantCertificate,
  keycloakAdmin,
  mapPlatformRoles,
  prepareAuthoringRealm,
  startTunnelProxy,
  tenantHost,
} from '../../../scripts/lib/ai-eval-stack.mjs'

// The AI authoring journey: an author asks AI for a director's approval above a threshold, a colleague's change meets the
// author's accept, and the author accepts and publishes. A next request lets a requester decide their own request, and
// the approvals contract refuses it. The last test is an outside MCP client: it registers once the tenant administrator
// allows all clients, and a developer's change through it passes its checks but cannot be accepted. See the AI authoring
// journey spec in docs/testing.md.
//
// The tenant is served on the port offset 10, because offsets 0 to 6, 8 and 9 are taken, and offset 7 is the port of the
// control plane. The agent host and the publisher are the same as the Ask AI agent spec's, see the comments there. The two
// AI authoring specs hold one lock for their whole run, so they never run at once.

// The publisher runs for the checks of the request and for its accept and publish, which take minutes.
test.setTimeout(900_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'ai-journey'
const host = tenantHost(tenant)
const application = 'AiJourney'
const request = 'Add a director approval for requests above 1000.'
const plan =
  'Add the approver kind director to the approval steps, with its labels and a scenario: a request above 1000 needs the line manager and then the director.'
// The application's text key of the director's label. It is not under the module's `approvals.` prefix, see docs/testing.md.
const directorKey = 'approvalStep.approver.director'
const scenarioName = 'DirectorApprovesLargeRequests'
const refusedRequest = 'Let a requester decide their own request when they are its approver.'
const refusedPlan =
  'Change the route step, so a request goes to its approver even when that approver is the person who asked for it.'
// The module's contract scenario that the refused request fails in the checks.
const refusedScenario = 'RequesterCannotDecideOwnRequest'
// The outside client: its registration, the approver kind it adds and the scenario of that kind.
const clientName = 'Journey helper'
const clientRedirect = 'https://ai-journey.test/callback'
const generalDirectorKey = 'approvalStep.approver.generalDirector'
const clientScenarioName = 'GeneralDirectorApprovesLargeRequests'
const usage = { input_tokens: 1, output_tokens: 1 }

interface Resource {
  id: string
  kind: string
  name: string | null
}

interface ApprovalStep {
  id: string
  fields: { name: string; values?: string[]; valueLabels?: Record<string, unknown> }[]
}

interface Process {
  id: string
  steps: ({ name: string; branches?: { name: string; when: string }[] } & Record<string, unknown>)[]
}

let folder: string
let trustDir: string
let author: AuthoringAccount
let colleague: AuthoringAccount
// The developer who signs in through the outside client, an account of its own so that no other account's TOTP code is reused.
let developer: AuthoringAccount
let server: Awaited<ReturnType<typeof startDirectoryServer>>
let proxy: Awaited<ReturnType<typeof startTunnelProxy>>
let browser: Browser
let agent: Awaited<ReturnType<typeof startAgentHost>> | undefined
// The folder of the server's applications, which the spec's own publisher serves too.
let applications: string
// The publisher of the test that runs it, see there.
let publisher: Awaited<ReturnType<typeof startSpecPublisher>> | undefined
// A TOTP code cannot be reused within its period, so the author signs in once and the tests share this context.
let authorContext: BrowserContext
// The lock of the AI authoring specs, held for the whole run, see holdAiSpecLock.
let aiLock: { release(): Promise<void> } | undefined
// The address of the ready request. The tests after the first one open it again.
let readyRequestUrl: string
// The id of the approvals application's employee role. The draft reads it, and the colleague changes it.
let employeeRole: string

test.beforeAll(async () => {
  // The other AI authoring spec may hold the lock for its whole run, see holdAiSpecLock. Waiting for it takes this long.
  test.setTimeout(1_800_000)
  aiLock = await holdAiSpecLock()
  // The first administrator changes the tenant's Connected apps in the last test.
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  const admin = await keycloakAdmin({
    keycloakUrl: required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak'),
    adminSecret: required('AXIS_E2E_IDENTITY_ADMIN_SECRET', 'the secret of the axis-admin client'),
  })
  // The author publishes, so the author has the publisher role too, see Studio__PublisherRole below.
  author = await prepareAuthoringRealm({
    admin,
    realm: tenant,
    redirectUris: [`https://${host}/*`],
    roles: ['author', 'publisher'],
    username: `author@${tenant}.test`,
  })
  colleague = await addAuthoringAccount({ admin, realm: tenant, roles: ['author'], username: `colleague@${tenant}.test` })
  developer = await addAuthoringAccount({ admin, realm: tenant, roles: ['author'], username: `developer@${tenant}.test` })

  // The spec's own certificate for `*.eval.localhost`: OpenSSL in the agent host refuses the run's `*.localhost` wildcard, see tenantDomain.
  folder = await mkdtemp(path.join(os.tmpdir(), 'axis-ai-journey-'))
  const tls = issueTenantCertificate(folder)
  trustDir = tls.trustDir

  applications = path.join(folder, 'applications')
  await mkdir(applications)
  server = await startDirectoryServer(10, {
    Studio__AuthorRole: 'author',
    Studio__PublisherRole: 'publisher',
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
  await aiLock?.release()
})

/** Reads `url` in `page`, with the page's session, and answers its JSON. */
async function readJson<T>(page: Page, url: string): Promise<T> {
  return (await page.evaluate(async (target) => (await fetch(target)).json(), url)) as T
}

/** Reads `url` in `page`, with the page's session, and answers its text. */
function readText(page: Page, url: string): Promise<string> {
  return page.evaluate(async (target) => (await fetch(target)).text(), url)
}

/** The content of a texts resource with the text `key` set to `label`. */
function withText(content: Record<string, unknown>, key: string, label: string) {
  return { ...content, texts: { ...(content.texts as Record<string, string>), [key]: label } }
}

/**
 * A scenario for the approvals application: a request type with the line manager as its first step and the approver kind
 * `approver` as its second step, which a request from `minAmount` on needs, and which `dan` decides. The request of the
 * amount `amounts.large` goes to the line manager and then to that step, and the one of `amounts.small` to the manager alone.
 */
function largeRequestScenario(name: string, approver: string, minAmount: number, amounts: { large: number; small: number }) {
  return {
    id: randomUUID(),
    kind: 'scenario',
    name,
    formatVersion: 1,
    now: '2026-11-02T09:00:00Z',
    users: { admin: ['admin'], ann: ['employee'], mia: ['employee'], dan: ['employee'] },
    steps: [
      {
        type: 'createRecord',
        entity: 'organization/Employee',
        as: 'admin',
        save: 'mia',
        values: { fullName: 'Mia Tran', workEmail: 'mia@example.com', user: 'mia' },
      },
      {
        type: 'createRecord',
        entity: 'organization/Employee',
        as: 'admin',
        save: 'dan',
        values: { fullName: 'Dan Pham', workEmail: 'dan@example.com', user: 'dan' },
      },
      {
        type: 'createRecord',
        entity: 'organization/Employee',
        as: 'admin',
        save: 'annEmployee',
        values: { fullName: 'Ann Nguyen', workEmail: 'ann@example.com', user: 'ann', manager: 'mia' },
      },
      {
        type: 'createRecord',
        entity: 'approvals/RequestType',
        as: 'admin',
        save: 'purchase',
        values: {
          name: 'Purchase',
          code: 'PURCHASE',
          steps: [
            { sequence: 1, approver: 'lineManager' },
            { sequence: 2, approver, employee: 'dan', minAmount },
          ],
        },
      },
      {
        type: 'createRecord',
        entity: 'approvals/Request',
        as: 'ann',
        save: 'large',
        values: { type: 'purchase', title: 'Monitor', amount: amounts.large },
      },
      { type: 'startProcess', process: 'approvals/RequestApproval', record: 'large', as: 'ann' },
      { type: 'expectTask', record: 'large', step: 'decide', assignee: { user: 'mia' } },
      { type: 'completeTask', record: 'large', step: 'decide', outcome: 'approve', as: 'mia' },
      { type: 'expectTask', record: 'large', step: 'decide', assignee: { user: 'dan' } },
      { type: 'completeTask', record: 'large', step: 'decide', outcome: 'approve', as: 'dan' },
      { type: 'expectInstance', process: 'approvals/RequestApproval', record: 'large', state: 'completed' },
      {
        type: 'expectRecord',
        record: 'large',
        values: { status: 'approved', stepsRequired: 2, step: 2 },
      },
      {
        type: 'createRecord',
        entity: 'approvals/Request',
        as: 'ann',
        save: 'small',
        values: { type: 'purchase', title: 'Keyboard', amount: amounts.small },
      },
      { type: 'startProcess', process: 'approvals/RequestApproval', record: 'small', as: 'ann' },
      { type: 'expectTask', record: 'small', step: 'decide', assignee: { user: 'mia' } },
      { type: 'completeTask', record: 'small', step: 'decide', outcome: 'approve', as: 'mia' },
      { type: 'expectInstance', process: 'approvals/RequestApproval', record: 'small', state: 'completed' },
      {
        type: 'expectRecord',
        record: 'small',
        values: { status: 'approved', stepsRequired: 1, step: 1 },
      },
    ],
  }
}

/**
 * Expects the ready request's checks to pass, and its diff to hold the layer on ApprovalStep and the scenario. The diff
 * marks whole fields of a keyed list, not the values of an enum, so the layer marks `approver` Changed, and `director` is
 * found in that field's opened diff. `scope` is the page, or the root of the dark theme.
 */
async function expectReadyDiff(scope: Page | Locator) {
  await expect(scope.getByTestId('studio-ai-request-checks')).toContainText('Passed')

  // The name is matched in its case, because the text key `approvalStep.approver.director` in the texts would match it too.
  const step = scope.getByTestId('studio-changes-file').filter({ hasText: /ApprovalStep/ })
  await expect(step).toHaveCount(1)
  await expect(step.getByRole('note', { name: 'approver, changed by this application', exact: true })).toHaveText(
    'Changed',
  )
  await step.getByRole('button', { name: 'Show the diff of ApprovalStep', exact: true }).click()
  await expect(step.getByTestId('json-diff-after')).toContainText('"director"')

  await expect(scope.getByTestId('studio-changes-file').filter({ hasText: scenarioName })).toHaveCount(1)
}

/**
 * Expects the refused request to read Failed, with its failure and its checks. The checks open, and their detail has the
 * contract scenario that failed. The request offers no Accept. `scope` is the page, or the root of the dark theme.
 */
async function expectRefused(scope: Page | Locator) {
  await expect(scope.getByTestId('studio-ai-request-state')).toHaveText('Failed')
  const alert = scope.getByTestId('studio-ai-request-failure')
  await expect(alert).toContainText('AI could not finish this request.')
  await expect(alert).toContainText('The checks still failed after the last fix round.')

  const checks = scope.getByTestId('studio-ai-request-checks')
  await expect(checks).toContainText('Failed')
  await checks.getByRole('button', { name: 'Show the checks', exact: true }).click()
  const row = scope
    .getByTestId('studio-ai-request-checks-detail')
    .getByTestId('studio-test-result')
    .filter({ hasText: refusedScenario })
  await expect(row).toHaveCount(1)
  await expect(row).toHaveAttribute('data-outcome', 'failed')

  await expect(scope.getByRole('button', { name: 'Accept', exact: true })).toHaveCount(0)
}

test("an author asks AI for a director's approval above a threshold, and the request reaches Ready with the layer and its scenario in its diff, in light and dark mode", async () => {
  authorContext = await browser.newContext({ ignoreHTTPSErrors: true })
  const page = await signIn(authorContext, host, author)
  let requestId: string | null = null
  try {
    // The call runs in the page, because the browser maps the tenant host to the server and the request context does not.
    const made = await page.evaluate(
      async ({ headers, name, template }) => {
        const response = await fetch('/api/studio/apps', {
          method: 'POST',
          headers: { ...headers, 'Content-Type': 'application/json' },
          body: JSON.stringify({ name, template }),
        })
        return response.status
      },
      { headers: csrfHeaders, name: application, template: 'approvals' },
    )
    expect(made).toBe(201)

    // The scripted answers name the ids of the application's resources, so they are read here. A template copy may get new ids.
    const api = `/api/studio/apps/${application}`
    const { items } = await readJson<{ items: Resource[] }>(page, `${api}/resources`)
    const idOf = (kind: string, name: string) => items.find((item) => item.kind === kind && item.name === name)!.id
    employeeRole = idOf('role', 'employee')
    const textsOf = async (name: string) =>
      (await readJson<{ content: Record<string, unknown> }>(page, `${api}/resources/${idOf('text', name)}`)).content
    const textsEn = await textsOf('TextsEn')
    const textsVi = await textsOf('TextsVi')

    // The module's ApprovalStep, with the kind director added to its approver field and labelled in the application's texts.
    const approvalStep: ApprovalStep = JSON.parse(
      await readFile(
        path.resolve(import.meta.dirname, '..', '..', '..', 'modules', 'approvals', 'entities', 'approval-step.json'),
        'utf8',
      ),
    )
    const layer = {
      ...approvalStep,
      fields: approvalStep.fields.map((field) =>
        field.name === 'approver'
          ? {
              ...field,
              values: [...(field.values ?? []), 'director'],
              valueLabels: { ...field.valueLabels, director: { textKey: directorKey } },
            }
          : field,
      ),
    }
    // The scenario: a 1500 request goes to the line manager and then to the director, and a 500 request to the manager alone.
    const scenario = largeRequestScenario(scenarioName, 'director', 1000, { large: 1500, small: 500 })

    // The plan, then the work reads the employee role, writes the layer and the two texts and the scenario, then its end,
    // then the review. The conflict test changes the role after AI reads it, and the accept then meets that change.
    const answersFile = path.join(folder, 'answers-director.json')
    await writeFile(
      answersFile,
      JSON.stringify([
        planAnswer(application, plan, usage),
        {
          content: [
            { type: 'tool_use', id: 'read-1', name: 'read_resource', input: { application, id: employeeRole } },
          ],
          stop_reason: 'tool_use',
          usage,
        },
        {
          content: [
            {
              type: 'tool_use',
              id: 'layer-1',
              name: 'write_layer',
              input: { application, module: 'approvals', id: approvalStep.id, content: layer },
            },
          ],
          stop_reason: 'tool_use',
          usage,
        },
        {
          content: [
            {
              type: 'tool_use',
              id: 'texts-en',
              name: 'write_resource',
              input: { application, content: withText(textsEn, directorKey, 'Director') },
            },
          ],
          stop_reason: 'tool_use',
          usage,
        },
        {
          content: [
            {
              type: 'tool_use',
              id: 'texts-vi',
              name: 'write_resource',
              input: { application, content: withText(textsVi, directorKey, 'Giám đốc') },
            },
          ],
          stop_reason: 'tool_use',
          usage,
        },
        {
          content: [
            { type: 'tool_use', id: 'scenario-1', name: 'write_resource', input: { application, content: scenario } },
          ],
          stop_reason: 'tool_use',
          usage,
        },
        { content: [{ type: 'text', text: 'Done.' }], stop_reason: 'end_turn', usage },
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
        answersFile,
      }),
    )

    await askAi(page, host, application, request)
    requestId = new URL(page.url()).searchParams.get('request')
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval', { timeout: 120_000 })
    await expect(page.getByText(plan, { exact: true })).toBeVisible()

    publisher = await startSpecPublisher(applications)
    try {
      await page.getByRole('button', { name: 'Approve plan', exact: true }).click()
      try {
        await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Ready', { timeout: 300_000 })
      } catch (error) {
        // The cause is in what the host and the publisher printed and in the request's steps.
        const steps = await readText(page, `${api}/authoring-requests/${requestId}`)
        await test.info().attach('agent-host-output', { body: agent?.output() ?? '', contentType: 'text/plain' })
        await test.info().attach('publisher-output', { body: publisher.output(), contentType: 'text/plain' })
        await test.info().attach('request', { body: steps, contentType: 'application/json' })
        throw error
      }
    } finally {
      await publisher.stop()
      publisher = undefined
    }
    // The scripted answers are used up, so the host stops. A running host could only pick up stray work.
    await agent?.stop()
    agent = undefined

    // Light mode, then dark mode after a reload, the same checks on the root of the dark theme. The request stays Ready.
    await expectReadyDiff(page)
    await chooseTheme(page, 'Dark')
    await page.reload()
    const dark = page.locator('[data-theme-mode="dark"]').first()
    await expect(dark).toBeVisible()
    await expectReadyDiff(dark)
    await chooseTheme(page, 'Light')
    readyRequestUrl = page.url()
  } finally {
    await page.close()
  }
})

test("an author's accept after a colleague changed the employee role shows the conflict, and the colleague's revision stays the newest", async () => {
  const context = await browser.newContext({ ignoreHTTPSErrors: true })
  const colleaguePage = await signIn(context, host, colleague)
  let changed: { id: string } | undefined
  try {
    // The colleague changes the employee role, which AI's draft read, as another author does while the draft waits.
    const outcome = await colleaguePage.evaluate(
      async ({ headers, application, role }) => {
        const api = `/api/studio/apps/${application}`
        const { version } = (await (await fetch(`${api}/resources/${role}`)).json()) as { version: string }
        const patch = await fetch(`${api}/resources/${role}`, {
          method: 'PATCH',
          headers: { ...headers, 'Content-Type': 'application/json-patch+json', 'If-Match': `"${version}"` },
          body: JSON.stringify([{ op: 'replace', path: '/label/textKey', value: 'role.admin' }]),
        })
        const { items } = (await (await fetch(`${api}/revisions`)).json()) as { items: { id: string }[] }
        return { status: patch.status, revision: items[0] }
      },
      { headers: csrfHeaders, application, role: employeeRole },
    )
    expect(outcome.status).toBe(200)
    changed = outcome.revision

    // The accept is the publisher's work, so the publisher runs from just before Accept until the accept ends.
    const page = await authorContext.newPage()
    try {
      await page.goto(readyRequestUrl)
      await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Ready')
      publisher = await startSpecPublisher(applications)
      try {
        await page.getByRole('button', { name: 'Accept', exact: true }).click()
        await expect(page.getByTestId('studio-ai-request-conflict')).toBeVisible({ timeout: 300_000 })
      } catch (error) {
        await test.info().attach('publisher-output', { body: publisher.output(), contentType: 'text/plain' })
        throw error
      } finally {
        await publisher.stop()
        publisher = undefined
      }

      // The conflict writes nothing, so the newest revision is still the colleague's, and the request stays Ready.
      const { items } = await readJson<{ items: { id: string }[] }>(page, `/api/studio/apps/${application}/revisions`)
      expect(items[0].id).toBe(changed.id)
      await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Ready')
    } finally {
      await page.close()
    }
  } finally {
    // The colleague undoes the change, so the draft's recorded version matches again and the next test can accept.
    if (changed !== undefined) {
      const undone = await colleaguePage.evaluate(
        async ({ headers, application, revision }) =>
          (await fetch(`/api/studio/apps/${application}/revisions/${revision}/undo`, { method: 'POST', headers })).status,
        { headers: csrfHeaders, application, revision: changed.id },
      )
      expect(undone).toBe(200)
    }
    await colleaguePage.close()
    await context.close()
  }
})

test("after the colleague undoes the change, the author accepts the request and publishes one active release", async () => {
  const page = await authorContext.newPage()
  try {
    await page.goto(readyRequestUrl)
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Ready')
    await expect(page.getByText('Checks and review passed', { exact: true })).toBeVisible()

    // The accept and the publish are the publisher's work, so it runs from just before Accept until the publish ends.
    publisher = await startSpecPublisher(applications)
    try {
      const sent = page.waitForResponse(
        (response) => response.request().method() === 'POST' && response.url().endsWith('/accept'),
      )
      await page.getByRole('button', { name: 'Accept', exact: true }).click()
      const answer = await sent
      expect(answer.status()).toBe(202)
      const acceptId = ((await answer.json()) as { id: string }).id

      const status = page.getByTestId('studio-request-status')
      try {
        await expect(status).toHaveAttribute('data-status', /^(succeeded|failed)$/, { timeout: 300_000 })
        expect(await status.getAttribute('data-status')).toBe('succeeded')
      } catch (error) {
        // The cause is in what the publisher printed and in the accept's publish request.
        const accepted = await readText(page, `/api/studio/apps/${application}/requests/${acceptId}`)
        await test.info().attach('publisher-output', { body: publisher.output(), contentType: 'text/plain' })
        await test.info().attach('accept-request', { body: accepted, contentType: 'application/json' })
        throw error
      }
      await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Accepted')

      // Publish from the Releases page, which the approvals application journey uses too.
      await page.goto(`https://${host}/studio?app=${application}&view=releases`)
      await page.getByRole('button', { name: 'Publish…', exact: true }).click()
      await confirmPublish(page)
      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
        timeout: 300_000,
      })
    } finally {
      await publisher.stop()
      publisher = undefined
    }

    const rows = page.getByTestId('studio-release-row')
    await expect(rows).toHaveCount(1)
    await expect(rows).toHaveAttribute('data-active', 'true')
  } finally {
    await page.close()
  }
})

test("a request that lets a requester decide their own request is refused by the approvals contract: it ends Failed with its checks and no Accept, and nothing is written, in light and dark mode", async () => {
  // Two rounds of checks run every approvals scenario, and each takes minutes, so the test waits longer than the default.
  test.setTimeout(1_200_000)
  const page = await authorContext.newPage()
  try {
    await page.goto(`https://${host}/`)
    const api = `/api/studio/apps/${application}`
    // The newest revision before the request. The refusal must leave it as the newest one.
    const before = (await readJson<{ items: { id: string }[] }>(page, `${api}/revisions`)).items[0].id

    // The module's RequestApproval. The layer changes the route step's toAdmin branch and nothing else, so a requester who
    // is their own step's approver gets the task, and the contract scenario sees it.
    const approval: Process = JSON.parse(
      await readFile(
        path.resolve(import.meta.dirname, '..', '..', '..', 'modules', 'approvals', 'processes', 'request-approval.json'),
        'utf8',
      ),
    )
    const changed = {
      ...approval,
      steps: approval.steps.map((step) =>
        step.name === 'route'
          ? {
              ...step,
              branches: step.branches?.map((branch) =>
                branch.name === 'toAdmin' ? { ...branch, when: 'approverUser == null' } : branch,
              ),
            }
          : step,
      ),
    }

    // The plan, then the work writes the layer, then its end. The one fix round changes nothing, so the checks fail again.
    const answersFile = path.join(folder, 'answers-refused.json')
    await writeFile(
      answersFile,
      JSON.stringify([
        planAnswer(application, refusedPlan, usage),
        {
          content: [
            {
              type: 'tool_use',
              id: 'layer-refused',
              name: 'write_layer',
              input: { application, module: 'approvals', id: approval.id, content: changed },
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
        answersFile,
      }),
      Ai__FixRounds: '1',
    })

    await askAi(page, host, application, refusedRequest)
    const requestId = new URL(page.url()).searchParams.get('request')
    await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Awaiting approval', { timeout: 120_000 })
    await expect(page.getByText(refusedPlan, { exact: true })).toBeVisible()

    publisher = await startSpecPublisher(applications)
    try {
      await page.getByRole('button', { name: 'Approve plan', exact: true }).click()
      try {
        await expect(page.getByTestId('studio-ai-request-state')).toHaveText('Failed', { timeout: 600_000 })
      } catch (error) {
        // The cause is in what the host and the publisher printed and in the request's steps.
        const steps = await readText(page, `${api}/authoring-requests/${requestId}`)
        await test.info().attach('agent-host-output', { body: agent?.output() ?? '', contentType: 'text/plain' })
        await test.info().attach('publisher-output', { body: publisher.output(), contentType: 'text/plain' })
        await test.info().attach('request', { body: steps, contentType: 'application/json' })
        throw error
      }
    } finally {
      await publisher.stop()
      publisher = undefined
    }
    // The scripted answers are used up, so the host stops, as the first test's host does.
    await agent?.stop()
    agent = undefined

    // Light mode, then dark mode after a reload, the same checks on the root of the dark theme. The request stays Failed.
    await expectRefused(page)
    await chooseTheme(page, 'Dark')
    await page.reload()
    const dark = page.locator('[data-theme-mode="dark"]').first()
    await expect(dark).toBeVisible()
    await expectRefused(dark)
    await chooseTheme(page, 'Light')

    // The refusal writes nothing: the newest revision is still the one before the request, and the module's
    // RequestApproval has no customization layer in the application.
    const { items } = await readJson<{ items: { id: string }[] }>(page, `${api}/revisions`)
    expect(items[0].id).toBe(before)
    const resource = await readJson<{ layer: unknown }>(page, `${api}/modules/approvals/resources/${approval.id}`)
    expect(resource.layer).toBeNull()
  } finally {
    await page.close()
  }
})

test("an outside MCP client registers once the administrator allows all clients, and a developer's change through it passes its checks but cannot be accepted", async () => {
  // The checks run every approvals scenario, which takes minutes, so the test waits longer than the default.
  test.setTimeout(1_200_000)
  const contexts: BrowserContext[] = []
  const newContext = async () => {
    const context = await browser.newContext({ ignoreHTTPSErrors: true })
    contexts.push(context)
    return context
  }
  const api = `/api/studio/apps/${application}`
  // The id of the application's newest revision, read in a page of the author's session.
  const newestRevision = async () => {
    const page = await authorContext.newPage()
    try {
      await page.goto(`https://${host}/`)
      return (await readJson<{ items: { id: string }[] }>(page, `${api}/revisions`)).items[0].id
    } finally {
      await page.close()
    }
  }

  try {
    const before = await newestRevision()
    const keycloak = required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')
    const realmUrl = `${keycloak}/realms/${tenant}`
    // A public client that asks for the code flow and the scope of the MCP server, the registration that the realm allows.
    const register = () =>
      fetch(`${realmUrl}/clients-registrations/openid-connect`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          client_name: clientName,
          redirect_uris: [clientRedirect],
          grant_types: ['authorization_code', 'refresh_token'],
          response_types: ['code'],
          token_endpoint_auth_method: 'none',
          scope: 'axis:authoring',
        }),
      })

    // Under the default `none` the realm refuses the registration.
    expect((await register()).status).toBe(403)

    // The tenant administrator that tenant create invited chooses All, and the choice survives a reload in dark mode.
    const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
    const adminPage = await (await newContext()).newPage()
    await adminPage.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
    await submitPassword(adminPage, firstAdmin.username, firstAdmin.password)
    await addAuthenticator(adminPage)
    await adminPage.waitForURL(`https://${host}/`)
    await adminPage.goto(`https://${host}/users`)
    await adminPage.getByRole('tab', { name: 'Connected apps', exact: true }).click()
    // The pattern does not match "Allow list".
    const all = adminPage.getByTestId('connected-apps').getByRole('radio', { name: /^All\b/ })
    await all.check()
    await adminPage.getByTestId('connected-apps').getByRole('button', { name: 'Save', exact: true }).click()
    await expect(adminPage.getByTestId('connected-apps-notice')).toHaveText('The setting was saved.')

    await chooseTheme(adminPage, 'Dark')
    await adminPage.reload()
    await adminPage.getByRole('tab', { name: 'Connected apps', exact: true }).click()
    const dark = adminPage.locator('[data-theme-mode="dark"]').first()
    await expect(dark.getByTestId('connected-apps').getByRole('radio', { name: /^All\b/ })).toBeChecked()
    await chooseTheme(adminPage, 'Light')

    // Registration now answers 201. The client's tokens get the developer's roles through the shared stand-in.
    const registered = await register()
    expect(registered.status).toBe(201)
    const clientId = ((await registered.json()) as { client_id: string }).client_id
    await mapPlatformRoles({
      admin: await keycloakAdmin({
        keycloakUrl: keycloak,
        adminSecret: required('AXIS_E2E_IDENTITY_ADMIN_SECRET', 'the secret of the axis-admin client'),
      }),
      realm: tenant,
      clientId,
    })

    // The developer signs in through the client with PKCE and allows it, and the client gets a code.
    const verifier = randomBytes(32).toString('base64url')
    const query = new URLSearchParams({
      client_id: clientId,
      redirect_uri: clientRedirect,
      response_type: 'code',
      scope: 'openid axis:authoring',
      state: 's',
      code_challenge: createHash('sha256').update(verifier).digest('base64url'),
      code_challenge_method: 'S256',
    })
    const personPage = await (await newContext()).newPage()
    await personPage.goto(`${realmUrl}/protocol/openid-connect/auth?${query}`)
    await submitPassword(personPage, developer.username, developer.password)
    await submitCode(personPage, await liveTotp(developer.secret))
    // Nothing serves the redirect address, so the page fails to load and the test reads the request.
    const redirected = personPage.waitForRequest((candidate) => candidate.url().startsWith(clientRedirect))
    await personPage.getByRole('button', { name: 'Allow', exact: true }).click()
    const code = new URL((await redirected).url()).searchParams.get('code')
    expect(code).toBeTruthy()

    const exchanged = await fetch(`${realmUrl}/protocol/openid-connect/token`, {
      method: 'POST',
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        client_id: clientId,
        code: code!,
        redirect_uri: clientRedirect,
        code_verifier: verifier,
      }),
    })
    expect(exchanged.status).toBe(200)
    const tokens = (await exchanged.json()) as { access_token: string; refresh_token: string }
    let accessToken = tokens.access_token
    let refreshToken = tokens.refresh_token

    // The client's calls to /mcp, made from a page on the tenant's origin, because Node's fetch refuses the self-signed
    // certificate. An access token lives 5 minutes and the checks take longer, so a 401 refreshes the token once.
    const mcpPage = await (await newContext()).newPage()
    await mcpPage.goto(`https://${host}/.well-known/oauth-protected-resource/mcp`)
    let session: string | undefined
    let nextId = 0
    const post = (token: string, payload: Record<string, unknown>) =>
      mcpPage.evaluate(
        async ({ token, session, payload }) => {
          const headers: Record<string, string> = {
            Authorization: `Bearer ${token}`,
            'Content-Type': 'application/json',
            Accept: 'application/json, text/event-stream',
          }
          if (session) {
            headers['Mcp-Session-Id'] = session
            headers['MCP-Protocol-Version'] = '2025-11-25'
          }
          const response = await fetch('/mcp', { method: 'POST', headers, body: JSON.stringify(payload) })
          const text = await response.text()
          return {
            status: response.status,
            session: response.headers.get('Mcp-Session-Id'),
            body: text.startsWith('{') ? JSON.parse(text) : null,
          }
        },
        { token, session, payload },
      )
    const call = async (method: string, params: Record<string, unknown>, notification = false) => {
      const payload = { jsonrpc: '2.0', method, params, ...(notification ? {} : { id: ++nextId }) }
      let answer = await post(accessToken, payload)
      if (answer.status === 401) {
        const refreshed = await fetch(`${realmUrl}/protocol/openid-connect/token`, {
          method: 'POST',
          body: new URLSearchParams({ grant_type: 'refresh_token', client_id: clientId, refresh_token: refreshToken }),
        })
        expect(refreshed.status).toBe(200)
        const renewed = (await refreshed.json()) as { access_token: string; refresh_token: string }
        accessToken = renewed.access_token
        refreshToken = renewed.refresh_token
        answer = await post(accessToken, payload)
      }
      return answer
    }
    interface ToolAnswer {
      isError: boolean
      text: string
      // The tool's output, which an error does not have.
      output: any
    }
    const tool = async (name: string, input: Record<string, unknown>): Promise<ToolAnswer> => {
      const answer = await call('tools/call', { name, arguments: input })
      expect(answer.status, `${name} answered ${answer.status}`).toBe(200)
      const result = answer.body.result
      return { isError: result.isError === true, text: result.content[0].text, output: result.structuredContent }
    }
    // A call that must not be refused.
    const run = async (name: string, input: Record<string, unknown>) => {
      const answer = await tool(name, input)
      expect(answer.isError, `${name}: ${answer.text}`).toBe(false)
      return answer.output
    }

    const initialized = await call('initialize', {
      protocolVersion: '2025-11-25',
      capabilities: {},
      clientInfo: { name: 'e2e', version: '1' },
    })
    expect(initialized.status).toBe(200)
    expect(initialized.session).toBeTruthy()
    session = initialized.session!
    expect((await call('notifications/initialized', {}, true)).status).toBe(202)

    // The client makes a draft and adds the approver kind generalDirector to the module's ApprovalStep, the whole resource as
    // read, which already holds the accepted director. The two texts and a scenario follow.
    const { id: draft } = await run('create_draft', { application })
    const approvalStepId = (
      JSON.parse(
        await readFile(
          path.resolve(import.meta.dirname, '..', '..', '..', 'modules', 'approvals', 'entities', 'approval-step.json'),
          'utf8',
        ),
      ) as ApprovalStep
    ).id
    const { content: current } = (await run('read_module_resource', {
      application,
      module: 'approvals',
      id: approvalStepId,
    })) as { content: ApprovalStep }
    const layer = {
      ...current,
      fields: current.fields.map((field) =>
        field.name === 'approver'
          ? {
              ...field,
              values: [...(field.values ?? []), 'generalDirector'],
              valueLabels: { ...field.valueLabels, generalDirector: { textKey: generalDirectorKey } },
            }
          : field,
      ),
    }
    await run('write_layer', { application, draft, module: 'approvals', id: approvalStepId, content: layer })

    // The ids of the application's texts, read through the resources route as the first test reads them.
    const page = await authorContext.newPage()
    let textsIds: Record<string, string>
    try {
      await page.goto(`https://${host}/`)
      const { items } = await readJson<{ items: Resource[] }>(page, `${api}/resources`)
      const idOf = (name: string) => items.find((item) => item.kind === 'text' && item.name === name)!.id
      textsIds = { 'General director': idOf('TextsEn'), 'Tổng giám đốc': idOf('TextsVi') }
    } finally {
      await page.close()
    }
    for (const [label, id] of Object.entries(textsIds)) {
      const { content } = (await run('read_resource', { application, id, draft })) as { content: Record<string, unknown> }
      await run('write_resource', { application, draft, content: withText(content, generalDirectorKey, label) })
    }
    // A request from 5000 on needs the general director after the line manager.
    await run('write_resource', {
      application,
      draft,
      content: largeRequestScenario(clientScenarioName, 'generalDirector', 5000, { large: 6000, small: 500 }),
    })

    const validated = (await run('validate_draft', { application, draft })) as { diagnostics: { severity: string }[] }
    expect(validated.diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])

    // run_scenarios stores a test request that the publisher runs, so the publisher runs until the request ends.
    publisher = await startSpecPublisher(applications)
    try {
      const started = (await run('run_scenarios', { application, draft })) as { request: string }
      type Run = { status: string; scenarios: { name: string; outcome: string }[] }
      let finished: Run | undefined
      try {
        await expect
          .poll(
            async () => {
              const read = (await run('run_scenarios', { application, draft, request: started.request })) as Run
              finished = read
              return read.status
            },
            { timeout: 600_000, intervals: [5_000] },
          )
          .toMatch(/^(succeeded|failed)$/)
      } catch (error) {
        await test.info().attach('publisher-output', { body: publisher.output(), contentType: 'text/plain' })
        throw error
      }
      expect(finished?.status, JSON.stringify(finished)).toBe('succeeded')
      expect(finished?.scenarios.filter((scenario) => scenario.outcome === 'failed')).toEqual([])
      expect(finished?.scenarios.find((scenario) => scenario.name === clientScenarioName)?.outcome).toBe('passed')
    } finally {
      await publisher.stop()
      publisher = undefined
    }

    // No ready authoring request owns this draft, so the accept is refused and nothing is written.
    const accepted = await tool('accept_draft', { application, draft })
    expect(accepted.isError).toBe(true)
    expect(accepted.text).toBe('No ready authoring request of yours has this draft.')
    expect(await newestRevision()).toBe(before)
  } finally {
    for (const context of contexts) {
      await context.close()
    }
  }
})
