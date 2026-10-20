import { cp, mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { startServer } from '../activate'
import { e2eApp, studioFolder } from '../server-settings'
import { useSession } from '../sessions'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys change the process, and the other Studio specs compare the diagnostics of the shared copy. So these run
// on a second server whose Studio edits a copy of its own. The copy is not the E2E server's.
test.describe('Studio edits a process in the process editor', () => {
  test.describe.configure({ mode: 'serial' })

  const file = 'processes/purchase-request-approval.json'
  const folder = '/api/studio/apps/PurchaseRequests'
  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-process-editor-'))
    await cp(studioFolder, copy, { recursive: true })
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
    await page.getByTestId(`studio-field-${field}`).locator('.ant-select-selector').click()
    // The dropdown of the previous pick may still be leaving, with the same option. The newest one is last.
    await page.getByRole('option', { name, exact: true }).last().click()
  }

  test('an author adds a decision step, makes submit lead to it and saves without a new diagnostic', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const id = await processId(page)
    const before = await diagnostics(page)

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
    await page.getByRole('combobox', { name: 'Add step' }).click()
    await page.getByRole('option', { name: 'decision', exact: true }).click()
    const card = page.getByTestId('studio-step-10')
    await card.getByRole('textbox', { name: 'Step name' }).fill('checkTotal')
    await card.getByRole('button', { name: 'Add branch' }).click()
    const when = page.getByTestId('studio-field-/steps/10/branches/0/when')
    await expect(when.locator('.cm-editor')).toBeVisible()
    await when.getByRole('textbox').fill('totalAmount > 0')
    await pick(page, '/steps/10/branches/0/next', 'managerApproval')
    await pick(page, '/steps/10/otherwise', 'markRejected')
    await pick(page, '/steps/0/next', 'checkTotal')
    await page.getByRole('button', { name: 'Save' }).click()

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
    await expect(page.getByTestId('studio-step-3')).toContainText('financeApproval')
    await page.getByTestId('studio-field-/steps/3/dueIn').getByRole('textbox').fill('P5D')
    await pick(page, '/steps/3/outcomes/1/next', 'markRejected')
    await page.getByRole('button', { name: 'Save' }).click()

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
    const card = page.getByTestId('studio-step-4')
    await expect(card).toContainText('markApproved')
    await card.getByRole('button', { name: 'Remove step' }).click()
    await page.getByRole('button', { name: 'Save' }).click()
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
})
