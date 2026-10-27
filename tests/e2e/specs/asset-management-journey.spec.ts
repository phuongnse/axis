import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { confirmDialog, waitForDialog } from '../dialogs'
import { compareApplicationFolders } from '../folder-comparison'
import { pickOption } from '../select'
import { assetManagement, journeyApplications, journeyURL } from '../server-settings'
import { useJourneySession } from '../sessions'
import { createResource } from '../studio-resources'

// Typing the sample one key at a time is slow, and the journey server is shared with other specs.
test.setTimeout(300_000)

const built = path.join(journeyApplications, 'asset-management')

const sixParts = (file: string) => file === 'application.json' || /^(roles|sequences|entities|seeds|texts)\//.test(file)

test.describe('the author builds the asset management application with Studio only', () => {
  // Each step builds on the folder the one before created, so they run in order.
  test.describe.configure({ mode: 'serial' })

  async function pick(page: Page, testId: string, option: string, filter?: string) {
    await pickOption(page.getByTestId(testId).getByRole('combobox'), option, filter)
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
      await expect(page.getByTestId('studio-migration-field')).toHaveText(field)
      await page.getByRole('button', { name: choice, exact: true }).click()
    }
    await expect(page.getByTestId('studio-saved')).toBeVisible()
    await answer
  }

  async function expectForm(page: Page) {
    await expect(page.getByRole('tab', { name: 'Form', exact: true })).toHaveAttribute('aria-selected', 'true')
  }

  async function writeLabel(page: Page, field: string, key: string, en: string) {
    const label = page.getByTestId(field).getByTestId('studio-label')
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

  async function texts(locale: string) {
    const { texts } = JSON.parse(await readFile(path.join(assetManagement, 'texts', `${locale}.json`), 'utf8')) as {
      texts: Record<string, string>
    }
    return texts
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

  test('the author creates AssetManagement as a blank application', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('AssetManagement')
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(/app=AssetManagement$/)
  })

  test('the author builds the en and vi texts in the text editor', async ({ page }) => {
    await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
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
      await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
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

  test('the author sets the application time zone', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    await page.getByTestId('studio-group-application').getByRole('button').click()
    await expectForm(page)
    await page.getByTestId('studio-field-/timeZone').getByRole('textbox').fill('Asia/Ho_Chi_Minh')
    await save(page)
  })

  test('the built folder matches the sample in its six parts', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=AssetManagement`)

    const files = (await readdir(assetManagement, { recursive: true })).filter(sixParts).sort()
    expect((await readdir(built, { recursive: true })).filter(sixParts).sort()).toEqual(files)
    await expect(page.getByTestId('studio-group-migration').getByRole('button')).toHaveCount(0)
    compareApplicationFolders(assetManagement, built, { files })

    const response = await page.request.get(`${journeyURL}/api/studio/apps/AssetManagement/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { file: string; severity: string }[] }
    expect(
      diagnostics.filter((diagnostic) => diagnostic.severity === 'error' && files.includes(diagnostic.file)),
    ).toEqual([])
  })
})
