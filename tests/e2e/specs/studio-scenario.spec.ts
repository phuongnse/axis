import { cp, mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { startServer } from '../activate'
import { e2eApp, studioFolder } from '../server-settings'
import { useSession } from '../sessions'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys save a scenario into a copy of the sample folder, so they run on the same second server as the
// other Studio creation specs and never change the shared copy of the E2E server.
test.describe('Studio edits a scenario in the typed editor', () => {
  test.describe.configure({ mode: 'serial' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>
  let scenarioURL: string

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-scenario-'))
    await cp(studioFolder, copy, { recursive: true })
    await rm(path.join(copy, 'texts', 'vi.json'), { force: true })
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  async function create(page: Page, kind: string, label: string, value: string) {
    await page.getByRole('button', { name: 'New resource' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Kind' }).click()
    await page.getByTitle(kind, { exact: true }).click()
    await dialog.getByRole('textbox', { name: label }).fill(value)
    await dialog.getByRole('button', { name: 'Create' }).click()
  }

  async function choose(page: Page, field: Locator, option: string) {
    await field.locator('.ant-select-selector').click()
    await page.locator('.ant-select-dropdown:visible').getByTitle(option, { exact: true }).click()
  }

  async function addStep(page: Page, index: number, type: string) {
    await page.getByRole('button', { name: 'Add step', exact: true }).click()
    await choose(page, page.getByTestId(`studio-field-/steps/${index}/type`), type)
  }

  async function save(page: Page) {
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()
  }

  async function diagnostics(page: Page) {
    return page.evaluate(async (url) => (await fetch(url)).json(), `${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
  }

  async function savedSteps(): Promise<Record<string, unknown>[]> {
    const file = JSON.parse(await readFile(path.join(copy, 'scenarios', 'approval-flow.json'), 'utf8')) as {
      steps: Record<string, unknown>[]
    }
    return file.steps
  }

  test('an author builds a scenario in the typed editor and the folder diagnostics stay the same', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    const before = await diagnostics(page)

    await create(page, 'scenario', 'Name', 'ApprovalFlow')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('scenarios/approval-flow.json')
    await expect(page.getByTestId('studio-scenario')).toBeVisible()
    scenarioURL = page.url()

    await choose(page, page.getByTestId('studio-field-/steps/0/type'), 'createRecord')
    await choose(page, page.getByTestId('studio-field-/steps/0/entity'), 'PurchaseRequest')
    await choose(page, page.getByTestId('studio-field-/steps/0/as'), 'author')
    await page.getByTestId('studio-field-/steps/0/save').getByRole('textbox').fill('request')
    await choose(page, page.getByTestId('studio-field-/steps/0/values'), 'title')
    await page.getByTestId('studio-field-/steps/0/values/title').getByRole('textbox').fill('Laptops')

    await addStep(page, 1, 'startProcess')
    await choose(page, page.getByTestId('studio-field-/steps/1/process'), 'PurchaseRequestApproval')
    await choose(page, page.getByTestId('studio-field-/steps/1/record'), 'request')
    await choose(page, page.getByTestId('studio-field-/steps/1/as'), 'author')

    await addStep(page, 2, 'expectTask')
    await choose(page, page.getByTestId('studio-field-/steps/2/record'), 'request')
    await choose(page, page.getByTestId('studio-field-/steps/2/step'), 'managerApproval')
    await choose(page, page.getByTestId('studio-field-/steps/2/state'), 'open')

    await save(page)
    expect((await savedSteps()).map((step) => step.type)).toEqual(['createRecord', 'startProcess', 'expectTask'])
    expect(await diagnostics(page)).toEqual(before)
  })

  test('an author adds a completeTask step that expects the author to be denied', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(scenarioURL)
    await expect(page.getByTestId('studio-scenario-step')).toHaveCount(3)

    await addStep(page, 3, 'completeTask')
    await choose(page, page.getByTestId('studio-field-/steps/3/record'), 'request')
    await choose(page, page.getByTestId('studio-field-/steps/3/step'), 'managerApproval')
    await choose(page, page.getByTestId('studio-field-/steps/3/outcome'), 'approve')
    await choose(page, page.getByTestId('studio-field-/steps/3/as'), 'author')
    await choose(page, page.getByTestId('studio-field-/steps/3/expect'), 'denied')

    await save(page)
    expect((await savedSteps()).at(-1)).toEqual({
      type: 'completeTask',
      record: 'request',
      step: 'managerApproval',
      outcome: 'approve',
      as: 'author',
      expect: 'denied',
    })
  })
})
