import { cp, mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { waitForDialog } from '../dialogs'
import { pickOption, pickOptions } from '../select'
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
    await useSession(page, 'author')
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
    await useSession(page, 'author')
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
    await useSession(page, 'author')
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

    await useSession(page, 'author')
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
    await useSession(page, 'author')
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

  test('an author sets a default, a minimum, a searchable text field and a unique index in the designer', async ({
    page,
  }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    const entityName = page.getByTestId('studio-entity-designer').getByRole('textbox', { name: 'Entity name', exact: true })

    await createResource(page, 'entity', 'Backlog')
    await expect(entityName).toHaveValue('Backlog')
    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/backlog.json')

    await addField(page, 1, 'stage', 'enum')
    const values = page.getByTestId('studio-field-/fields/1/values').getByRole('combobox')
    await values.fill('open')
    await values.press('Enter')
    await values.fill('closed')
    await values.press('Enter')
    await values.press('Escape')
    const stageDefault = page.getByTestId('studio-field-/fields/1/default')
    // The read-only fallback is replaced once CodeMirror loads, so wait for the editor before typing.
    await expect(stageDefault.locator('.cm-editor')).toBeVisible()
    await stageDefault.getByRole('textbox').fill("'open'")

    await addField(page, 2, 'count', 'integer')
    await page.getByTestId('studio-field-/fields/2/min').getByRole('spinbutton').fill('0')

    await addField(page, 3, 'code', 'text')
    await page.getByTestId('studio-field-/fields/3/searchable').getByRole('switch', { name: 'Searchable', exact: true }).click()

    await page.getByRole('button', { name: 'Add index', exact: true }).click()
    await pickOptions(page.getByTestId('studio-field-/indexes/0/fields').getByRole('combobox'), ['stage', 'count'])
    await page.getByTestId('studio-field-/indexes/0/unique').getByRole('checkbox', { name: 'Unique index', exact: true }).check()

    const diagnostics = await save(page)
    expect(diagnostics.filter((diagnostic) => diagnostic.file === 'entities/backlog.json')).toEqual([])
    const backlog = JSON.parse(await readFile(path.join(copy, 'entities', 'backlog.json'), 'utf8')) as {
      fields: Record<string, unknown>[]
      indexes: unknown[]
    }
    expect(backlog.fields).toEqual([
      { name: 'name', type: 'text' },
      { name: 'stage', type: 'enum', values: ['open', 'closed'], default: "'open'" },
      { name: 'count', type: 'integer', min: 0 },
      { name: 'code', type: 'text', searchable: true },
    ])
    expect(backlog.indexes).toEqual([{ fields: ['stage', 'count'], unique: true }])
  })

  test('an author creates a second oidc identity provider and lets the Purchasing site allow it', async ({ page }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await createResource(page, 'identityProvider', 'Partner')
    await openFormTab(page)
    await expect(page.getByTestId('studio-resource-details')).toContainText('identity-providers/partner.json')
    await pick(page, 'studio-field-/type', 'oidc')
    await page.getByRole('textbox', { name: 'Issuer (URL binding)', exact: true }).fill('PartnerIssuer')
    await page.getByRole('textbox', { name: 'Client id (Secret binding)', exact: true }).fill('PartnerClientId')
    await page.getByRole('textbox', { name: 'Client secret (Secret binding)', exact: true }).fill('PartnerClientSecret')
    const label = page.getByTestId('studio-field-/label')
    await label.getByRole('textbox', { name: 'en', exact: true }).fill('Continue with Partner')
    await label.getByRole('button', { name: 'Write texts', exact: true }).click()
    await expect(label.getByTestId('studio-label-written')).toBeVisible()
    await page.getByRole('button', { name: 'Add role', exact: true }).click()
    const row = page.getByTestId('studio-role-row-0')
    await row.getByRole('textbox').fill('managers')
    await pickOption(row.getByRole('combobox'), 'department-manager')
    const providerDiagnostics = await save(page)

    await page.getByTestId('studio-group-site').getByRole('button', { name: 'Purchasing', exact: true }).click()
    await openFormTab(page)
    await page.getByRole('button', { name: 'Add provider', exact: true }).click()
    await pickOption(page.getByTestId('studio-field-/signIn/2').getByRole('combobox'), 'Partner')
    const siteDiagnostics = await save(page)

    const own = [...providerDiagnostics, ...siteDiagnostics].filter(
      (diagnostic) =>
        diagnostic.file.includes('identity-providers/partner.json') || diagnostic.file.includes('sites/purchasing.json'),
    )
    expect(own).toEqual([])
    // The starter of the page that an earlier journey created has an empty label, so the panel is checked for the two
    // files of this journey and for the codes that only a provider or a sign-in list raises.
    const panel = page.getByTestId('studio-diagnostics')
    for (const file of ['identity-providers/partner.json', 'sites/purchasing.json']) {
      await expect(panel.getByRole('button').filter({ hasText: file })).toHaveCount(0)
    }
    for (const code of ['AXC0285', 'AXC0288', 'AXC0289']) {
      await expect(panel).not.toContainText(code)
    }
    const saved = JSON.parse(await readFile(path.join(copy, 'identity-providers', 'partner.json'), 'utf8')) as Record<
      string,
      unknown
    >
    expect(saved).toMatchObject({
      type: 'oidc',
      issuer: { binding: 'PartnerIssuer' },
      clientId: { binding: 'PartnerClientId' },
      clientSecret: { binding: 'PartnerClientSecret' },
      roles: { values: { managers: 'department-manager' } },
    })
    const site = JSON.parse(await readFile(path.join(copy, 'sites', 'purchasing.json'), 'utf8')) as { signIn: string[] }
    expect(site.signIn).toEqual(['accounts', 'company', 'Partner'])
  })

  test('an author builds an oauth2 connector with path, query, nested and source controls in the Form tab', async ({
    page,
  }) => {
    const fixture = JSON.parse(
      await readFile(path.join(import.meta.dirname, '..', 'fixtures', 'studio-connector-oauth2.json'), 'utf8'),
    ) as Record<string, unknown>
    const { id: _fixtureId, ...expected } = fixture
    const textbox = (name: string) => page.getByRole('textbox', { name, exact: true })
    const row = (index: number) => page.getByTestId(`studio-field-/operations/0/input/${index}`)
    const nested = (index: number) => page.getByTestId(`studio-field-/operations/0/input/2/fields/${index}`)

    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    await createResource(page, 'connector', 'WarehouseShipping')
    await openFormTab(page)
    await expect(page.getByTestId('studio-resource-details')).toContainText('connectors/warehouse-shipping.json')

    await textbox('URL binding').fill('ShippingUrl')
    await pick(page, 'studio-field-/auth/type', 'oauth2')
    await textbox('Token URL (URL binding)').fill('ShippingTokenUrl')
    await textbox('Client id (Secret binding)').fill('ShippingClientId')
    await textbox('Client secret (Secret binding)').fill('ShippingClientSecret')
    await textbox('Scope').fill('shipments.write')
    await expect(textbox('Audience')).toHaveValue('')

    await textbox('Operation name').fill('createShipment')
    await pick(page, 'studio-field-/operations/0/method', 'POST')
    await textbox('Path').fill('/warehouses/{warehouse}/shipments')

    for (let index = 0; index < 3; index++) {
      await page.getByRole('button', { name: 'Add input field', exact: true }).click()
    }
    await row(0).getByRole('textbox', { name: 'Input name', exact: true }).fill('warehouse')
    await pick(page, 'studio-field-/operations/0/input/0/in', 'path')
    await row(0).getByRole('checkbox', { name: 'Required', exact: true }).check()

    await row(1).getByRole('textbox', { name: 'Input name', exact: true }).fill('dryRun')
    await pick(page, 'studio-field-/operations/0/input/1/type', 'boolean')
    await pick(page, 'studio-field-/operations/0/input/1/in', 'query')

    await row(2).getByRole('textbox', { name: 'Input name', exact: true }).fill('lines')
    await pick(page, 'studio-field-/operations/0/input/2/type', 'list')
    await expect(page.getByTestId('studio-field-/operations/0/input/2/in')).toHaveCount(0)
    await nested(0).getByRole('textbox', { name: 'Field name', exact: true }).fill('sku')
    await nested(0).getByRole('checkbox', { name: 'Required', exact: true }).check()
    await row(2).getByRole('button', { name: 'Add nested field', exact: true }).click()
    await nested(1).getByRole('textbox', { name: 'Field name', exact: true }).fill('quantity')
    await pick(page, 'studio-field-/operations/0/input/2/fields/1/type', 'integer')
    await nested(1).getByRole('checkbox', { name: 'Required', exact: true }).check()

    await page.getByRole('button', { name: 'Add output field', exact: true }).click()
    await textbox('Output name').fill('trackingNumber')
    await textbox('Output source').fill('/shipment/tracking/number')

    const diagnostics = await save(page)
    expect(diagnostics.filter((diagnostic) => diagnostic.file.includes('connectors/warehouse-shipping.json'))).toEqual([])
    const saved = JSON.parse(await readFile(path.join(copy, 'connectors', 'warehouse-shipping.json'), 'utf8')) as Record<
      string,
      unknown
    >
    const { id: _savedId, ...savedWithoutId } = saved
    expect(savedWithoutId).toEqual(expected)
  })

  test('a click on the disabled Create button fails within the action timeout and names the button', async ({ page }) => {
    await useSession(page, 'author')
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
