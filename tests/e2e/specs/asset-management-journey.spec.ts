import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import type { Locator, Page } from '@playwright/test'
import { confirmDialog, waitForDialog } from '../dialogs'
import { dragTo, waitForCanvas } from '../design-drag'
import { compareApplicationFolders } from '../folder-comparison'
import { pickAction, pickOption, pickOptions } from '../select'
import { assetManagement, journeyApplications, journeyURL } from '../server-settings'
import { csrfHeaders } from '../csrf'
import { cellValue, expectShown, fieldLabel } from '../fields'
import { useJourneySession } from '../sessions'
import { createResource, openFormTab, openTab } from '../studio-resources'

// Typing the sample one key at a time is slow, and the journey server is shared with other specs.
test.setTimeout(300_000)

const built = path.join(journeyApplications, 'asset-management')

interface SampleDataSource {
  entity: string
  fields: { name: string; path: string }[]
  filter?: string
  aggregate?: { groupBy: string[]; measures: { name: string; function: string; field?: string }[] }
}

interface SampleScenario {
  name: string
  now: string
  users: Record<string, string[]>
  steps: Record<string, unknown>[]
}

interface SampleForm {
  entity: string
  sections: { name: string; title: { textKey: string }; fields: { field: string; width?: number }[] }[]
}

const policies = [
  'policies/asset-manager-access.json',
  'policies/employee-access.json',
  'policies/auditor-access.json',
]

const scenarios = [
  ['AssetIsRegistered', 'scenarios/asset-is-registered.json'],
  ['EmployeeCannotWriteAnAsset', 'scenarios/employee-cannot-write-an-asset.json'],
  ['AuditorCannotWrite', 'scenarios/auditor-cannot-write.json'],
  ['NegativeCostIsRefused', 'scenarios/negative-cost-is-refused.json'],
  ['UserWithoutARoleIsDenied', 'scenarios/user-without-a-role-is-denied.json'],
] as const

const jsonScenarios = [['AssetWithMaintenanceAndSoftware', 'scenarios/asset-with-maintenance-and-software.json']] as const
const allScenarios = [...scenarios, ...jsonScenarios]

const sixParts = (file: string) => file === 'application.json' || /^(roles|sequences|entities|seeds|texts)\//.test(file)

test.describe('the author builds the asset management application with Studio only', () => {
  // Each step builds on the folder the one before created, so they run in order.
  test.describe.configure({ mode: 'serial' })

  // The Form tab stays mounted while the Design tab shows, and repeats its test ids. A Design step passes the design
  // panel as `scope`, so that only the panel's field matches.
  async function pick(scope: Page | Locator, testId: string, option: string, filter?: string) {
    await pickOption(scope.getByTestId(testId).getByRole('combobox'), option, filter)
  }

  async function save(page: Page, answers: [field: string, choice: string][] = []) {
    // A save with a migration posts one changes call, and a save without one patches the resource.
    const answer = page.waitForResponse(
      (response) => {
        const url = response.url()
        const method = response.request().method()
        return (
          (method === 'PATCH' && url.includes('/api/studio/apps/AssetManagement/resources/')) ||
          (method === 'POST' && url.endsWith('/api/studio/apps/AssetManagement/changes'))
        )
      },
      { timeout: 60_000 },
    )
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    for (const [field, choice] of answers) {
      // A click while the dialog zooms in is lost, so wait until it has finished opening.
      await waitForDialog(page)
      await expect(page.getByTestId('studio-migration-field')).toHaveText(field)
      // A click during the dialog's zoom-in is lost, so each answer waits for its dialog to open.
      const dialog = await waitForDialog(page)
      await dialog.getByRole('button', { name: choice, exact: true }).click()
    }
    await expect(page.getByTestId('studio-saved')).toBeVisible()
    const { diagnostics } = (await (await answer).json()) as {
      diagnostics: { file: string; severity: string }[]
    }
    return diagnostics
  }

  async function expectForm(page: Page) {
    await expect(page.getByRole('tab', { name: 'Form', exact: true })).toHaveAttribute('aria-selected', 'true')
  }

  async function writeLabel(scope: Page | Locator, field: string, key: string, en: string) {
    const label = scope.getByTestId(field).getByTestId('studio-label')
    const keyBox = label.getByRole('textbox', { name: 'Text key', exact: true })
    const english = label.getByRole('textbox', { name: 'en', exact: true })
    // The label editor fills the texts when the key is typed, from the names it has then. An editor that was replaced
    // or got its names later starts over, so the key is typed again until the text shows.
    await expect(async () => {
      await keyBox.fill('')
      await keyBox.fill(key)
      await expect(english).toHaveValue(en, { timeout: 2_000 })
    }).toPass({ timeout: 20_000 })
    await label.getByRole('button', { name: 'Write texts', exact: true }).click()
    await expect(label.getByTestId('studio-label-written')).toBeVisible()
  }

  async function grantIn(
    page: Page,
    role: string,
    kind: string,
    target: string,
    grants: { actions: string[]; filter?: string }[],
  ) {
    const cell = page.getByTestId(`studio-matrix-cell-${role}-${kind}-${target}`)
    await cell.click()
    for (const [index, grant] of grants.entries()) {
      await page.getByRole('button', { name: 'Add grant', exact: true }).click()
      const own = page.getByTestId('studio-matrix-own-grant').nth(index)
      for (const action of grant.actions) {
        // The matrix ticks the box a moment after the click, which check() would report as no change.
        const box = own.getByTestId('studio-matrix-actions').getByRole('checkbox', { name: action, exact: true })
        await box.click()
        await expect(box).toBeChecked()
      }
      if (grant.filter !== undefined) {
        await expect(own.locator('.cm-editor')).toBeVisible()
        await own.getByRole('textbox', { name: 'Record filter', exact: true }).fill(grant.filter)
      }
    }
    await page.getByTestId('studio-matrix-own-grants').getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-matrix-saved')).toBeVisible()
    // The matrix loads again after a save. A cell clicked before that could lose its new draft.
    for (const action of grants.flatMap((grant) => grant.actions)) {
      await expect(cell).toContainText(action)
    }
  }

  async function writeScenarioStep(page: Page, index: number, step: Record<string, unknown>, enumFields: Set<string>) {
    const at = `studio-field-/steps/${index}`
    await pick(page, `${at}/type`, String(step.type))
    for (const key of ['entity', 'record', 'as'] as const) {
      if (key in step) {
        await pick(page, `${at}/${key}`, String(step[key]))
      }
    }
    if ('save' in step) {
      await page.getByTestId(`${at}/save`).getByRole('textbox').fill(String(step.save))
    }
    if ('values' in step) {
      for (const [key, value] of Object.entries(step.values as Record<string, unknown>)) {
        await pickAction(
          page.getByTestId(`${at}/values`).getByRole('combobox', { name: 'Add value', exact: true }),
          key,
          page.getByTestId(`${at}/values/${key}`),
        )
        if (enumFields.has(key)) {
          await pick(page, `${at}/values/${key}`, String(value))
        } else {
          // A money box parses its text as a number, so the text of 1500 is `1500`.
          await page.getByTestId(`${at}/values/${key}`).getByRole('textbox').fill(String(value))
        }
      }
    }
    if ('expect' in step) {
      const expected = step.expect
      if (typeof expected === 'string') {
        await pick(page, `${at}/expect`, expected)
      } else {
        await pick(page, `${at}/expect`, 'invalid')
        await page
          .getByTestId(`${at}/expect`)
          .getByRole('textbox', { name: 'invalid', exact: true })
          .fill((expected as { invalid: string }).invalid)
      }
    }
  }

  async function texts(locale: string) {
    const { texts } = JSON.parse(await readFile(path.join(assetManagement, 'texts', `${locale}.json`), 'utf8')) as {
      texts: Record<string, string>
    }
    return texts
  }

  async function sample<T>(file: string): Promise<T> {
    return JSON.parse(await readFile(path.join(assetManagement, file), 'utf8')) as T
  }

  async function createPage(page: Page, name: string) {
    await createResource(page, 'page', name)
    await openFormTab(page)
    await expect(page.getByTestId('studio-page-editor').getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(name)
  }

  async function openPage(page: Page, name: string) {
    await page.getByTestId('studio-group-page').getByRole('button', { name, exact: true }).click()
    await openFormTab(page)
    await expect(page.getByTestId('studio-page-editor').getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(name)
  }

  // The dialog fades out after a choice, and a drag that starts under it does not reach the canvas. The canvas then
  // redraws, and a click during the redraw can miss.
  async function chooseBinding(page: Page, type: string) {
    await waitForDialog(page)
    await page.getByTestId(`studio-binding-${type}`).click()
    await expect(page.getByTestId('studio-binding-choice')).toBeHidden()
    await waitForCanvas(page)
  }

  async function openDesign(page: Page) {
    await page.getByRole('tab', { name: 'Design', exact: true }).click()
  }

  /** The canvas frame of the widget at `pointer`. */
  function frame(page: Page, pointer: string) {
    return page.locator(`[data-testid="studio-canvas-widget"][data-path="${pointer}"]`)
  }

  async function addField(page: Page, index: number, name: string) {
    await page.getByRole('button', { name: 'Add field', exact: true }).click()
    await page.getByTestId(`studio-field-/fields/${index}/name`).getByRole('textbox').fill(name)
  }

  async function addTags(page: Page, testId: string, values: string[]) {
    const input = page.getByTestId(testId).getByRole('combobox')
    for (const value of values) {
      await input.fill(value)
      await input.press('Enter')
    }
    await input.press('Escape')
  }

  async function chooseFieldCurrency(page: Page, index: number) {
    await page
      .getByTestId(`studio-field-/fields/${index}`)
      .getByRole('radiogroup', { name: 'Currency source', exact: true })
      .getByText('Field', { exact: true })
      .click()
  }

  // Deleting the last migration removes its group from the tree, so the loop ends when the group has no buttons.
  async function deleteMigrations(page: Page) {
    const migrations = page.getByTestId('studio-group-migration')
    while ((await migrations.getByRole('button').count()) > 0) {
      const before = await migrations.getByRole('button').count()
      await migrations.getByRole('button').first().click()
      await expectForm(page)
      await page.getByRole('button', { name: 'Delete', exact: true }).click()
      await confirmDialog(page, 'Delete')
      await expect(migrations.getByRole('button')).toHaveCount(before - 1)
    }
  }

  async function buildDataSource(page: Page, name: string, file: string) {
    const list = await sample<SampleDataSource>(file)
    await createResource(page, 'dataSource', name)
    await expectForm(page)
    await pick(page, 'studio-field-/entity', list.entity)
    // The starter has the first row, so only the later rows are added.
    for (const [index, field] of list.fields.entries()) {
      if (index > 0) {
        await page.getByTestId('studio-field-/fields').getByRole('button', { name: 'Add', exact: true }).click()
      }
      await page.getByTestId(`studio-field-/fields/${index}/name`).getByRole('textbox').fill(field.name)
      await page.getByTestId(`studio-field-/fields/${index}/path`).getByRole('textbox').fill(field.path)
    }
    if (list.filter !== undefined) {
      const filter = page.getByTestId('studio-field-/filter')
      await expect(filter.locator('.cm-editor')).toBeVisible()
      await filter.getByRole('textbox').fill(list.filter)
    }
    if (list.aggregate !== undefined) {
      const groupBy = page.getByTestId('studio-field-/aggregate/groupBy')
      for (const [index, column] of list.aggregate.groupBy.entries()) {
        await groupBy.getByRole('button', { name: 'Add', exact: true }).click()
        await page.getByTestId(`studio-field-/aggregate/groupBy/${index}`).getByRole('textbox').fill(column)
      }
      if (list.aggregate.groupBy.length === 0) {
        // The form has no empty array to start from, so the author adds an entry and removes it to write `[]`.
        await groupBy.getByRole('button', { name: 'Add', exact: true }).click()
        await groupBy.getByRole('button', { name: 'Remove', exact: true }).click()
      }
      for (const [index, measure] of list.aggregate.measures.entries()) {
        const at = `studio-field-/aggregate/measures/${index}`
        await page
          .getByTestId('studio-field-/aggregate/measures')
          .getByRole('button', { name: 'Add', exact: true })
          .click()
        await page.getByTestId(`${at}/name`).getByRole('textbox').fill(measure.name)
        await pick(page, `${at}/function`, measure.function)
        if (measure.field !== undefined) {
          await page.getByTestId(`${at}/field`).getByRole('textbox').fill(measure.field)
        }
      }
    }
    await save(page)
    await expectForm(page)
  }

  test('the author creates AssetManagement as a blank application', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('AssetManagement')
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(/app=AssetManagement$/)
  })

  test('the author builds the en and vi texts in the text editor', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const starter = page.getByTestId('studio-group-text')
    await starter.getByRole('button').click()
    await expectForm(page)

    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmDialog(page, 'Delete')
    await expect(starter.getByRole('button')).toHaveCount(0)

    await createResource(page, 'text', 'en', 'TextsEn')
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/en.json')
    await expectForm(page)

    await createResource(page, 'text', 'vi', 'TextsVi')
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/vi.json')
    await expectForm(page)

    const english = await texts('en')
    const vietnamese = await texts('vi')

    const add = page.getByTestId('studio-text-add')
    for (const [key, text] of Object.entries(english)) {
      await add.getByRole('textbox', { name: 'New key', exact: true }).fill(key)
      await add.getByRole('textbox', { name: 'en', exact: true }).fill(text)
      await add.getByRole('textbox', { name: 'vi', exact: true }).fill(vietnamese[key])
      // Ant Design adds "loading" to the name while the button is busy.
      await add.getByRole('button', { name: /^(loading )?Add key$/ }).click()
      await expect(add.getByRole('textbox', { name: 'New key', exact: true })).toHaveValue('')
      await expectForm(page)
    }
  })

  test('the author adds the three roles', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    for (const [name, key] of [
      ['asset-manager', 'role.assetManager'],
      ['employee', 'role.employee'],
      ['auditor', 'role.auditor'],
    ]) {
      await createResource(page, 'role', name)
      // The editor of the previous role stays on screen until the new role replaces it, so wait for the file.
      await expect(page.getByTestId('studio-resource-details')).toContainText(`roles/${name}.json`)
      await expectForm(page)
      await writeLabel(page, 'studio-field-/label', key, en[key])
      await save(page)
      await expectForm(page)
    }
  })

  test('the author adds the AssetNumber sequence', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    await createResource(page, 'sequence', 'AssetNumber')
    await expectForm(page)
    await page.getByTestId('studio-field-/format').getByRole('textbox').fill('AS-{n:6}')
    await save(page)
    await expectForm(page)
  })

  // Each name entity has one field, the starter `name`. Its maxLength asks a convert, which is skipped, and its
  // required asks a fill, which leaves the nulls.
  for (const [name, camel] of [
    ['Category', 'category'],
    ['Location', 'location'],
    ['Vendor', 'vendor'],
    ['Software', 'software'],
  ] as const) {
    test(`the author builds the ${name} entity in the entity designer`, async ({ page }) => {
      await useJourneySession(page, 'author')
      await page.goto(`${journeyURL}/studio?app=AssetManagement`)

      const en = await texts('en')
      await createResource(page, 'entity', name)
      await expectForm(page)
      await writeLabel(page, 'studio-field-/label', `${camel}.label`, en[`${camel}.label`])

      await page.getByTestId('studio-field-/fields/0/required').getByRole('checkbox').check()
      await page.getByTestId('studio-field-/fields/0/maxLength').getByRole('spinbutton').fill('100')
      await pick(page, 'studio-field-/fields/0/label', `${camel}.name`, `${camel}.name`)

      await pick(page, 'studio-field-/displayField', 'name')
      await save(page, [
        ['name', 'Skip'],
        ['name', 'Leave nulls'],
      ])
      await deleteMigrations(page)
    })
  }

  test('the author builds the Attachment and MaintenanceEntry child entities', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')

    // Row 0 is the starter `name` field, which becomes the file. Renaming it with Drop data also skips the convert
    // question for its type change, and the migration is deleted, because the sample has none.
    await createResource(page, 'entity', 'Attachment')
    await expectForm(page)
    await writeLabel(page, 'studio-field-/label', 'attachment.label', en['attachment.label'])
    await page.getByTestId('studio-field-/fields/0/name').getByRole('textbox').fill('file')
    await pick(page, 'studio-field-/fields/0/type', 'file')
    await addTags(page, 'studio-field-/fields/0/accept', ['application/pdf', 'image/*'])
    await pick(page, 'studio-field-/fields/0/label', 'attachment.file', 'attachment.file')
    await addField(page, 1, 'note')
    await page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton').fill('500')
    await pick(page, 'studio-field-/fields/1/label', 'attachment.note', 'attachment.note')
    await save(page, [['name → file', 'Drop data']])
    await deleteMigrations(page)

    // Row 0 is the starter `name` field, which becomes the required date. The rename with Drop data skips the convert,
    // the new required date asks a fill, and the new required currency asks a fill too.
    await createResource(page, 'entity', 'MaintenanceEntry')
    await expectForm(page)
    await writeLabel(page, 'studio-field-/label', 'maintenance.label', en['maintenance.label'])
    await page.getByTestId('studio-field-/fields/0/name').getByRole('textbox').fill('date')
    await pick(page, 'studio-field-/fields/0/type', 'date')
    await page.getByTestId('studio-field-/fields/0/required').getByRole('checkbox').check()
    await pick(page, 'studio-field-/fields/0/label', 'maintenance.date', 'maintenance.date')

    await addField(page, 1, 'vendor')
    await pick(page, 'studio-field-/fields/1/type', 'reference')
    await pick(page, 'studio-field-/fields/1/target', 'Vendor')
    await pick(page, 'studio-field-/fields/1/label', 'maintenance.vendor', 'maintenance.vendor')

    await addField(page, 2, 'currency')
    await pick(page, 'studio-field-/fields/2/type', 'enum')
    await addTags(page, 'studio-field-/fields/2/values', ['VND', 'USD', 'EUR'])
    await page.getByTestId('studio-field-/fields/2/required').getByRole('checkbox').check()
    await pick(page, 'studio-field-/fields/2/label', 'maintenance.currency', 'maintenance.currency')

    await addField(page, 3, 'cost')
    await pick(page, 'studio-field-/fields/3/type', 'money')
    await chooseFieldCurrency(page, 3)
    await pick(page, 'studio-field-/fields/3/currencyField', 'currency')
    await page.getByTestId('studio-field-/fields/3/min').getByRole('spinbutton').fill('0')
    await pick(page, 'studio-field-/fields/3/label', 'maintenance.cost', 'maintenance.cost')

    await addField(page, 4, 'note')
    await page.getByTestId('studio-field-/fields/4/maxLength').getByRole('spinbutton').fill('500')
    await pick(page, 'studio-field-/fields/4/label', 'maintenance.note', 'maintenance.note')

    await save(page, [
      ['name → date', 'Drop data'],
      ['date', 'Leave nulls'],
      ['currency', 'Leave nulls'],
    ])
    await deleteMigrations(page)
  })

  test('the author builds the Asset entity in the entity designer', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    await createResource(page, 'entity', 'Asset')
    await expectForm(page)
    await writeLabel(page, 'studio-field-/label', 'asset.label', en['asset.label'])

    // Row 0 is the starter `name` field, which stays `name`. The sample orders `number` before it, so the author adds
    // `number` below it and moves it up. Its sequence is AssetNumber.
    await page.getByTestId('studio-field-/fields/0/required').getByRole('checkbox').check()
    await page.getByTestId('studio-field-/fields/0/maxLength').getByRole('spinbutton').fill('200')
    await page.getByTestId('studio-field-/fields/0/searchable').getByRole('switch').click()
    await pick(page, 'studio-field-/fields/0/label', 'asset.name', 'asset.name')

    await addField(page, 1, 'number')
    await page.getByTestId('studio-field-/fields/1/unique').getByRole('checkbox').check()
    await page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton').fill('20')
    await pick(page, 'studio-field-/fields/1/sequence', 'AssetNumber')
    await pick(page, 'studio-field-/fields/1/label', 'asset.number', 'asset.number')
    await page.getByTestId('studio-field-/fields/1').getByRole('button', { name: 'Move up', exact: true }).click()

    await addField(page, 2, 'category')
    await pick(page, 'studio-field-/fields/2/type', 'reference')
    await pick(page, 'studio-field-/fields/2/target', 'Category')
    await pick(page, 'studio-field-/fields/2/label', 'asset.category', 'asset.category')

    await addField(page, 3, 'status')
    await pick(page, 'studio-field-/fields/3/type', 'enum')
    await addTags(page, 'studio-field-/fields/3/values', ['in-stock', 'assigned', 'in-repair', 'retired'])
    await page.getByTestId('studio-field-/fields/3/required').getByRole('checkbox').check()
    for (const value of ['in-stock', 'assigned', 'in-repair', 'retired']) {
      await pick(page, `studio-field-/fields/3/valueLabels/${value}`, `asset.status.${value}`, `asset.status.${value}`)
    }
    const status = page.getByTestId('studio-field-/fields/3/default')
    await expect(status.locator('.cm-editor')).toBeVisible()
    await status.getByRole('textbox').fill("'in-stock'")
    await pick(page, 'studio-field-/fields/3/label', 'asset.status', 'asset.status')

    await addField(page, 4, 'location')
    await pick(page, 'studio-field-/fields/4/type', 'reference')
    await pick(page, 'studio-field-/fields/4/target', 'Location')
    await pick(page, 'studio-field-/fields/4/label', 'asset.location', 'asset.location')

    await addField(page, 5, 'holder')
    await page.getByTestId('studio-field-/fields/5/maxLength').getByRole('spinbutton').fill('100')
    await pick(page, 'studio-field-/fields/5/label', 'asset.holder', 'asset.holder')

    await addField(page, 6, 'currency')
    await pick(page, 'studio-field-/fields/6/type', 'enum')
    await addTags(page, 'studio-field-/fields/6/values', ['VND', 'USD', 'EUR'])
    await page.getByTestId('studio-field-/fields/6/required').getByRole('checkbox').check()
    const currency = page.getByTestId('studio-field-/fields/6/default')
    await expect(currency.locator('.cm-editor')).toBeVisible()
    await currency.getByRole('textbox').fill("'VND'")
    await pick(page, 'studio-field-/fields/6/label', 'asset.currency', 'asset.currency')

    await addField(page, 7, 'purchaseCost')
    await pick(page, 'studio-field-/fields/7/type', 'money')
    await chooseFieldCurrency(page, 7)
    await pick(page, 'studio-field-/fields/7/currencyField', 'currency')
    await page.getByTestId('studio-field-/fields/7/min').getByRole('spinbutton').fill('0')
    await pick(page, 'studio-field-/fields/7/label', 'asset.purchaseCost', 'asset.purchaseCost')

    await addField(page, 8, 'purchaseDate')
    await pick(page, 'studio-field-/fields/8/type', 'date')
    await pick(page, 'studio-field-/fields/8/label', 'asset.purchaseDate', 'asset.purchaseDate')

    await addField(page, 9, 'warrantyEnd')
    await pick(page, 'studio-field-/fields/9/type', 'date')
    await pick(page, 'studio-field-/fields/9/label', 'asset.warrantyEnd', 'asset.warrantyEnd')
    await pick(page, 'studio-field-/fields/9/help', 'asset.warrantyEnd.help', 'asset.warrantyEnd.help')

    await addField(page, 10, 'attachments')
    await pick(page, 'studio-field-/fields/10/type', 'child-collection')
    await pick(page, 'studio-field-/fields/10/target', 'Attachment')
    await pick(page, 'studio-field-/fields/10/label', 'asset.attachments', 'asset.attachments')

    await addField(page, 11, 'maintenance')
    await pick(page, 'studio-field-/fields/11/type', 'child-collection')
    await pick(page, 'studio-field-/fields/11/target', 'MaintenanceEntry')
    await pick(page, 'studio-field-/fields/11/label', 'asset.maintenance', 'asset.maintenance')

    await addField(page, 12, 'software')
    await pick(page, 'studio-field-/fields/12/type', 'references')
    await pick(page, 'studio-field-/fields/12/target', 'Software')
    await pick(page, 'studio-field-/fields/12/label', 'asset.software', 'asset.software')

    await pick(page, 'studio-field-/displayField', 'name')
    await save(page, [
      ['name', 'Skip'],
      ['name', 'Leave nulls'],
      ['status', 'Leave nulls'],
      ['currency', 'Leave nulls'],
    ])
    await deleteMigrations(page)
  })

  test('the author adds the Categories and Locations seeds in the seed grid', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    for (const [name, entity, records] of [
      ['Categories', 'Category', ['Laptop', 'Monitor', 'Phone', 'Furniture']],
      ['Locations', 'Location', ['Hanoi office', 'Ho Chi Minh City office', 'Da Nang office']],
    ] as const) {
      await createResource(page, 'seed', name)
      await expectForm(page)
      await pick(page, 'studio-field-/entity', entity)
      await page.getByTestId('studio-field-/sync').getByRole('checkbox').check()
      await expect(page.getByTestId('studio-seed-grid')).toBeVisible()

      const addRecord = page.getByRole('button', { name: 'Add record', exact: true })
      for (let index = 0; index < records.length; index++) {
        await addRecord.click()
      }
      await expect(page.getByTestId('studio-seed-row')).toHaveCount(records.length)
      for (const [index, record] of records.entries()) {
        await page
          .getByTestId('studio-seed-row')
          .nth(index)
          .getByRole('textbox', { name: 'name', exact: true })
          .fill(record)
      }
      await save(page)
      await expectForm(page)
    }
  })

  test('the author sets the application time zone, label and description', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    await page.getByTestId('studio-group-application').getByRole('button').click()
    await expectForm(page)
    await page.getByTestId('studio-field-/timeZone').getByRole('textbox').fill('Asia/Ho_Chi_Minh')
    await writeLabel(page, 'studio-field-/label', 'application.title', en['application.title'])
    await writeLabel(page, 'studio-field-/description', 'application.description', en['application.description'])
    await save(page)
  })

  test('the author builds the three aggregate data sources', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    for (const [name, file] of [
      ['AssetValueByLocation', 'data-sources/asset-value-by-location.json'],
      ['AssetsByStatus', 'data-sources/assets-by-status.json'],
      ['WarrantiesEndingSoon', 'data-sources/warranties-ending-soon.json'],
    ]) {
      await buildDataSource(page, name, file)
    }
  })

  test('the author lays out the AssetEdit form on the Design tab', async ({ page }) => {
    // A tall window keeps the palette and the outline in view together, away from the edges where a drag scrolls.
    await page.setViewportSize({ width: 1280, height: 1000 })
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    const form = await sample<SampleForm>('forms/asset-edit.json')
    await createResource(page, 'form', 'AssetEdit')
    await openFormTab(page)
    // The Design pane has no editor for the entity, so it is picked on the Form tab.
    await pick(page, 'studio-field-/entity', form.entity)
    await openDesign(page)
    const panel = page.getByTestId('studio-design-panel')
    await expect(panel.getByTestId('studio-form-palette-number')).toBeVisible()

    const sectionTitle = (index: number) =>
      panel.locator(`[data-testid="studio-outline-section"][data-path="/sections/${index}"] > span`).first()
    const outlineRow = (testId: string, pointer: string) =>
      panel.locator(`[data-testid="${testId}"][data-path="${pointer}"]`)

    // A section gets its name and title, then its fields. The first field is dragged from the palette onto the section's
    // line in the outline. The others are clicked, and each lands after the field selected before it.
    async function layOutSection(index: number) {
      const section = form.sections[index]
      await sectionTitle(index).click()
      const name = panel.getByTestId('studio-design-section-name')
      await name.fill(section.name)
      await name.press('Enter')
      await expect(sectionTitle(index)).toHaveText(section.name)
      const titleKey = section.title.textKey
      await writeLabel(panel, `studio-field-/sections/${index}/title`, titleKey, en[titleKey])
      for (const [position, field] of section.fields.entries()) {
        const button = panel.getByTestId(`studio-form-palette-${field.field}`)
        if (position === 0) {
          await dragTo(page, 'studio-form-design', button, sectionTitle(index))
        } else {
          await button.click()
        }
        await expect(outlineRow('studio-outline-field', `/sections/${index}/fields/${position}`)).toHaveAttribute(
          'aria-current',
          'true',
        )
        // The new field is the selected one, and the property panel sets its width on the row.
        if (field.width !== undefined) {
          await panel.getByRole('spinbutton', { name: 'Width in twelfths of the row', exact: true }).fill(String(field.width))
        }
      }
    }

    // The starter section holds one empty field, which the sample does not have. Its outline row has no name and no size
    // to click, so Enter selects it.
    await outlineRow('studio-outline-field', '/sections/0/fields/0').press('Enter')
    await panel.getByTestId('studio-design-remove-field').click()
    await expect(panel.getByTestId('studio-outline-empty-section')).toBeVisible()

    await layOutSection(0)
    for (let index = 1; index < form.sections.length; index++) {
      await panel.getByTestId('studio-form-add-section').click()
      await expect(sectionTitle(index)).toBeVisible()
      await layOutSection(index)
    }
    await save(page)
  })

  test('the author builds the AssetForm and AssetDetail pages', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    await createPage(page, 'AssetForm')
    await writeLabel(page, 'studio-field-/title', 'pages.assetForm.title', en['pages.assetForm.title'])
    await pick(page, 'studio-field-/widgets/0/type', 'form')
    await pick(page, 'studio-field-/widgets/0/form', 'AssetEdit')
    await save(page)

    // The detail page takes the asset as a required parameter. Its detail widget is laid out on the Design tab later,
    // because the canvas draws nothing while any file has an error. The starter Home page and its site entry have errors
    // until the author finishes the site.
    await createPage(page, 'AssetDetail')
    await writeLabel(page, 'studio-field-/title', 'pages.assetDetail.title', en['pages.assetDetail.title'])
    await page.getByRole('button', { name: 'Add parameter', exact: true }).click()
    await page.getByRole('textbox', { name: 'Parameter name', exact: true }).fill('asset')
    await pick(page, 'studio-field-/parameters/0/type', 'reference')
    await pick(page, 'studio-field-/parameters/0/target', 'Asset')
    await page.getByRole('switch', { name: 'Required', exact: true }).click()
    await save(page)
  })

  test('the author builds the Assets and Dashboard pages', async ({ page }) => {
    await useJourneySession(page, 'author')
    // The page names load with the application, so AssetForm and AssetDetail are offered below.
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    await createPage(page, 'Assets')
    await writeLabel(page, 'studio-field-/title', 'pages.assets.title', en['pages.assets.title'])
    await pick(page, 'studio-field-/widgets/0/type', 'table')
    await pick(page, 'studio-field-/widgets/0/entity', 'Asset')
    // The row actions open the asset in its form and show it on the detail page, and a toolbar action starts a new
    // one. The detail page gets the asset through its parameter.
    for (const [kind, index, target, key, parameter] of [
      ['rowActions', 0, 'AssetForm', 'actions.open', undefined],
      ['rowActions', 1, 'AssetDetail', 'actions.view', 'asset'],
      ['toolbarActions', 0, 'AssetForm', 'actions.new', undefined],
    ] as const) {
      const action = `studio-field-/widgets/0/${kind}/${index}`
      await page
        .getByTestId(`studio-field-/widgets/0/${kind}`)
        .getByRole('button', { name: 'Add navigate action', exact: true })
        .click()
      await pick(page, `${action}/page`, target)
      await writeLabel(page, `${action}/label`, key, en[key])
      if (kind === 'rowActions') {
        // The record of the form link and the asset parameter of the detail link both take the id of the row.
        const input = parameter ? `${action}/parameters/${parameter}` : `${action}/record`
        await page.getByTestId(input).getByRole('textbox').fill('id')
      }
    }
    await save(page)

    await createPage(page, 'Dashboard')
    await writeLabel(page, 'studio-field-/title', 'pages.dashboard.title', en['pages.dashboard.title'])
    await save(page)
  })

  test('the author finishes the site and deletes the Home page', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    await page.getByTestId('studio-group-site').getByRole('button').click()
    await expectForm(page)

    await page.getByTestId('studio-field-/locales/available').getByRole('button', { name: 'Add', exact: true }).click()
    await page.getByTestId('studio-field-/locales/available/1').getByRole('textbox').fill('vi')
    await writeLabel(page, 'studio-field-/description', 'site.description', en['site.description'])

    // The starter site has the entry for Home, which the author turns into the first entry.
    const navigation: [page: string, key: string, icon: string][] = [
      ['Assets', 'nav.assets', 'device'],
      ['Dashboard', 'nav.dashboard', 'dashboard'],
    ]
    for (const [index, [target, key, icon]] of navigation.entries()) {
      if (index > 0) {
        await page.getByTestId('studio-field-/navigation').getByRole('button', { name: 'Add', exact: true }).click()
      }
      await pick(page, `studio-field-/navigation/${index}/page`, target)
      await writeLabel(page, `studio-field-/navigation/${index}/label`, key, en[key])
      await pick(page, `studio-field-/navigation/${index}/icon`, icon)
    }
    await save(page)
    await expectForm(page)

    // The sample has no Home page, and nothing points to it now.
    const home = page.getByTestId('studio-group-page').getByRole('button', { name: 'Home', exact: true })
    await home.click()
    await openFormTab(page)
    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmDialog(page, 'Delete')
    await expect(page.getByTestId('studio-group-page').getByRole('button', { name: /Home/i })).toHaveCount(0)
  })

  test('the author lays out the AssetDetail page on the Design tab', async ({ page }) => {
    // A tall, wide window keeps the data panel and the canvas in view together, away from the edges where a drag scrolls.
    await page.setViewportSize({ width: 1920, height: 1000 })
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    await openPage(page, 'AssetDetail')
    // The Asset entity dropped on the starter widget binds a detail widget to the required asset parameter. The starter
    // task inbox is not in the sample, so the author removes it from the widget tree.
    await openDesign(page)
    const panel = page.getByTestId('studio-design-panel')
    const starter = frame(page, '/widgets/0')
    await expect(starter).toBeVisible()
    await dragTo(page, 'studio-page-design', panel.getByTestId('studio-data-entity-Asset'), starter, 0.75)
    await chooseBinding(page, 'detail')
    await expect(frame(page, '/widgets/1')).toBeVisible()
    await panel
      .getByTestId('studio-widget-row-/widgets/0')
      .getByRole('button', { name: 'Remove widget', exact: true })
      .click()
    await expect(panel.getByTestId('studio-widget-/widgets/1')).toHaveCount(0)
    await save(page)
  })

  test('the author lays out the Dashboard on the Design tab with a KPI, a chart and the value table', async ({ page }) => {
    // A tall, wide window keeps the palette, the data panel and the canvas in view together, away from the edges where a
    // drag scrolls. A table makes the canvas wider than a narrow window, which pushes the panel out of sight of the mouse.
    await page.setViewportSize({ width: 1920, height: 1000 })
    await useJourneySession(page, 'author')
    // The names load with the application, so the new data sources are offered below.
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    await openPage(page, 'Dashboard')
    await openDesign(page)
    const panel = page.getByTestId('studio-design-panel')
    const dragOnto = (source: string, target: Locator, fraction?: number) =>
      dragTo(page, 'studio-page-design', panel.getByTestId(source), target, fraction)
    const list = (pointer: string) => page.locator(`[data-testid="studio-canvas-list"][data-list="${pointer}"]`)
    await expect(frame(page, '/widgets/0')).toBeVisible()

    // The value table goes below the starter widget, and the columns widget above it. Then the author removes the
    // starter task inbox, which the sample does not have.
    await dragOnto('studio-data-dataSource-AssetValueByLocation', frame(page, '/widgets/0'), 0.75)
    await chooseBinding(page, 'table')
    await expect(frame(page, '/widgets/1')).toBeVisible()
    await dragOnto('studio-palette-columns', frame(page, '/widgets/0'), 0.25)
    await expect(panel.getByTestId('studio-widget-/widgets/2')).toBeVisible()
    await panel
      .getByTestId('studio-widget-row-/widgets/1')
      .getByRole('button', { name: 'Remove widget', exact: true })
      .click()
    await expect(panel.getByTestId('studio-widget-/widgets/2')).toHaveCount(0)
    await panel.getByTestId('studio-widget-/widgets/0').click()
    await panel.getByTestId('studio-field-/widgets/0/columns/0/width').getByRole('spinbutton').fill('4')
    await panel.getByTestId('studio-field-/widgets/0/columns/1/width').getByRole('spinbutton').fill('8')

    // The KPI is a data source dropped on the first column's list. The data source sets the measure, and the author
    // writes the label. The columns widget does not compile until its tile has a label and its second column has a
    // widget, so the canvas draws it as a placeholder with its lists, and the widgets are found in the widget tree.
    const kpi = '/widgets/0/columns/0/widgets/0'
    await dragOnto('studio-data-dataSource-WarrantiesEndingSoon', list('/widgets/0/columns/0/widgets'))
    await chooseBinding(page, 'kpi')
    await expect(panel.getByTestId(`studio-widget-${kpi}`)).toBeVisible()
    await writeLabel(panel, `studio-field-${kpi}/label`, 'dashboard.warranties.label', en['dashboard.warranties.label'])

    // The chart is a data source with one group field dropped in the second column. The binding picks the category and
    // the measure.
    const chart = '/widgets/0/columns/1/widgets/0'
    await dragOnto('studio-data-dataSource-AssetsByStatus', list('/widgets/0/columns/1/widgets'))
    await chooseBinding(page, 'chart')
    await expect(panel.getByTestId(`studio-widget-${chart}`)).toBeVisible()
    await writeLabel(
      panel,
      `studio-field-${chart}/measures/0/label`,
      'dashboard.byStatus.count',
      en['dashboard.byStatus.count'],
    )
    await writeLabel(panel, `studio-field-${chart}/title`, 'dashboard.byStatus.title', en['dashboard.byStatus.title'])
    await save(page)

    // After a reload the Design tab draws all three widgets.
    await page.reload()
    await openPage(page, 'Dashboard')
    await openDesign(page)
    await expect(frame(page, kpi)).toContainText('Warranties ending within 30 days')
    await expect(frame(page, chart)).toContainText('Assets by status')
    await expect(frame(page, '/widgets/1').getByRole('table')).toBeVisible()
  })

  test('the built folder matches the sample in its six parts', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const files = (await readdir(assetManagement, { recursive: true })).filter(sixParts).sort()
    expect((await readdir(built, { recursive: true })).filter(sixParts).sort()).toEqual(files)
    await expect(page.getByTestId('studio-group-migration').getByRole('button')).toHaveCount(0)
    compareApplicationFolders(assetManagement, built, { files, ignorePageElementNames: true, generatedNames: true })

    const response = await page.request.get(`${journeyURL}/api/studio/apps/AssetManagement/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { file: string; severity: string }[] }
    expect(
      diagnostics.filter((diagnostic) => diagnostic.severity === 'error' && files.includes(diagnostic.file)),
    ).toEqual([])
  })

  test('the built data sources, form, pages and site match the sample', async () => {
    compareApplicationFolders(assetManagement, built, {
      generatedNames: true,
      files: [
        'data-sources/asset-value-by-location.json',
        'data-sources/assets-by-status.json',
        'data-sources/warranties-ending-soon.json',
        'forms/asset-edit.json',
        'pages/asset-detail.json',
        'pages/asset-form.json',
        'pages/assets.json',
        'pages/dashboard.json',
        'sites/asset-management.json',
      ],
      ignorePageElementNames: true,
    })
    // The sample has no Home page.
    expect(await readdir(path.join(built, 'pages'))).not.toContain('home.json')
  })

  test('the author builds the three access policies in the policy matrix', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)
    await page.getByText('Policy matrix', { exact: true }).click()

    const entities = ['Asset', 'Category', 'Location', 'Vendor', 'Software']
    const dataSources = ['AssetValueByLocation', 'AssetsByStatus', 'WarrantiesEndingSoon']
    // The matrix appends each grant to the end of the policy, so the order here is the order of the sample.
    for (const entity of entities) {
      await grantIn(page, 'asset-manager', 'entity', entity, [
        { actions: ['read', 'create', 'update', 'delete', 'export', 'import'] },
      ])
    }
    for (const dataSource of dataSources) {
      await grantIn(page, 'asset-manager', 'dataSource', dataSource, [{ actions: ['read', 'export'] }])
    }

    for (const entity of entities) {
      await grantIn(
        page,
        'employee',
        'entity',
        entity,
        [entity === 'Asset' ? { actions: ['read'], filter: 'holder == currentUser()' } : { actions: ['read'] }],
      )
    }

    for (const entity of entities) {
      await grantIn(page, 'auditor', 'entity', entity, [{ actions: ['read', 'export'] }])
    }
    for (const dataSource of dataSources) {
      await grantIn(page, 'auditor', 'dataSource', dataSource, [{ actions: ['read', 'export'] }])
    }

    const cell = (role: string, entity: string) => page.getByTestId(`studio-matrix-cell-${role}-entity-${entity}`)
    await expect(cell('auditor', 'Asset')).toContainText('export')
    await expect(cell('auditor', 'Asset')).not.toContainText('import')
    await expect(cell('asset-manager', 'Asset')).toContainText('export')
    await expect(cell('asset-manager', 'Asset')).toContainText('import')
    for (const entity of entities) {
      await expect(cell('employee', entity)).not.toContainText('export')
      await expect(cell('employee', entity)).not.toContainText('import')
    }
  })

  for (const [name, file] of scenarios) {
    test(`the author writes the ${name} scenario in the scenario editor`, async ({ page }) => {
      await useJourneySession(page, 'author')
      await page.goto(`${journeyURL}/studio?app=AssetManagement`)

      await createResource(page, 'scenario', name)
      await expect(page.getByTestId('studio-resource-details')).toContainText(file)
      await expectForm(page)

      const scenario = await sample<SampleScenario>(file)
      const asset = await sample<{ fields: { name: string; type: string }[] }>('entities/asset.json')
      const enumFields = new Set(asset.fields.filter((field) => field.type === 'enum').map((field) => field.name))

      await page.getByTestId('studio-field-/now').getByRole('textbox').fill(scenario.now)
      for (const [index, [user, roles]] of Object.entries(scenario.users).entries()) {
        if (index > 0) {
          await page.getByRole('button', { name: 'Add user', exact: true }).click()
        }
        const newRow = page.getByTestId(`studio-field-/users/${index === 0 ? 'author' : 'user1'}`)
        const userId = newRow.getByRole('textbox', { name: 'User id', exact: true })
        await userId.fill(user)
        // The rename detaches this input, so Tab goes through the page keyboard rather than the locator.
        await page.keyboard.press('Tab')
        const row = page.getByTestId(`studio-field-/users/${user}`)
        await expect(row).toBeVisible()
        // A user without a role keeps the empty list the starter row has.
        if (roles.length > 0) {
          await pickOptions(row.getByRole('combobox', { name: 'Roles', exact: true }), roles)
          for (const role of roles) {
            await expect(row).toContainText(role)
          }
        }
      }

      for (const [index, step] of scenario.steps.entries()) {
        if (index > 0) {
          await page.getByRole('button', { name: 'Add step', exact: true }).click()
        }
        await writeScenarioStep(page, index, step, enumFields)
      }

      const diagnostics = await save(page)
      const errors = diagnostics.filter((diagnostic) => diagnostic.file === file && diagnostic.severity === 'error')
      expect(errors).toEqual([])
      await expectForm(page)
    })
  }

  for (const [name, file] of jsonScenarios) {
    test(`the author writes the ${name} scenario through the JSON view`, async ({ page }) => {
      await useJourneySession(page, 'author')
      await page.goto(`${journeyURL}/studio?app=AssetManagement`)

      await createResource(page, 'scenario', name)
      await expect(page.getByTestId('studio-resource-details')).toContainText(file)

      // The scenario editor cannot add a child-collection value, so the sample is pasted into the JSON view.
      await openTab(page, 'JSON')
      const json = page.getByRole('textbox', { name: 'JSON', exact: true })
      const content = await sample<Record<string, unknown>>(file)
      content.id = (JSON.parse(await json.inputValue()) as { id: string }).id
      await json.fill(JSON.stringify(content, null, 2))

      const diagnostics = await save(page)
      expect(diagnostics.filter((diagnostic) => diagnostic.file === file && diagnostic.severity === 'error')).toEqual([])
    })
  }

  test('the built policies and scenarios match the sample', async () => {
    compareApplicationFolders(assetManagement, built, {
      files: [...policies, ...allScenarios.map(([, file]) => file)],
    })
  })

  test("the author builds the warranty reminder's data source, process and schedule trigger", async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const en = await texts('en')
    const stepCard = (index: number) => page.getByTestId(`studio-step-${index}`)

    await buildDataSource(page, 'AssetsWithWarrantyEndingSoon', 'data-sources/assets-with-warranty-ending-soon.json')

    // A new trigger starts as an event trigger, and only picking an event and clearing it removes the key. The sample
    // has no event, so one exists until the trigger is built.
    await createResource(page, 'event', 'Placeholder')
    await openFormTab(page)
    await pick(page, 'studio-field-/entity', 'Asset')
    await save(page)

    async function dropEvent() {
      await pick(page, 'studio-field-/event', 'Placeholder')
      const field = page.getByTestId('studio-field-/event')
      await field.hover()
      await field.getByRole('img', { name: 'close-circle', exact: true }).click()
      await expect(field.getByTitle('Placeholder', { exact: true })).toHaveCount(0)
    }

    await createResource(page, 'process', 'WarrantyReminder')
    await openFormTab(page)
    // The subject of a new process is an entity, so only the entity is picked.
    await pick(page, 'studio-field-/entity', 'Asset')
    await stepCard(0).getByRole('button', { name: 'Remove step', exact: true }).click()
    for (const [index, [stepName, type]] of [
      ['remind', 'operation'],
      ['done', 'end'],
    ].entries()) {
      await pickAction(page.getByRole('combobox', { name: 'Add step', exact: true }), type, stepCard(index))
      await stepCard(index).getByRole('textbox', { name: 'Step name', exact: true }).fill(stepName)
    }

    await pick(page, 'studio-field-/steps/0/operation', 'notify')
    await page
      .getByTestId('studio-field-/steps/0/to')
      .getByRole('button', { name: 'Add recipient', exact: true })
      .click()
    await page
      .getByTestId('studio-field-/steps/0/to/0')
      .locator('label')
      .filter({ has: page.getByRole('radio', { name: 'Role', exact: true }) })
      .click()
    await pick(page, 'studio-field-/steps/0/to/0/role', 'asset-manager')
    await page
      .getByTestId('studio-field-/steps/0/channels')
      .getByRole('checkbox', { name: 'In-app', exact: true })
      .check()
    for (const [field, key] of [
      ['subject', 'asset.warrantyReminder.subject'],
      ['body', 'asset.warrantyReminder.body'],
    ]) {
      await writeLabel(page, `studio-field-/steps/0/message/${field}`, key, en[key])
    }
    const values = page.getByTestId('studio-field-/steps/0/message/values')
    for (const [placeholder, expression] of [
      ['name', 'name'],
      ['warrantyEnd', 'warrantyEnd'],
    ]) {
      await values.getByRole('button', { name: 'Add mapping', exact: true }).click()
      await values.getByRole('textbox', { name: 'Placeholder', exact: true }).last().fill(placeholder)
      const value = page.getByTestId(`studio-field-/steps/0/message/values/${placeholder}`)
      await expect(value.locator('.cm-editor')).toBeVisible()
      await value.getByRole('textbox', { name: 'Expression', exact: true }).fill(expression)
    }
    await page
      .getByTestId('studio-field-/steps/0/open')
      .getByRole('checkbox', { name: 'Open a page', exact: true })
      .check()
    await pick(page, 'studio-field-/steps/0/open/page', 'AssetDetail')
    // The page's parameters are offered by name, so the author picks `asset` and writes the expression.
    await pickAction(
      page
        .getByTestId('studio-field-/steps/0/open/parameters')
        .getByRole('combobox', { name: 'Add mapping Page parameters', exact: true }),
      'asset',
      page.getByTestId('studio-field-/steps/0/open/parameters/asset'),
    )
    const parameter = page.getByTestId('studio-field-/steps/0/open/parameters/asset')
    await expect(parameter.locator('.cm-editor')).toBeVisible()
    await parameter.getByRole('textbox', { name: 'Expression', exact: true }).fill('id')
    await pick(page, 'studio-field-/steps/0/next', 'done')
    await pick(page, 'studio-field-/start', 'remind')
    const diagnostics = await save(page)
    await expectForm(page)
    expect(
      diagnostics.filter(
        (diagnostic) => diagnostic.file === 'processes/warranty-reminder.json' && diagnostic.severity === 'error',
      ),
    ).toEqual([])

    await createResource(page, 'trigger', 'WarrantyReminderWeekdays')
    await openFormTab(page)
    await pick(page, 'studio-field-/type', 'schedule')
    await dropEvent()
    await page.getByTestId('studio-field-/schedule').getByRole('textbox').fill('0 8 * * 1-5')
    await page.getByTestId('studio-field-/timeZone').getByRole('textbox').fill('Asia/Ho_Chi_Minh')
    await pick(page, 'studio-field-/dataSource', 'AssetsWithWarrantyEndingSoon')
    await pick(page, 'studio-field-/process', 'WarrantyReminder')
    await save(page)

    await page.getByTestId('studio-group-event').getByRole('button', { name: 'Placeholder', exact: true }).click()
    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmDialog(page, 'Delete')
    await expect(page.getByTestId('studio-group-event')).toHaveCount(0)
  })

  test('the whole built folder matches the sample', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    compareApplicationFolders(assetManagement, built, { ignorePageElementNames: true, generatedNames: true })
    const response = await page.request.get(`${journeyURL}/api/studio/apps/AssetManagement/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  })

  test('the author runs the six test scenarios from Studio', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)
    await page.getByTestId('studio-run-tests').click()

    // Each scenario creates and drops a database, and the publisher queue is shared with other specs.
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 240_000,
    })
    await expect(page.getByTestId('studio-test-result')).toHaveCount(allScenarios.length)
    await expect(page.locator('[data-testid="studio-test-result"][data-outcome="passed"]')).toHaveCount(
      allScenarios.length,
    )
  })

  // The tests on the published site share one run suffix and the asset the register test creates. A retry runs the
  // whole serial group again in a new worker, so it gets a new suffix, and it adds records. So no test counts an
  // exact number of them.
  const run = `${Date.now()}`
  const apiRecords = (entity: string) => `${journeyURL}/api/apps/AssetManagement/entities/${entity}/records`
  const registered = `Laptop ${run}`
  const imported = ['Imported ThinkPad X1', 'Imported Dell U2723']

  /** Opens the register searched for `name`, so its row is on the first page. */
  async function searchRegister(page: Page, name: string) {
    await page.goto(`${journeyURL}/asset-management/assets?search=${encodeURIComponent(name)}`)
    await expect(page.getByRole('searchbox', { name: 'Search', exact: true })).toHaveValue(name)
  }

  test('the author publishes AssetManagement, and release 1 is active', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    await page.getByRole('button', { name: 'Publish', exact: true }).click()
    await confirmDialog(page, 'Publish')
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 120_000,
    })
    const rows = page.getByTestId('studio-release-row')
    await expect(rows).toHaveCount(1)
    await expect(rows).toHaveAttribute('data-number', '1')
    await expect(rows).toHaveAttribute('data-active', 'true')
  })

  test('asset-manager registers an asset with an invoice, two software records and a maintenance entry, and sees them on its detail page', async ({
    page,
  }) => {
    await useJourneySession(page, 'asset-manager')
    // The sample has no page for Software or Vendor records, and neither entity is seeded.
    const software = [`Word ${run}`, `Excel ${run}`]
    const vendor = `Vendor ${run}`
    for (const [entity, name] of [
      ['Software', software[0]],
      ['Software', software[1]],
      ['Vendor', vendor],
    ]) {
      const response = await page.request.post(apiRecords(entity), { data: { values: { name } }, headers: csrfHeaders })
      expect(response.status()).toBe(201)
    }

    await page.goto(`${journeyURL}/asset-management/assets`)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(/\/asset-management\/assetform\/new$/)
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill(registered)
    await page.getByRole('textbox', { name: 'Holder', exact: true }).fill('employee')
    await pickOption(page.getByRole('combobox', { name: /^(\* )?Currency$/ }), 'USD')
    await page.getByLabel(fieldLabel('Purchase cost')).fill('1500')

    for (const name of software) {
      await page.getByRole('button', { name: 'Add Software', exact: true }).click()
      const dialog = await waitForDialog(page)
      await dialog.getByTestId('reference-lookup-search').fill(name)
      await dialog.getByRole('row', { name: new RegExp(name) }).click()
      await expect(dialog).toBeHidden()
    }

    // The form has two child tables, the attachments and then the maintenance, and each has an Add row button.
    const addRow = page.getByRole('button', { name: 'Add row', exact: true })
    await addRow.nth(0).click()
    const invoice = `invoice-${run}.pdf`
    const chooser = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload File 1', exact: true }).click()
    await (await chooser).setFiles({
      name: invoice,
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4\n%test\n'),
    })
    await expect(page.getByRole('link', { name: invoice, exact: true })).toBeVisible()

    await addRow.nth(1).click()
    const date = page.getByLabel(fieldLabel('Date 1'))
    await date.click()
    await date.fill('10/01/2026')
    await date.press('Enter')
    await expect(date).toHaveValue('10/01/2026')
    await page.getByRole('button', { name: 'Choose Vendor 1', exact: true }).click()
    const lookup = await waitForDialog(page)
    await lookup.getByTestId('reference-lookup-search').fill(vendor)
    await lookup.getByRole('row', { name: new RegExp(vendor) }).click()
    await expect(lookup).toBeHidden()
    await pickOption(page.getByRole('combobox', { name: 'Currency 1', exact: true }), 'USD')
    await page.getByLabel(fieldLabel('Cost 1')).fill('200')

    const saved = page.waitForResponse(
      (response) => response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/entities/Asset/records'),
    )
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    expect((await saved).status()).toBe(201)
    await expect(page).not.toHaveURL(/assetform/)

    await searchRegister(page, registered)
    const row = page.getByRole('row', { name: new RegExp(registered) })
    await row.getByRole('button', { name: 'View', exact: true }).click()
    await expect(page).toHaveURL(/\/asset-management\/assetdetail\?asset=/)

    const detail = page.getByTestId('detail')
    await expect(detail).toContainText(registered)
    // Retries add assets, so the number is not exact.
    await expect(detail).toContainText(/AS-\d{6}/)
    await expect(detail).toContainText('In stock')
    await expect(detail).toContainText('$1,500')
    await expect(page.getByRole('link', { name: invoice, exact: true })).toBeVisible()
    const labels = page.getByTestId('detail-references-software')
    for (const name of software) {
      await expect(labels).toContainText(name)
    }
    await expectShown(cellValue(page, 'maintenance', 0, 'vendor'), vendor)
    await expectShown(cellValue(page, 'maintenance', 0, 'cost'), /200/)
  })

  test('asset-manager imports the asset CSV after a clean dry run and finds the imported assets in the register', async ({
    page,
  }) => {
    await useJourneySession(page, 'asset-manager')
    await page.goto(`${journeyURL}/asset-management/assets`)

    await page.getByRole('button', { name: 'Import', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog
      .locator('input[type=file]')
      .setInputFiles(path.join(import.meta.dirname, '..', 'fixtures', 'asset-management-import.csv'))
    await dialog.getByRole('button', { name: 'Check', exact: true }).click()
    const commit = dialog.getByRole('button', { name: 'Commit', exact: true })
    await expect(commit).toBeVisible()
    await expect(dialog.getByTestId('import-status')).not.toContainText('Row')
    await commit.click()

    await expect(dialog).toBeHidden()
    await expect(page.getByTestId('table-action-done')).toHaveText('The records were imported.')
    for (const name of imported) {
      await searchRegister(page, name)
      await expect(page.getByRole('row', { name: new RegExp(name) }).first()).toBeVisible()
    }
  })

  test('auditor exports the register to CSV and Excel, sees the dashboard and has no New or Import button', async ({
    page,
  }) => {
    await useJourneySession(page, 'auditor')
    await page.goto(`${journeyURL}/asset-management/assets`)
    await expect(page.getByRole('table')).toBeVisible()
    await expect(page.getByRole('button', { name: 'New', exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Import', exact: true })).toHaveCount(0)

    // While a file is being made the button names its spinner too, 'loading Export', so the name ends with Export.
    const exportAs = async (format: string) => {
      const downloading = page.waitForEvent('download')
      await page.getByRole('button', { name: /Export$/ }).click()
      await page.getByRole('menuitem', { name: format, exact: true }).click()
      return downloading
    }

    const csv = await exportAs('CSV')
    expect(csv.suggestedFilename()).toBe('Asset.csv')
    const text = await readFile((await csv.path())!, 'utf8')
    const lines = text
      .replace(/^﻿/, '')
      .split('\r\n')
      .filter((line) => line !== '')
    // The record API gives the auditor's own view of the register, so the file holds every asset the auditor reads.
    const listed = await page.request.get(`${apiRecords('Asset')}?pageSize=1`)
    expect(listed.status()).toBe(200)
    const { totalCount } = (await listed.json()) as { totalCount: number | string }
    expect(lines.length - 1).toBe(Number(totalCount))
    expect(text).toContain(registered)
    for (const name of imported) {
      expect(text).toContain(name)
    }

    const excel = await exportAs('Excel')
    expect(excel.suggestedFilename()).toMatch(/\.xlsx$/)
    const workbook = await readFile((await excel.path())!)
    // A workbook is a zip file, and a zip file starts with PK. The CSV check covers the contents.
    expect(workbook.subarray(0, 2).toString('latin1')).toBe('PK')

    await page.goto(`${journeyURL}/asset-management/dashboard`)
    await expect(page.getByTestId('kpi-tile')).toContainText('Warranties ending within 30 days')
    await expect(page.getByRole('img', { name: 'Assets by status', exact: true }).first()).toBeVisible()
  })

  test('employee sees only the asset held and no Export, Import or New button', async ({ page }) => {
    await useJourneySession(page, 'employee')
    await page.goto(`${journeyURL}/asset-management/assets`)

    await expect(page.getByRole('table')).toBeVisible()
    await expect(page.getByRole('row', { name: new RegExp(registered) })).toBeVisible()
    for (const name of imported) {
      await expect(page.getByRole('row', { name: new RegExp(name) })).toHaveCount(0)
    }
    for (const name of ['Export', 'Import', 'New']) {
      await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0)
    }
  })
})
