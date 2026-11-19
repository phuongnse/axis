import { randomUUID } from 'node:crypto'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type BrowserContext, type Locator, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { sparePort } from '../activate'
import { runAdmin } from '../admin-tool'
import { startAgentHost } from '../agent-host'
import { askAi, holdAiSpecLock, signIn, startSpecPublisher, type AuthoringAccount } from '../ai-authoring'
import { csrfHeaders } from '../csrf'
import { startDirectoryServer } from '../directory-server'
import { platformSecretKey, required } from '../server-settings'
import { chooseTheme } from '../shell'
import { confirmPublish } from '../studio-resources'
import {
  addAuthoringAccount,
  agentHostSettings,
  issueTenantCertificate,
  keycloakAdmin,
  prepareAuthoringRealm,
  startTunnelProxy,
  tenantHost,
} from '../../../scripts/lib/ai-eval-stack.mjs'

// The AI authoring journey: an author asks AI for a director's approval above a threshold, a colleague's change meets the
// author's accept, and the author accepts and publishes. A last request lets a requester decide their own request, and
// the approvals contract refuses it. See the AI authoring journey spec in docs/testing.md.
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
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host])
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
    const withLabel = (content: Record<string, unknown>, label: string) => ({
      ...content,
      texts: { ...(content.texts as Record<string, string>), [directorKey]: label },
    })

    // The scenario: a 1500 request goes to the line manager and then to the director, and a 500 request to the manager alone.
    const scenario = {
      id: randomUUID(),
      kind: 'scenario',
      name: scenarioName,
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
              { sequence: 2, approver: 'director', employee: 'dan', minAmount: 1000 },
            ],
          },
        },
        {
          type: 'createRecord',
          entity: 'approvals/Request',
          as: 'ann',
          save: 'large',
          values: { type: 'purchase', title: 'Monitor', amount: 1500 },
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
          values: { type: 'purchase', title: 'Keyboard', amount: 500 },
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

    // The plan, then the work reads the employee role, writes the layer and the two texts and the scenario, then its end,
    // then the review. The conflict test changes the role after AI reads it, and the accept then meets that change.
    const answersFile = path.join(folder, 'answers-director.json')
    await writeFile(
      answersFile,
      JSON.stringify([
        { content: [{ type: 'text', text: plan }], stop_reason: 'end_turn', usage },
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
              input: { application, content: withLabel(textsEn, 'Director') },
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
              input: { application, content: withLabel(textsVi, 'Giám đốc') },
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
        { content: [{ type: 'text', text: refusedPlan }], stop_reason: 'end_turn', usage },
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
