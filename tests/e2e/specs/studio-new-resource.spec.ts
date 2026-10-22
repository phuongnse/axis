import { cp, mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { startServer } from '../activate'
import { pickOption } from '../select'
import { e2eApp, studioFolder } from '../server-settings'
import { useSession } from '../sessions'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys create files, and the other Studio specs compare the diagnostics of the shared copy. So these run on
// a second server whose Studio edits a copy of its own, without texts/vi.json. The copy is not the E2E server's.
// The entity journeys also build an entity in the designer, and the expression journey reopens that entity.
test.describe('Studio creates a resource from a starter', () => {
  test.describe.configure({ mode: 'serial' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-resource-'))
    await cp(studioFolder, copy, { recursive: true })
    await rm(path.join(copy, 'texts', 'vi.json'), { force: true })
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

  async function create(page: Page, kind: string, label: string, value: string) {
    await page.getByRole('button', { name: 'New resource' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Kind' }).click()
    await page.getByTitle(kind, { exact: true }).click()
    await dialog.getByRole('textbox', { name: label }).fill(value)
    await dialog.getByRole('button', { name: 'Create' }).click()
  }

  async function pick(page: Page, field: string, option: string) {
    await pickOption(page.getByTestId(field).getByRole('combobox'), option)
  }

  async function save(page: Page) {
    const answer = page.waitForResponse(
      (response) =>
        response.request().method() === 'PATCH' && response.url().includes('/api/studio/apps/PurchaseRequests/resources/'),
    )
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()
    const { diagnostics } = (await (await answer).json()) as { diagnostics: { file: string }[] }
    return diagnostics
  }

  async function addField(page: Page, index: number, name: string, type: string) {
    await page.getByRole('button', { name: 'Add field' }).click()
    await page.getByTestId(`studio-field-/fields/${index}/name`).getByRole('textbox').fill(name)
    await pick(page, `studio-field-/fields/${index}/type`, type)
  }

  test('an author creates an entity and a page, and each opens in the editor', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await create(page, 'entity', 'Name', 'CostCentre')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/cost-centre.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()

    await create(page, 'page', 'Name', 'Overview')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('pages/overview.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()
  })

  test('an author adds a vi text resource that holds every key of en', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await create(page, 'text', 'Locale', 'vi')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/vi.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()

    const keys = async (locale: string) => {
      const { texts } = JSON.parse(await readFile(path.join(copy, 'texts', `${locale}.json`), 'utf8')) as {
        texts: Record<string, string>
      }
      return Object.keys(texts).toSorted()
    }
    const english = await keys('en')
    expect(english.length).toBeGreaterThan(0)
    expect(await keys('vi')).toEqual(english)
  })

  test('an author builds an entity with enum, reference and child-collection fields in the designer', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    const entityName = page.getByTestId('studio-entity-designer').getByRole('textbox', { name: 'Entity name' })

    await create(page, 'entity', 'Name', 'ProjectTask')
    await expect(entityName).toHaveValue('ProjectTask')
    await create(page, 'entity', 'Name', 'Project')
    await expect(entityName).toHaveValue('Project')
    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/project.json')

    await addField(page, 1, 'stage', 'enum')
    const values = page.getByTestId('studio-field-/fields/1/values').getByRole('combobox')
    await values.fill('open')
    await values.press('Enter')
    await values.fill('closed')
    await values.press('Enter')
    await values.press('Escape')

    await addField(page, 2, 'department', 'reference')
    await pick(page, 'studio-field-/fields/2/target', 'Department')
    await addField(page, 3, 'tasks', 'child-collection')
    await pick(page, 'studio-field-/fields/3/target', 'ProjectTask')

    const diagnostics = await save(page)
    expect(diagnostics.filter((diagnostic) => diagnostic.file === 'entities/project.json')).toEqual([])
    const project = JSON.parse(await readFile(path.join(copy, 'entities', 'project.json'), 'utf8')) as {
      fields: unknown[]
    }
    expect(project.fields).toEqual([
      { name: 'name', type: 'text' },
      { name: 'stage', type: 'enum', values: ['open', 'closed'] },
      { name: 'department', type: 'reference', target: 'Department' },
      { name: 'tasks', type: 'child-collection', target: 'ProjectTask' },
    ])
  })

  test('an author adds a computed field and the expression editor flags and then clears a type error', async ({
    page,
  }) => {
    const { id } = JSON.parse(await readFile(path.join(copy, 'entities', 'project.json'), 'utf8')) as { id: string }

    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
    await expect(page.getByTestId('studio-entity-designer').getByRole('textbox', { name: 'Entity name' })).toHaveValue(
      'Project',
    )

    await addField(page, 4, 'nameLength', 'integer')
    const field = page.getByTestId('studio-field-/fields/4/expression')
    // The read-only fallback is replaced once CodeMirror loads, so wait for the editor before typing.
    await expect(field.locator('.cm-editor')).toBeVisible()
    await field.getByRole('textbox').fill('length(name)')

    const diagnostics = await save(page)
    expect(diagnostics.filter((diagnostic) => diagnostic.file === 'entities/project.json')).toEqual([])

    await field.getByRole('textbox').fill('name > 5')
    await expect(field.locator('.cm-lintRange-error')).toBeVisible()
    await expect(field.getByTestId('studio-expression-error')).toBeVisible()

    await field.getByRole('textbox').fill('length(name)')
    await expect(field.locator('.cm-lintRange-error')).toHaveCount(0)
    await expect(field.getByTestId('studio-expression-error')).toHaveCount(0)
    await expect(field.getByTestId('studio-expression-type')).toContainText('integer')
  })
})
