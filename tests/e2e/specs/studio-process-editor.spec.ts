import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { pickAction, pickOption } from '../select'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'
import { openFormTab } from '../studio-resources'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys change the process. They run on a second server over a folder of its own, copied from the committed
// sample, so no other spec's writes reach it.
test.describe('Studio edits a process in the process editor', () => {
  test.describe.configure({ mode: 'serial' })

  const file = 'processes/purchase-request-approval.json'
  const folder = '/api/studio/apps/PurchaseRequests'
  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-process-editor-'))
    await cp(purchaseRequests, copy, { recursive: true })
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  async function processId(page: Page): Promise<string> {
    const list = await page.request.get(`${server.url}${folder}/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    return items.find((item) => item.file === file)!.id
  }

  async function diagnostics(page: Page): Promise<unknown> {
    return (await page.request.get(`${server.url}${folder}/diagnostics`)).json()
  }

  async function pick(page: Page, field: string, name: string) {
    await pickOption(page.getByTestId(`studio-field-${field}`).getByRole('combobox'), name)
  }

  test('an author adds a decision step, makes submit lead to it and saves without a new diagnostic', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const id = await processId(page)
    const before = await diagnostics(page)

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
    await openFormTab(page)
    await pickAction(
      page.getByRole('combobox', { name: 'Add step', exact: true }),
      'decision',
      page.getByTestId('studio-step-10'),
    )
    const card = page.getByTestId('studio-step-10')
    await card.getByRole('textbox', { name: 'Step name', exact: true }).fill('checkTotal')
    await card.getByRole('button', { name: 'Add branch', exact: true }).click()
    const when = page.getByTestId('studio-field-/steps/10/branches/0/when')
    await expect(when.locator('.cm-editor')).toBeVisible()
    await when.getByRole('textbox').fill('totalAmount > 0')
    await pick(page, '/steps/10/branches/0/next', 'managerApproval')
    await pick(page, '/steps/10/otherwise', 'markRejected')
    await pick(page, '/steps/0/next', 'checkTotal')
    await page.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(page.getByTestId('studio-saved')).toBeVisible()
    expect(await diagnostics(page)).toEqual(before)
    const saved = JSON.parse(await readFile(path.join(copy, file), 'utf8')) as { steps: Record<string, unknown>[] }
    expect(saved.steps[0].next).toBe('checkTotal')
    expect(saved.steps[10]).toEqual({
      name: 'checkTotal',
      type: 'decision',
      branches: [{ when: 'totalAmount > 0', next: 'managerApproval' }],
      otherwise: 'markRejected',
    })
  })

  test('an author edits the set expression of markRejected and the file changes only there', async ({ page }) => {
    await useSession(page, 'hoa')
    const id = await processId(page)
    const before = await diagnostics(page)
    const original = await readFile(path.join(copy, file), 'utf8')

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
    await openFormTab(page)
    await expect(page.getByTestId('studio-step-8')).toContainText('markRejected')
    const status = page.getByTestId('studio-field-/steps/8/set/status')
    await expect(status.locator('.cm-editor')).toBeVisible()
    await status.getByRole('textbox', { name: 'Expression', exact: true }).fill("'returned'")
    await page.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(page.getByTestId('studio-saved')).toBeVisible()
    expect(await diagnostics(page)).toEqual(before)
    const expected = JSON.parse(original) as { steps: { set: Record<string, string> }[] }
    expected.steps[8].set.status = "'returned'"
    expect(JSON.parse(await readFile(path.join(copy, file), 'utf8'))).toEqual(expected)
  })

  test('an author changes the due time and an outcome target of a task and saves only those changes', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const id = await processId(page)
    const before = JSON.parse(await readFile(path.join(copy, file), 'utf8')) as {
      steps: { dueIn?: string; outcomes: { next: string }[] }[]
    }
    const beforeDiagnostics = await diagnostics(page)

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
    await openFormTab(page)
    await expect(page.getByTestId('studio-step-3')).toContainText('financeApproval')
    await page.getByTestId('studio-field-/steps/3/dueIn').getByRole('textbox').fill('P5D')
    await pick(page, '/steps/3/outcomes/1/next', 'markRejected')
    await page.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(page.getByTestId('studio-saved')).toBeVisible()
    const expected = structuredClone(before)
    expected.steps[3].dueIn = 'P5D'
    expected.steps[3].outcomes[1].next = 'markRejected'
    expect(JSON.parse(await readFile(path.join(copy, file), 'utf8'))).toEqual(expected)
    expect(await diagnostics(page)).toEqual(beforeDiagnostics)
  })

  test('an author removes a step, and the diagnostic of the transition that names it opens its picker', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const id = await processId(page)

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
    await openFormTab(page)
    const card = page.getByTestId('studio-step-4')
    await expect(card).toContainText('markApproved')
    await card.getByRole('button', { name: 'Remove step', exact: true }).click()
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    const diagnostic = page
      .getByTestId('studio-diagnostics')
      .getByRole('button')
      .filter({ hasText: 'Path: /steps/2/otherwise' })
    await expect(diagnostic).toContainText('AXC0070')
    await diagnostic.click()

    await expect(page).toHaveURL(/path=%2Fsteps%2F2%2Fotherwise/)
    const otherwise = page.getByTestId('studio-field-/steps/2/otherwise')
    await expect(otherwise).toBeInViewport()
    await expect(otherwise.getByTestId('studio-reference-unknown')).toBeVisible()
  })

  test('a step whose transition names a missing step is marked on the graph, and the transition draws no edge', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const id = await processId(page)
    const target = path.join(copy, file)
    const original = await readFile(target, 'utf8')

    try {
      const content = JSON.parse(original) as { steps: { name: string }[] }
      const i = content.steps.findIndex((step) => step.name === 'markRejected')
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
      await expect(page.getByTestId(`rf__edge-/steps/${i}/next`)).toHaveCount(1)
      await expect(page.getByTestId(`studio-graph-node-${i}`)).not.toHaveAttribute('data-marked')

      await page.getByRole('tab', { name: 'JSON', exact: true }).click()
      const textarea = page.getByRole('textbox', { name: 'JSON', exact: true })
      const draft = JSON.parse(await textarea.inputValue()) as { steps: { next?: string }[] }
      draft.steps[i].next = 'noSuchStep'
      await textarea.fill(JSON.stringify(draft, null, 2))
      await page.getByRole('button', { name: 'Save', exact: true }).click()

      await expect(page.getByTestId('studio-saved')).toBeVisible()
      await page.getByRole('tab', { name: 'Design', exact: true }).click()
      const node = page.getByTestId(`studio-graph-node-${i}`)
      await expect(node).toHaveAttribute('data-marked', 'true')
      await expect(node).toHaveAccessibleName('markRejected · operation · has diagnostics')
      await expect(page.getByTestId(`rf__edge-/steps/${i}/next`)).toHaveCount(0)
    } finally {
      await writeFile(target, original)
    }
  })

  test('an author moves an edge in the panel, removes a step by keyboard, sees the diagnostic after a save and undoes it', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const id = await processId(page)
    const target = path.join(copy, file)
    const original = await readFile(target, 'utf8')

    try {
      // The earlier journeys changed the copy, so the steps are found by name.
      const content = JSON.parse(original) as { steps: { name: string }[] }
      const indexOf = (name: string) => content.steps.findIndex((step) => step.name === name)
      const first = 0
      const removed = indexOf('markReturned')
      expect(removed).toBeGreaterThan(0)
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)

      const edge = page.getByTestId(`rf__edge-/steps/${first}/next`)
      await expect(edge).toHaveAccessibleName(/^submit to \w+ · next$/)
      await page.getByTestId(`studio-graph-node-${first}`).click()
      await pick(page, `/steps/${first}/next`, 'done')
      await expect(edge).toHaveAccessibleName('submit to done · next')

      const node = page.getByTestId(`studio-graph-node-${removed}`)
      await node.focus()
      await page.keyboard.press('Enter')
      await expect(page.getByTestId(`studio-step-${removed}`)).toContainText('markReturned')
      await page.keyboard.press('Delete')
      await expect(page.getByTestId('studio-process-announcement')).toHaveText('Removed step markReturned')
      await expect(page.getByTestId(`studio-graph-node-${content.steps.length - 1}`)).toHaveCount(0)

      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-saved')).toBeVisible()
      await expect(page.getByTestId('studio-diagnostics')).toContainText('AXC0070')

      await page.getByRole('tab', { name: 'Design', exact: true }).click()
      await page.getByRole('button', { name: 'Undo', exact: true }).click()
      await expect(page.getByRole('button', { name: 'markReturned · operation', exact: true })).toBeVisible()
    } finally {
      await writeFile(target, original)
    }
  })
})
