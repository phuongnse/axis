import { cp, mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { waitForDialog } from '../dialogs'
import { pickOption } from '../select'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'
import { createResource, openFormTab } from '../studio-resources'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys create files. They run on a second server over a folder of their own, copied from the committed sample
// without texts/vi.json, so no other spec's writes reach it.
// The entity journeys also build an entity in the designer, and the expression journey reopens that entity.
test.describe('Studio creates a resource from a starter', () => {
  test.describe.configure({ mode: 'serial' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-resource-'))
    await cp(purchaseRequests, copy, { recursive: true })
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

  async function pick(page: Page, field: string, option: string) {
    await pickOption(page.getByTestId(field).getByRole('combobox'), option)
  }

  async function save(page: Page) {
    const answer = page.waitForResponse(
      (response) =>
        response.request().method() === 'PATCH' && response.url().includes('/api/studio/apps/PurchaseRequests/resources/'),
      { timeout: 60_000 },
    )
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()
    const { diagnostics } = (await (await answer).json()) as { diagnostics: { file: string }[] }
    return diagnostics
  }

  async function addField(page: Page, index: number, name: string, type: string) {
    await page.getByRole('button', { name: 'Add field', exact: true }).click()
    await page.getByTestId(`studio-field-/fields/${index}/name`).getByRole('textbox').fill(name)
    await pick(page, `studio-field-/fields/${index}/type`, type)
  }

  test('an author creates an entity and a page, and each opens in the editor', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await createResource(page, 'entity', 'CostCentre')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/cost-centre.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()

    await createResource(page, 'page', 'Overview')
    await openFormTab(page)
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('pages/overview.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()
  })

  test('an author adds a vi text resource that holds every key of en', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await createResource(page, 'text', 'vi', 'TextsVi')
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
    const entityName = page.getByTestId('studio-entity-designer').getByRole('textbox', { name: 'Entity name', exact: true })

    await createResource(page, 'entity', 'ProjectTask')
    await expect(entityName).toHaveValue('ProjectTask')
    await createResource(page, 'entity', 'Project')
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
    await expect(page.getByTestId('studio-entity-designer').getByRole('textbox', { name: 'Entity name', exact: true })).toHaveValue(
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

  test('an author builds money, file, references and labelled enum fields in the designer', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    const entityName = page.getByTestId('studio-entity-designer').getByRole('textbox', { name: 'Entity name', exact: true })
    const savedFields = async (file: string) =>
      (JSON.parse(await readFile(path.join(copy, 'entities', file), 'utf8')) as { fields: unknown[] }).fields
    const tag = async (field: string, value: string) => {
      const combobox = page.getByTestId(field).getByRole('combobox')
      await combobox.fill(value)
      await combobox.press('Enter')
      await combobox.press('Escape')
    }

    await createResource(page, 'entity', 'ShipmentLine')
    await expect(entityName).toHaveValue('ShipmentLine')
    await addField(page, 1, 'supplier', 'reference')
    await pick(page, 'studio-field-/fields/1/target', 'Supplier')
    const lineDiagnostics = await save(page)

    await createResource(page, 'entity', 'Shipment')
    await expect(entityName).toHaveValue('Shipment')
    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/shipment.json')

    await addField(page, 1, 'currencyCode', 'enum')
    await tag('studio-field-/fields/1/values', 'VND')
    await tag('studio-field-/fields/1/values', 'USD')
    await pick(page, 'studio-field-/fields/1/valueLabels/VND', 'nav.suppliers')
    await pick(page, 'studio-field-/fields/1/valueLabels/USD', 'nav.departments')

    await addField(page, 2, 'amount', 'money')
    await page.getByTestId('studio-field-/fields/2').getByRole('radio', { name: 'Field', exact: true }).check()
    await pick(page, 'studio-field-/fields/2/currencyField', 'currencyCode')

    await addField(page, 3, 'receipt', 'file')
    await page.getByTestId('studio-field-/fields/3/maxSize').getByRole('spinbutton').fill('1048576')
    await tag('studio-field-/fields/3/accept', 'application/pdf')
    await tag('studio-field-/fields/3/accept', 'image/*')

    await addField(page, 4, 'departments', 'references')
    await pick(page, 'studio-field-/fields/4/target', 'Department')
    await addField(page, 5, 'lines', 'child-collection')
    await pick(page, 'studio-field-/fields/5/target', 'ShipmentLine')

    const diagnostics = [...lineDiagnostics, ...(await save(page))]
    expect(diagnostics.filter((diagnostic) => diagnostic.file.startsWith('entities/shipment'))).toEqual([])
    expect(await savedFields('shipment-line.json')).toEqual([
      { name: 'name', type: 'text' },
      { name: 'supplier', type: 'reference', target: 'Supplier' },
    ])
    expect(await savedFields('shipment.json')).toEqual([
      { name: 'name', type: 'text' },
      {
        name: 'currencyCode',
        type: 'enum',
        values: ['VND', 'USD'],
        valueLabels: { VND: { textKey: 'nav.suppliers' }, USD: { textKey: 'nav.departments' } },
      },
      { name: 'amount', type: 'money', currencyField: 'currencyCode' },
      { name: 'receipt', type: 'file', maxSize: 1048576, accept: ['application/pdf', 'image/*'] },
      { name: 'departments', type: 'references', target: 'Department' },
      { name: 'lines', type: 'child-collection', target: 'ShipmentLine' },
    ])
  })

  test('a click on the disabled Create button fails within the action timeout and names the button', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    await page.getByRole('button', { name: 'New resource', exact: true }).click()
    const dialog = await waitForDialog(page, 'New resource')
    const create = dialog.getByRole('button', { name: 'Create', exact: true })

    const started = Date.now()
    const error = await create.click().catch((caught: Error) => caught)

    expect(error).toBeInstanceOf(Error)
    expect((error as Error).message).toContain('Timeout 15000ms')
    expect((error as Error).message).toContain('Create')
    expect(Date.now() - started).toBeLessThan(20_000)
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  })
})
