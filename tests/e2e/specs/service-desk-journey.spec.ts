import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import type { Locator, Page } from '@playwright/test'
import { confirmDialog, waitForDialog } from '../dialogs'
import { dragTo, waitForCanvas } from '../design-drag'
import { expectShown, fieldLabel, fieldValue } from '../fields'
import { compareApplicationFolders } from '../folder-comparison'
import { expectChosen, pickAction, pickOption, pickOptions } from '../select'
import { journeyApplications, journeyURL, serviceDesk } from '../server-settings'
import { useJourneySession } from '../sessions'
import { createResource, openFormTab } from '../studio-resources'

// Typing the sample one key at a time is slow, and the journey server is shared with other specs.
test.setTimeout(300_000)

const built = path.join(journeyApplications, 'service-desk')

interface SampleDataSource {
  entity: string
  fields: { name: string; path: string }[]
  filter?: string
  aggregate?: { groupBy: string[]; measures: { name: string; function: string; field?: string }[] }
}

interface SampleForm {
  entity: string
  sections: {
    name?: string
    title: { textKey: string }
    fields: { field: string; readOnly?: boolean; visibleWhen?: string; requiredWhen?: string }[]
  }[]
  steps?: { title: { textKey: string }; sections: string[] }[]
}

interface SampleScenario {
  name: string
  now: string
  users: Record<string, string[]>
  steps: Record<string, unknown>[]
}

const scenarios = [
  ['TicketIsClosed', 'scenarios/ticket-is-closed.json'],
  ['TicketIsRejected', 'scenarios/ticket-is-rejected.json'],
  ['ReopenedTicketReturnsToTheAgent', 'scenarios/reopened-ticket-returns-to-the-agent.json'],
  ['AgentCannotActOnAnotherAgentsTicket', 'scenarios/agent-cannot-act-on-another-agents-ticket.json'],
  ['LeadCannotActForTheRequester', 'scenarios/lead-cannot-act-for-the-requester.json'],
  ['RequesterCannotReachAnotherTicket', 'scenarios/requester-cannot-reach-another-ticket.json'],
  ['RequesterDeletesADraftTicket', 'scenarios/requester-deletes-a-draft-ticket.json'],
  ['AgentCannotDeleteATicket', 'scenarios/agent-cannot-delete-a-ticket.json'],
  ['LeadIsRemindedOnTheNextWeekdayMorning', 'scenarios/lead-is-reminded-on-the-next-weekday-morning.json'],
  ['LeadIsToldWhichTicketWasDeleted', 'scenarios/lead-is-told-which-ticket-was-deleted.json'],
  ['LateTicketEscalatesToTheLead', 'scenarios/late-ticket-escalates-to-the-lead.json'],
  ['RequesterIsNotifiedOfTheResolution', 'scenarios/requester-is-notified-of-the-resolution.json'],
] as const

test.describe('the author builds the service desk with Studio only', () => {
  // Each step builds on the folder the one before created, so they run in order.
  test.describe.configure({ mode: 'serial' })

  async function createPage(page: Page, name: string) {
    await createResource(page, 'page', name)
    await openFormTab(page)
    await expect(page.getByTestId('studio-page-editor').getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(name)
  }

  // The Form tab stays mounted while the Design tab shows, and repeats its test ids. A Design step passes the design
  // panel as `scope`, so that only the panel's field matches.
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

  async function pick(scope: Page | Locator, testId: string, option: string, filter?: string) {
    await pickOption(scope.getByTestId(testId).getByRole('combobox'), option, filter)
  }

  // A row with more than one select names the one to pick, since pick() would match them all.
  async function pickNamed(page: Page, testId: string, name: string, option: string) {
    await pickOption(page.getByTestId(testId).getByRole('combobox', { name, exact: true }), option)
  }

  async function save(page: Page, answers: [field: string, choice: string, expression?: string][] = []) {
    // A save with a migration posts one changes call, and a save without one patches the resource.
    const answer = page.waitForResponse(
      (response) => {
        const url = response.url()
        const method = response.request().method()
        return (
          (method === 'PATCH' && url.includes('/api/studio/apps/ServiceDesk/resources/')) ||
          (method === 'POST' && url.endsWith('/api/studio/apps/ServiceDesk/changes'))
        )
      },
      { timeout: 60_000 },
    )
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    for (const [field, choice, expression] of answers) {
      // A click while the dialog zooms in is lost, so wait until it has finished opening.
      await waitForDialog(page)
      await expect(page.getByTestId('studio-migration-field')).toHaveText(field)
      if (expression !== undefined) {
        await page.getByRole('textbox', { name: 'Convert expression', exact: true }).fill(expression)
      }
      await page.getByRole('button', { name: choice, exact: true }).click()
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

  async function openDesign(page: Page) {
    await page.getByRole('tab', { name: 'Design', exact: true }).click()
  }

  /** The canvas frame of the widget at `pointer`. */
  function frame(page: Page, pointer: string) {
    return page.locator(`[data-testid="studio-canvas-widget"][data-path="${pointer}"]`)
  }

  async function grantIn(
    page: Page,
    role: string,
    kind: string,
    target: string,
    grants: { actions: string[]; filter?: string; fields?: string[] }[],
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
      if (grant.fields !== undefined) {
        await pickOptions(own.getByRole('combobox'), grant.fields)
      }
    }
    await page.getByTestId('studio-matrix-own-grants').getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-matrix-saved')).toBeVisible()
    // The matrix loads again after a save. A cell clicked before that could lose its new draft.
    for (const action of grants.flatMap((grant) => grant.actions)) {
      await expect(cell).toContainText(action)
    }
  }

  async function texts(locale: string) {
    const { texts } = JSON.parse(await readFile(path.join(serviceDesk, 'texts', `${locale}.json`), 'utf8')) as {
      texts: Record<string, string>
    }
    return texts
  }

  async function sample<T>(file: string): Promise<T> {
    return JSON.parse(await readFile(path.join(serviceDesk, file), 'utf8')) as T
  }

  // The Categories seed gets new record ids in the journey, so each sample id maps to the id the grid shows.
  async function shownCategoryIds(page: Page): Promise<Map<string, string>> {
    await page.getByTestId('studio-group-seed').getByRole('button', { name: 'Categories', exact: true }).click()
    await expectForm(page)
    const rows = page.getByTestId('studio-seed-row')
    await expect(rows).toHaveCount(3)
    const shown = await rows.locator('code').allTextContents()
    const sampleSeed = await sample<{ records: { id: string }[] }>('seeds/categories.json')
    return new Map(sampleSeed.records.map((record, index): [string, string] => [record.id, shown[index]]))
  }

  async function writeScenarioStep(
    page: Page,
    index: number,
    step: Record<string, unknown>,
    ids: Map<string, string>,
    enumFields: Set<string>,
  ) {
    const at = `studio-field-/steps/${index}`
    await pick(page, `${at}/type`, String(step.type))
    for (const key of ['entity', 'record', 'as', 'process', 'step', 'outcome', 'state'] as const) {
      if (key in step) {
        await pick(page, `${at}/${key}`, String(step[key]))
      }
    }
    if ('save' in step) {
      await page.getByTestId(`${at}/save`).getByRole('textbox').fill(String(step.save))
    }
    if ('assignee' in step) {
      const [[kind, name]] = Object.entries(step.assignee as Record<string, string>)
      await pickNamed(page, `${at}/assignee`, 'assignee kind', kind === 'role' ? 'Role' : 'User')
      await pickNamed(page, `${at}/assignee`, kind, name)
    }
    if ('by' in step) {
      await page.getByTestId(`${at}/by`).getByRole('textbox').fill(String(step.by))
    }
    if ('recipient' in step) {
      const [[kind, name]] = Object.entries(step.recipient as Record<string, string>)
      await pickNamed(page, `${at}/recipient`, 'recipient kind', kind === 'role' ? 'Role' : 'User')
      await pickNamed(page, `${at}/recipient`, kind, name)
    }
    if ('channel' in step) {
      await pick(page, `${at}/channel`, String(step.channel))
    }
    if ('values' in step && step.type === 'expectNotification') {
      const values = page.getByTestId(`${at}/values`)
      for (const [key, value] of Object.entries(step.values as Record<string, unknown>)) {
        await values.getByRole('button', { name: 'Add value', exact: true }).click()
        // The new row is the last one, under a fresh key. The rename detaches the input, so Tab goes through the page keyboard.
        await values.getByRole('textbox', { name: 'Value name', exact: true }).last().fill(key)
        await page.keyboard.press('Tab')
        // The box parses its text as JSON, so the text of the number 1 is `1`.
        await values.getByRole('textbox', { name: key, exact: true }).fill(String(value))
      }
    } else if ('values' in step) {
      for (const [key, value] of Object.entries(step.values as Record<string, unknown>)) {
        await pickAction(
          page.getByTestId(`${at}/values`).getByRole('combobox', { name: 'Add value', exact: true }),
          key,
          page.getByTestId(`${at}/values/${key}`),
        )
        if (enumFields.has(key)) {
          await pick(page, `${at}/values/${key}`, String(value))
        } else {
          await page
            .getByTestId(`${at}/values/${key}`)
            .getByRole('textbox')
            .fill(String(ids.get(String(value)) ?? value))
        }
      }
    }
    if ('expect' in step) {
      await pick(page, `${at}/expect`, String(step.expect))
    }
  }

  async function checkBuilt(page: Page) {
    compareApplicationFolders(serviceDesk, built, { ignorePageElementNames: true, generatedNames: true })
    const response = await page.request.get(`${journeyURL}/api/studio/apps/ServiceDesk/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  }

  test('the author creates ServiceDesk as a blank application', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('ServiceDesk')
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(/app=ServiceDesk$/)
  })

  test('the author sets the application time zone', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await page.getByTestId('studio-group-application').getByRole('button').click()
    await expectForm(page)
    await page.getByTestId('studio-field-/timeZone').getByRole('textbox').fill('Asia/Ho_Chi_Minh')
    await save(page)
  })

  test('the author builds the en and vi texts in the text editor', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

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

  test('the author gives the application its label and description', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await page.getByTestId('studio-group-application').getByRole('button').click()
    await expectForm(page)
    await writeLabel(page, 'studio-field-/label', 'application.title', en['application.title'])
    await writeLabel(page, 'studio-field-/description', 'application.description', en['application.description'])
    await save(page)
  })

  test('the author adds the three roles', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    for (const [name, key] of [
      ['requester', 'role.requester'],
      ['agent', 'role.agent'],
      ['service-desk-lead', 'role.serviceDeskLead'],
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

  test('the author adds the TicketNumber sequence', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await createResource(page, 'sequence', 'TicketNumber')
    await expectForm(page)
    await page.getByTestId('studio-field-/format').getByRole('textbox').fill('SD-{yyyy}-{n:5}')
    await save(page)
    await expectForm(page)
  })

  test('the author builds the Category entity in the entity designer', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await createResource(page, 'entity', 'Category')
    await expectForm(page)
    await writeLabel(page, 'studio-field-/label', 'category.label', en['category.label'])

    await page.getByTestId('studio-field-/fields/0/required').getByRole('checkbox').check()
    await page.getByTestId('studio-field-/fields/0/unique').getByRole('checkbox').check()
    await page.getByTestId('studio-field-/fields/0/maxLength').getByRole('spinbutton').fill('100')
    await pick(page, 'studio-field-/fields/0/label', 'category.name', 'category.name')

    await page.getByRole('button', { name: 'Add field', exact: true }).click()
    await page.getByTestId('studio-field-/fields/1/name').getByRole('textbox').fill('defaultAgent')
    await page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton').fill('100')
    await pick(page, 'studio-field-/fields/1/label', 'category.defaultAgent', 'category.defaultAgent')

    await pick(page, 'studio-field-/displayField', 'name')
    await save(page, [
      ['name', 'Skip'],
      ['name', 'Leave nulls'],
    ])
    await expectForm(page)
  })

  test('the author adds the Categories seed in the seed grid', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await createResource(page, 'seed', 'Categories')
    await expectForm(page)
    await pick(page, 'studio-field-/entity', 'Category')
    await page.getByTestId('studio-field-/sync').getByRole('checkbox').check()
    await expect(page.getByTestId('studio-seed-grid')).toBeVisible()

    const addRecord = page.getByRole('button', { name: 'Add record', exact: true })
    for (let index = 0; index < 3; index++) {
      await addRecord.click()
    }
    await expect(page.getByTestId('studio-seed-row')).toHaveCount(3)

    const records = [
      ['Hardware', 'agent'],
      ['Software', 'agent'],
      ['Access', 'agent'],
    ]
    for (const [index, [name, defaultAgent]] of records.entries()) {
      const row = page.getByTestId('studio-seed-row').nth(index)
      await row.getByRole('textbox', { name: 'name', exact: true }).fill(name)
      await row.getByRole('textbox', { name: 'defaultAgent', exact: true }).fill(defaultAgent)
    }
    await save(page)
    await expectForm(page)
  })

  test('the author builds the Ticket entity in the entity designer', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await createResource(page, 'entity', 'Ticket')
    await expectForm(page)
    await writeLabel(page, 'studio-field-/label', 'ticket.label', en['ticket.label'])

    async function addField(index: number, name: string) {
      await page.getByRole('button', { name: 'Add field', exact: true }).click()
      await page.getByTestId(`studio-field-/fields/${index}/name`).getByRole('textbox').fill(name)
      await pick(page, `studio-field-/fields/${index}/label`, `ticket.${name}`, `ticket.${name}`)
    }

    async function addValues(index: number, values: string[]) {
      const input = page.getByTestId(`studio-field-/fields/${index}/values`).getByRole('combobox')
      for (const value of values) {
        await input.fill(value)
        await input.press('Enter')
      }
      await input.press('Escape')
    }

    // Row 0 is the starter `name` field, which becomes `number`.
    await page.getByTestId('studio-field-/fields/0/name').getByRole('textbox').fill('number')
    await page.getByTestId('studio-field-/fields/0/unique').getByRole('checkbox').check()
    await page.getByTestId('studio-field-/fields/0/maxLength').getByRole('spinbutton').fill('20')
    await pick(page, 'studio-field-/fields/0/sequence', 'TicketNumber')
    await pick(page, 'studio-field-/fields/0/label', 'ticket.number', 'ticket.number')

    await addField(1, 'title')
    await page.getByTestId('studio-field-/fields/1/required').getByRole('checkbox').check()
    await page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton').fill('200')

    await addField(2, 'description')
    await page.getByTestId('studio-field-/fields/2/maxLength').getByRole('spinbutton').fill('4000')

    await addField(3, 'category')
    await pick(page, 'studio-field-/fields/3/type', 'reference')
    await pick(page, 'studio-field-/fields/3/target', 'Category')

    await addField(4, 'priority')
    await pick(page, 'studio-field-/fields/4/type', 'enum')
    await addValues(4, ['low', 'normal', 'high', 'urgent'])

    await addField(5, 'requester')
    await page.getByTestId('studio-field-/fields/5/maxLength').getByRole('spinbutton').fill('100')
    await page.getByTestId('studio-field-/fields/5/creator').getByRole('checkbox').check()

    await addField(6, 'assignedAgent')
    await page.getByTestId('studio-field-/fields/6/maxLength').getByRole('spinbutton').fill('100')

    await addField(7, 'status')
    await pick(page, 'studio-field-/fields/7/type', 'enum')
    await addValues(7, ['draft', 'submitted', 'assigned', 'resolved', 'closed', 'rejected', 'reopened'])

    await addField(8, 'resolution')
    await page.getByTestId('studio-field-/fields/8/maxLength').getByRole('spinbutton').fill('4000')

    await pick(page, 'studio-field-/displayField', 'title')
    // Drop the starter data on the rename. Leave title null in existing rows, since it is new and required.
    await save(page, [
      ['name → number', 'Drop data'],
      ['title', 'Leave nulls'],
    ])
    await expectForm(page)

    // The designer wrote a migration for the starter rename. The sample has none, so the author deletes it.
    const migrations = page.getByTestId('studio-group-migration')
    await migrations.getByRole('button').click()
    await expectForm(page)
    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmDialog(page, 'Delete')
    await expect(migrations).toHaveCount(0)
  })

  test('the author removes the reopened status and converts the stored tickets to draft', async ({ page }) => {
    // Studio names the migration after the UTC time of the save, so a fixed clock gives the committed file name.
    await page.clock.setFixedTime(new Date('2026-10-25T12:00:00Z'))
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await page.getByTestId('studio-group-entity').getByRole('button', { name: 'Ticket', exact: true }).click()
    await expectForm(page)
    const values = page.getByTestId('studio-field-/fields/7/values')
    await expect(values.getByText('reopened', { exact: true })).toBeVisible()
    // Backspace in the empty tags input removes the last tag, which is reopened.
    await values.getByRole('combobox').press('Backspace')
    await expect(values.getByText('reopened', { exact: true })).toHaveCount(0)
    await save(page, [['status', 'Add convert', "'draft'"]])
    await expectForm(page)
    await expect(page.getByTestId('studio-group-migration').getByRole('button')).toHaveCount(1)
  })

  test('the author builds the ticket lists and the open ticket counts as data sources', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    for (const [name, file] of [
      ['MyTicketList', 'data-sources/my-ticket-list.json'],
      ['AllTicketList', 'data-sources/all-ticket-list.json'],
      ['OpenTicketTotals', 'data-sources/open-ticket-totals.json'],
      ['UrgentTicketTotals', 'data-sources/urgent-ticket-totals.json'],
      ['OpenTicketsByCategory', 'data-sources/open-tickets-by-category.json'],
    ]) {
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
  })

  test('the author lays out the TicketEdit form on the Design tab', async ({ page }) => {
    // A tall window keeps the palette and the outline in view together, away from the edges where a drag scrolls.
    await page.setViewportSize({ width: 1280, height: 1000 })
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    const form = await sample<SampleForm>('forms/ticket-edit.json')
    await createResource(page, 'form', 'TicketEdit')
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
      await name.fill(section.name!)
      await name.press('Enter')
      await expect(sectionTitle(index)).toHaveText(section.name!)
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
      }
    }

    // The starter section holds one empty field, which the sample does not have. Its outline row has no name and no size
    // to click, so Enter selects it.
    await outlineRow('studio-outline-field', '/sections/0/fields/0').press('Enter')
    await panel.getByTestId('studio-design-remove-field').click()
    await expect(panel.getByTestId('studio-outline-empty-section')).toBeVisible()

    await layOutSection(0)

    // Each step takes the key of its title. The first step lists the section, and the second is empty and selected, so
    // the section added next joins it.
    for (const [index, step] of form.steps!.entries()) {
      await panel.getByTestId('studio-form-add-step').click()
      await expect(outlineRow('studio-outline-step', `/steps/${index}`)).toHaveAttribute('aria-current', 'true')
      await writeLabel(panel, `studio-field-/steps/${index}/title`, step.title.textKey, en[step.title.textKey])
    }
    await panel.getByTestId('studio-form-add-section').click()
    await expect(sectionTitle(1)).toBeVisible()
    await layOutSection(1)

    // The conditions of a field are in the property panel of the field.
    for (const [position, field] of form.sections[1].fields.entries()) {
      for (const condition of ['visibleWhen', 'requiredWhen'] as const) {
        const expression = field[condition]
        if (expression) {
          await outlineRow('studio-outline-field', `/sections/1/fields/${position}`).click()
          const box = panel.getByTestId(`studio-field-/sections/1/fields/${position}/${condition}`)
          await expect(box.locator('.cm-editor')).toBeVisible()
          await box.getByRole('textbox').fill(expression)
        }
      }
    }
    await save(page)
  })

  test('the author builds the three task forms', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    for (const [name, file] of [
      ['TicketTriage', 'forms/ticket-triage.json'],
      ['TicketResolution', 'forms/ticket-resolution.json'],
      ['TicketConfirmation', 'forms/ticket-confirmation.json'],
    ]) {
      const form = await sample<SampleForm>(file)
      await createResource(page, 'form', name)
      await openFormTab(page)
      await pick(page, 'studio-field-/entity', form.entity)
      for (const [sectionIndex, section] of form.sections.entries()) {
        if (sectionIndex > 0) {
          await page
            .getByTestId('studio-field-/sections')
            .getByRole('button', { name: 'Add', exact: true })
            .last()
            .click()
        }
        const titleKey = section.title.textKey
        await writeLabel(page, `studio-field-/sections/${sectionIndex}/title`, titleKey, en[titleKey])
        for (const [fieldIndex, field] of section.fields.entries()) {
          // A new section starts with no fields. The starter section has its first field already.
          if (sectionIndex > 0 || fieldIndex > 0) {
            await page
              .getByTestId(`studio-field-/sections/${sectionIndex}/fields`)
              .getByRole('button', { name: 'Add', exact: true })
              .click()
          }
          await pick(page, `studio-field-/sections/${sectionIndex}/fields/${fieldIndex}/field`, field.field)
          if (field.readOnly) {
            await page
              .getByTestId(`studio-field-/sections/${sectionIndex}/fields/${fieldIndex}/readOnly`)
              .getByRole('checkbox')
              .check()
          }
        }
      }
      await save(page)
      await expectForm(page)
    }
  })

  test('the author builds the TicketHandling process in the process editor', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await createResource(page, 'process', 'TicketHandling')
    await openFormTab(page)
    await pick(page, 'studio-field-/entity', 'Ticket')
    await page.getByTestId('studio-step-0').getByRole('button', { name: 'Remove step', exact: true }).click()

    const stepCard = (index: number) => page.getByTestId(`studio-step-${index}`)
    const steps: [name: string, type: string][] = [
      ['submit', 'operation'],
      ['triage', 'task'],
      ['markAssigned', 'operation'],
      ['resolve', 'task'],
      ['markResolved', 'operation'],
      ['tellRequester', 'operation'],
      ['confirm', 'task'],
      ['markClosed', 'operation'],
      ['markRejected', 'operation'],
      ['done', 'end'],
    ]
    // Every name exists before any transition is picked, because the pickers offer only named steps.
    for (const [index, [name, type]] of steps.entries()) {
      await pickAction(page.getByRole('combobox', { name: 'Add step', exact: true }), type, stepCard(index))
      await stepCard(index).getByRole('textbox', { name: 'Step name', exact: true }).fill(name)
    }

    // The notify fields of a notify step or of an escalation's notify, at the pointer of the notify. The open page is
    // not part of it: the page picker offers only pages that exist, and the pages come after the process.
    async function notify(
      pointer: string,
      spec: {
        to: { role: string } | { user: string }
        channels: string[]
        subject: string
        body: string
        values: [placeholder: string, expression: string][]
      },
    ) {
      await page
        .getByTestId(`studio-field-${pointer}/to`)
        .getByRole('button', { name: 'Add recipient', exact: true })
        .click()
      const recipient = `studio-field-${pointer}/to/0`
      if ('role' in spec.to) {
        // The radio input of a button group is not visible, so the click goes to its label.
        await page
          .getByTestId(recipient)
          .locator('label')
          .filter({ has: page.getByRole('radio', { name: 'Role', exact: true }) })
          .click()
        await pick(page, `${recipient}/role`, spec.to.role)
      } else {
        const user = page.getByTestId(`${recipient}/user`)
        await expect(user.locator('.cm-editor')).toBeVisible()
        await user.getByRole('textbox').fill(spec.to.user)
      }
      for (const channel of spec.channels) {
        await page
          .getByTestId(`studio-field-${pointer}/channels`)
          .getByRole('checkbox', { name: channel, exact: true })
          .check()
      }
      await writeLabel(page, `studio-field-${pointer}/message/subject`, spec.subject, en[spec.subject])
      await writeLabel(page, `studio-field-${pointer}/message/body`, spec.body, en[spec.body])
      const values = page.getByTestId(`studio-field-${pointer}/message/values`)
      for (const [placeholder, expression] of spec.values) {
        await values.getByRole('button', { name: 'Add mapping', exact: true }).click()
        await values.getByRole('textbox', { name: 'Placeholder', exact: true }).last().fill(placeholder)
        const value = page.getByTestId(`studio-field-${pointer}/message/values/${placeholder}`)
        await expect(value.locator('.cm-editor')).toBeVisible()
        await value.getByRole('textbox', { name: 'Expression', exact: true }).fill(expression)
      }
    }

    async function operation(index: number, sets: [field: string, expression: string][], next: string) {
      await pick(page, `studio-field-/steps/${index}/operation`, 'updateRecord')
      for (const [field, expression] of sets) {
        const row = page.getByTestId(`studio-field-/steps/${index}/set/${field}`)
        await pickAction(stepCard(index).getByRole('combobox', { name: 'Add mapping Set', exact: true }), field, row)
        await expect(row.locator('.cm-editor')).toBeVisible()
        await row.getByRole('textbox', { name: 'Expression', exact: true }).fill(expression)
      }
      await pick(page, `studio-field-/steps/${index}/next`, next)
    }

    async function task(
      index: number,
      spec: {
        label: string
        assignee: { role: string } | { user: string }
        form: string
        dueIn: string
        escalations?: { after: string; notify?: Parameters<typeof notify>[1]; reassign?: { role: string } }[]
        outcomes: { name: string; label: string; next: string }[]
      },
    ) {
      await pick(page, `studio-field-/steps/${index}/label/textKey`, spec.label, spec.label)
      if ('role' in spec.assignee) {
        await page.getByTestId(`studio-field-/steps/${index}/assignee`).getByText('Role', { exact: true }).click()
        await pick(page, `studio-field-/steps/${index}/assignee/role`, spec.assignee.role)
      } else {
        const user = page.getByTestId(`studio-field-/steps/${index}/assignee/user`)
        await expect(user.locator('.cm-editor')).toBeVisible()
        await user.getByRole('textbox').fill(spec.assignee.user)
      }
      await pick(page, `studio-field-/steps/${index}/form`, spec.form)
      await page.getByTestId(`studio-field-/steps/${index}/dueIn`).getByRole('textbox').fill(spec.dueIn)
      for (const [position, escalation] of (spec.escalations ?? []).entries()) {
        const row = `/steps/${index}/escalations/${position}`
        await page
          .getByTestId(`studio-field-/steps/${index}/escalations`)
          .getByRole('button', { name: 'Add escalation', exact: true })
          .click()
        const fields = page.getByTestId(`studio-field-${row}`)
        await fields.getByRole('textbox', { name: 'Escalate after', exact: true }).fill(escalation.after)
        if (escalation.notify) {
          await fields.getByRole('switch', { name: 'Notify', exact: true }).click()
          await notify(`${row}/notify`, escalation.notify)
        }
        if (escalation.reassign) {
          // A new reassign starts as a user, so the Role radio is picked before the role.
          await fields.getByRole('switch', { name: 'Reassign', exact: true }).click()
          await page.getByTestId(`studio-field-${row}/reassign`).getByText('Role', { exact: true }).click()
          await pick(page, `studio-field-${row}/reassign/role`, escalation.reassign.role)
        }
      }
      for (const [position, outcome] of spec.outcomes.entries()) {
        await stepCard(index).getByRole('button', { name: 'Add outcome', exact: true }).click()
        await page
          .getByTestId(`studio-field-/steps/${index}/outcomes/${position}/name`)
          .getByRole('textbox')
          .fill(outcome.name)
        await pick(page, `studio-field-/steps/${index}/outcomes/${position}/label/textKey`, outcome.label, outcome.label)
        await pick(page, `studio-field-/steps/${index}/outcomes/${position}/next`, outcome.next)
      }
    }

    await operation(0, [['status', "'submitted'"]], 'triage')
    await task(1, {
      label: 'ticket.triage',
      assignee: { role: 'service-desk-lead' },
      form: 'TicketTriage',
      dueIn: 'P1D',
      outcomes: [
        { name: 'assign', label: 'task.assign', next: 'markAssigned' },
        { name: 'reject', label: 'task.reject', next: 'markRejected' },
      ],
    })
    await operation(
      2,
      [
        ['status', "'assigned'"],
        ['assignedAgent', 'coalesce(assignedAgent, category.defaultAgent)'],
      ],
      'resolve',
    )
    await task(3, {
      label: 'ticket.resolve',
      assignee: { user: 'assignedAgent' },
      form: 'TicketResolution',
      dueIn: 'P3D',
      escalations: [
        {
          after: 'P3D',
          notify: {
            to: { role: 'service-desk-lead' },
            channels: ['In-app'],
            subject: 'ticket.late.subject',
            body: 'ticket.late.body',
            values: [['number', 'number']],
          },
        },
        { after: 'P4D', reassign: { role: 'service-desk-lead' } },
        {
          after: 'P3D',
          notify: {
            to: { user: 'assignedAgent' },
            channels: ['In-app', 'Email'],
            subject: 'ticket.late.subject',
            body: 'ticket.late.body',
            values: [['number', 'number']],
          },
        },
      ],
      outcomes: [{ name: 'resolve', label: 'task.resolve', next: 'markResolved' }],
    })
    await operation(4, [['status', "'resolved'"]], 'tellRequester')
    // The open page of this step is added after the TicketDetail page exists.
    await pick(page, 'studio-field-/steps/5/operation', 'notify')
    await notify('/steps/5', {
      to: { user: 'requester' },
      channels: ['In-app', 'Email'],
      subject: 'ticket.resolved.subject',
      body: 'ticket.resolved.body',
      values: [
        ['number', 'number'],
        ['resolution', 'resolution'],
      ],
    })
    await pick(page, 'studio-field-/steps/5/next', 'confirm')
    await task(6, {
      label: 'ticket.confirm',
      assignee: { user: 'requester' },
      form: 'TicketConfirmation',
      dueIn: 'P3D',
      outcomes: [
        { name: 'confirm', label: 'task.confirm', next: 'markClosed' },
        { name: 'reopen', label: 'task.reopen', next: 'markAssigned' },
      ],
    })
    await operation(7, [['status', "'closed'"]], 'done')
    await operation(8, [['status', "'rejected'"]], 'done')

    await pick(page, 'studio-field-/start', 'submit')
    const condition = page.getByTestId('studio-field-/startCondition/expression')
    await expect(condition.locator('.cm-editor')).toBeVisible()
    await condition.getByRole('textbox').fill("status == 'draft'")
    await writeLabel(page, 'studio-field-/startCondition/message', 'ticket.cannotSubmit', en['ticket.cannotSubmit'])
    await save(page)
    await expectForm(page)
  })

  test('the author builds the ticket form, the task inbox and the ticket detail pages', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await createPage(page, 'TicketForm')
    await writeLabel(page, 'studio-field-/title', 'pages.ticketForm.title', en['pages.ticketForm.title'])
    await pick(page, 'studio-field-/widgets/0/type', 'form')
    await pick(page, 'studio-field-/widgets/0/form', 'TicketEdit')
    await page.getByRole('button', { name: 'Add action', exact: true }).click()
    await pick(page, 'studio-field-/widgets/0/actions/0/process', 'TicketHandling')
    await writeLabel(page, 'studio-field-/widgets/0/actions/0/label', 'ticket.submit', en['ticket.submit'])
    await save(page)

    // The starter widget of a new page is the task inbox, which is what MyTasks needs.
    await createPage(page, 'MyTasks')
    await writeLabel(page, 'studio-field-/title', 'pages.myTasks.title', en['pages.myTasks.title'])
    await save(page)

    // The detail page takes the ticket as a required parameter. Its detail widget is laid out on the Design tab later,
    // because the canvas draws nothing while any file has an error. The starter Home page and its site entry have errors
    // until the author finishes the site.
    await createPage(page, 'TicketDetail')
    await writeLabel(page, 'studio-field-/title', 'pages.ticketDetail.title', en['pages.ticketDetail.title'])
    await page.getByRole('button', { name: 'Add parameter', exact: true }).click()
    await page.getByRole('textbox', { name: 'Parameter name', exact: true }).fill('ticket')
    await pick(page, 'studio-field-/parameters/0/type', 'reference')
    await pick(page, 'studio-field-/parameters/0/target', 'Ticket')
    await page.getByRole('switch', { name: 'Required', exact: true }).click()
    await save(page)
  })

  test('the author builds the two ticket table pages', async ({ page }) => {
    await useJourneySession(page, 'author')
    // The page names load with the application, so TicketForm is offered below.
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    for (const [name, titleKey, list] of [
      ['MyTickets', 'pages.myTickets.title', 'MyTicketList'],
      ['AllTickets', 'pages.allTickets.title', 'AllTicketList'],
    ]) {
      await createPage(page, name)
      await writeLabel(page, 'studio-field-/title', titleKey, en[titleKey])
      await pick(page, 'studio-field-/widgets/0/type', 'table')
      await pick(page, 'studio-field-/widgets/0/dataSource', list)
      // The row actions open the ticket in its form and show it on the detail page, and a toolbar action starts a new
      // one. The detail page gets the ticket through its parameter. The page names load with the application, so
      // TicketDetail and its parameter are offered here.
      for (const [kind, index, target, key, parameter] of [
        ['rowActions', 0, 'TicketForm', 'actions.open', undefined],
        ['rowActions', 1, 'TicketDetail', 'actions.view', 'ticket'],
        ['toolbarActions', 0, 'TicketForm', 'actions.new', undefined],
      ] as const) {
        const action = `studio-field-/widgets/0/${kind}/${index}`
        await page
          .getByTestId(`studio-field-/widgets/0/${kind}`)
          .getByRole('button', { name: 'Add navigate action', exact: true })
          .click()
        await pick(page, `${action}/page`, target)
        await writeLabel(page, `${action}/label`, key, en[key])
        if (kind === 'rowActions') {
          // The record of the form link and the ticket parameter of the detail link both take the id of the row.
          const input = parameter ? `${action}/parameters/${parameter}` : `${action}/record`
          await page.getByTestId(input).getByRole('textbox').fill('id')
        }
      }
      await save(page)
    }
  })

  test('the author creates the Dashboard page', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await createPage(page, 'Dashboard')
    await writeLabel(page, 'studio-field-/title', 'pages.dashboard.title', en['pages.dashboard.title'])
    await save(page)
  })

  test('the author finishes the site and deletes the Home page', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await page.getByTestId('studio-group-site').getByRole('button').click()
    await expectForm(page)

    await page.getByTestId('studio-field-/locales/available').getByRole('button', { name: 'Add', exact: true }).click()
    await page.getByTestId('studio-field-/locales/available/1').getByRole('textbox').fill('vi')
    await writeLabel(page, 'studio-field-/description', 'site.description', en['site.description'])

    // The starter site has the entry for Home, which the author turns into the first entry.
    const navigation: [page: string, key: string, icon: string][] = [
      ['MyTickets', 'nav.myTickets', 'support'],
      ['MyTasks', 'nav.myTasks', 'tasks'],
      ['AllTickets', 'nav.allTickets', 'list'],
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
    // The Service desk allows only accounts, so the site names that provider.
    await page.getByRole('button', { name: 'Add provider', exact: true }).click()
    await pickOption(page.getByTestId('studio-field-/signIn/0').getByRole('combobox'), 'accounts')
    await save(page)
    await expectForm(page)

    // The sample has no Home page, and nothing points to it now.
    const home = page.getByTestId('studio-group-page').getByRole('button', { name: 'Home', exact: true })
    await home.click()
    await openFormTab(page)
    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmDialog(page, 'Delete')
    await expect(page.getByTestId("studio-group-page").getByRole("button", { name: /Home/i })).toHaveCount(0)
  })

  test('the author lays out the TicketDetail page on the Design tab', async ({ page }) => {
    // A tall, wide window keeps the data panel and the canvas in view together, away from the edges where a drag scrolls.
    // A table makes the canvas wider than a narrow window, which pushes the panel out of sight of the mouse.
    await page.setViewportSize({ width: 1920, height: 1000 })
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await openPage(page, 'TicketDetail')
    // The Ticket entity dropped on the starter widget binds a detail widget to the required ticket parameter. The
    // starter task inbox is not in the sample, so the author removes it from the widget tree.
    await openDesign(page)
    const panel = page.getByTestId('studio-design-panel')
    const starter = frame(page, '/widgets/0')
    await expect(starter).toBeVisible()
    await dragTo(page, 'studio-page-design', panel.getByTestId('studio-data-entity-Ticket'), starter, 0.75)
    await chooseBinding(page, 'detail')
    await expect(frame(page, '/widgets/1')).toBeVisible()
    await panel
      .getByTestId('studio-widget-row-/widgets/0')
      .getByRole('button', { name: 'Remove widget', exact: true })
      .click()
    await expect(panel.getByTestId('studio-widget-/widgets/1')).toHaveCount(0)
    await save(page)
  })

  test('the author makes the resolved notification open the TicketDetail page', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    // The page picker offers only pages that exist, so this step waited for TicketDetail.
    await page.getByTestId('studio-group-process').getByRole('button', { name: 'TicketHandling', exact: true }).click()
    await openFormTab(page)
    await expect(page.getByTestId('studio-step-5')).toContainText('tellRequester')
    await page
      .getByTestId('studio-field-/steps/5/open')
      .getByRole('checkbox', { name: 'Open a page', exact: true })
      .check()
    await pick(page, 'studio-field-/steps/5/open/page', 'TicketDetail')
    // The page has a ticket parameter, so the mapping key is picked from its parameters.
    const row = page.getByTestId('studio-field-/steps/5/open/parameters/ticket')
    await pickAction(
      page.getByRole('combobox', { name: 'Add mapping Page parameters', exact: true }),
      'ticket',
      row,
    )
    await expect(row.locator('.cm-editor')).toBeVisible()
    await row.getByRole('textbox', { name: 'Expression', exact: true }).fill('id')
    await save(page)
    await expectForm(page)
  })

  test('the author lays out the Dashboard on the Design tab with two tiles, a chart and the ticket table', async ({ page }) => {
    // A tall, wide window keeps the palette, the data panel and the canvas in view together, away from the edges where a
    // drag scrolls. A table makes the canvas wider than a narrow window, which pushes the panel out of sight of the mouse.
    await page.setViewportSize({ width: 1920, height: 1000 })
    await useJourneySession(page, 'author')
    // The names load with the application, so the new data sources and AllTickets are offered below.
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await openPage(page, 'Dashboard')
    await openDesign(page)
    const panel = page.getByTestId('studio-design-panel')
    const dragOnto = (source: string, target: Locator, fraction?: number) =>
      dragTo(page, 'studio-page-design', panel.getByTestId(source), target, fraction)
    const list = (pointer: string) => page.locator(`[data-testid="studio-canvas-list"][data-list="${pointer}"]`)
    await expect(frame(page, '/widgets/0')).toBeVisible()

    // The ticket table goes below the starter widget, and the columns widget above it. Then the author removes the
    // starter task inbox, which the sample does not have.
    await dragOnto('studio-data-dataSource-AllTicketList', frame(page, '/widgets/0'), 0.75)
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

    // Each tile is a data source dropped on the first column's list, which puts it at the end. The data source sets the
    // measure, and the author writes the label. The columns widget does not compile until its tiles have labels and its
    // second column has a widget, so the canvas draws it as a placeholder with its lists, and the tiles are found in the
    // widget tree.
    const tiles = '/widgets/0/columns/0/widgets'
    for (const [index, tile] of [
      { dataSource: 'OpenTicketTotals', label: 'dashboard.open.label', show: 'dashboard.open.show' },
      { dataSource: 'UrgentTicketTotals', label: 'dashboard.urgent.label' },
    ].entries()) {
      const base = `${tiles}/${index}`
      await dragOnto(`studio-data-dataSource-${tile.dataSource}`, list(tiles))
      await chooseBinding(page, 'kpi')
      await expect(panel.getByTestId(`studio-widget-${base}`)).toBeVisible()
      await writeLabel(panel, `studio-field-${base}/label`, tile.label, en[tile.label])
      if (tile.show) {
        // The open tile links to the table of every ticket.
        await panel
          .getByTestId(`studio-field-${base}/navigate`)
          .getByRole('button', { name: 'Add navigate action', exact: true })
          .click()
        await pick(panel, `studio-field-${base}/navigate/page`, 'AllTickets')
        await writeLabel(panel, `studio-field-${base}/navigate/label`, tile.show, en[tile.show])
      }
    }

    // The chart is a data source with one group field dropped in the second column. The binding picks the category and
    // the measure.
    const chart = '/widgets/0/columns/1/widgets/0'
    await dragOnto('studio-data-dataSource-OpenTicketsByCategory', list('/widgets/0/columns/1/widgets'))
    await chooseBinding(page, 'chart')
    await expect(panel.getByTestId(`studio-widget-${chart}`)).toBeVisible()
    await writeLabel(
      panel,
      `studio-field-${chart}/measures/0/label`,
      'dashboard.byCategory.open',
      en['dashboard.byCategory.open'],
    )
    await writeLabel(panel, `studio-field-${chart}/title`, 'dashboard.byCategory.title', en['dashboard.byCategory.title'])

    // A table that is dragged into the chart's column by mistake is one move, and one undo puts it back below the columns.
    await dragTo(page, 'studio-page-design', frame(page, '/widgets/1'), frame(page, chart), 0.75)
    await expect(page.getByTestId('studio-palette-announcement')).toContainText('Moved table')
    await expect(frame(page, '/widgets/0/columns/1/widgets/1')).toBeVisible()
    await page.getByRole('button', { name: 'Undo', exact: true }).click()
    await expect(frame(page, '/widgets/0/columns/1/widgets/1')).toHaveCount(0)
    await expect(frame(page, '/widgets/1').getByRole('columnheader', { name: 'Title', exact: true })).toBeVisible()
    await save(page)
  })

  test('the author builds the two notify processes and the schedule and delete triggers', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    const stepCard = (index: number) => page.getByTestId(`studio-step-${index}`)

    // A new trigger starts as an event trigger, and only picking an event and clearing it removes the key. The sample
    // has no event, so one exists until both triggers are built.
    await createResource(page, 'event', 'Placeholder')
    await openFormTab(page)
    await pick(page, 'studio-field-/entity', 'Ticket')
    await save(page)

    async function dropEvent() {
      await pick(page, 'studio-field-/event', 'Placeholder')
      const field = page.getByTestId('studio-field-/event')
      await field.hover()
      await field.getByRole('img', { name: 'close-circle', exact: true }).click()
      await expect(field.getByTitle('Placeholder', { exact: true })).toHaveCount(0)
    }

    async function buildNotifyProcess(
      name: string,
      parameters: [name: string, type: string][],
      step: {
        name: string
        subject: string
        body: string
        values: [placeholder: string, expression: string][]
        page?: string
      },
    ) {
      await createResource(page, 'process', name)
      await openFormTab(page)
      await page
        .locator('label')
        .filter({
          has: page.getByRole('radio', { name: 'Parameters', exact: true }),
        })
        .click()
      await expect(page.getByTestId('studio-field-/entity')).toHaveCount(0)
      for (const [index, [parameter, type]] of parameters.entries()) {
        await page.getByRole('button', { name: 'Add parameter', exact: true }).click()
        await page.getByTestId(`studio-field-/parameters/${index}/name`).getByRole('textbox').fill(parameter)
        await pick(page, `studio-field-/parameters/${index}/type`, type)
      }
      await stepCard(0).getByRole('button', { name: 'Remove step', exact: true }).click()
      for (const [index, [stepName, type]] of [
        [step.name, 'operation'],
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
      await pick(page, 'studio-field-/steps/0/to/0/role', 'service-desk-lead')
      await page
        .getByTestId('studio-field-/steps/0/channels')
        .getByRole('checkbox', { name: 'In-app', exact: true })
        .check()
      await writeLabel(page, 'studio-field-/steps/0/message/subject', step.subject, en[step.subject])
      await writeLabel(page, 'studio-field-/steps/0/message/body', step.body, en[step.body])
      const values = page.getByTestId('studio-field-/steps/0/message/values')
      for (const [placeholder, expression] of step.values) {
        await values.getByRole('button', { name: 'Add mapping', exact: true }).click()
        await values.getByRole('textbox', { name: 'Placeholder', exact: true }).last().fill(placeholder)
        const value = page.getByTestId(`studio-field-/steps/0/message/values/${placeholder}`)
        await expect(value.locator('.cm-editor')).toBeVisible()
        await value.getByRole('textbox', { name: 'Expression', exact: true }).fill(expression)
      }
      if (step.page !== undefined) {
        await page
          .getByTestId('studio-field-/steps/0/open')
          .getByRole('checkbox', { name: 'Open a page', exact: true })
          .check()
        await pick(page, 'studio-field-/steps/0/open/page', step.page)
      }
      await pick(page, 'studio-field-/steps/0/next', 'done')
      await pick(page, 'studio-field-/start', step.name)
      await save(page)
      await expectForm(page)
    }

    await buildNotifyProcess('DailyOverdueReminder', [['daysOverdue', 'integer']], {
      name: 'remind',
      subject: 'notifications.overdue.subject',
      body: 'notifications.overdue.body',
      values: [['days', 'daysOverdue']],
      page: 'AllTickets',
    })
    await buildNotifyProcess(
      'TellLeadTicketDeleted',
      [
        ['number', 'text'],
        ['title', 'text'],
      ],
      {
        name: 'tellLead',
        subject: 'notifications.ticketDeleted.subject',
        body: 'notifications.ticketDeleted.body',
        values: [
          ['number', 'number'],
          ['title', 'title'],
        ],
      },
    )

    await createResource(page, 'trigger', 'EveryMorning')
    await openFormTab(page)
    await pick(page, 'studio-field-/type', 'schedule')
    await dropEvent()
    await page.getByTestId('studio-field-/schedule').getByRole('textbox').fill('0 8 * * 1-5')
    await page.getByTestId('studio-field-/timeZone').getByRole('textbox').fill('Asia/Ho_Chi_Minh')
    await pick(page, 'studio-field-/process', 'DailyOverdueReminder')
    const schedule = page.getByTestId('studio-field-/parameters')
    await schedule.getByRole('button', { name: 'Add', exact: true }).click()
    await schedule.getByRole('spinbutton', { name: 'daysOverdue', exact: true }).fill('1')
    await save(page)

    await createResource(page, 'trigger', 'TicketDeleted')
    await openFormTab(page)
    await pick(page, 'studio-field-/type', 'dataChange')
    await dropEvent()
    await pick(page, 'studio-field-/entity', 'Ticket')
    await page.getByTestId('studio-field-/on').getByRole('button', { name: 'Add', exact: true }).click()
    await pick(page, 'studio-field-/on/0', 'delete')
    await pick(page, 'studio-field-/process', 'TellLeadTicketDeleted')
    const deleted = page.getByTestId('studio-field-/parameters')
    for (const parameter of ['number', 'title']) {
      await deleted.getByRole('button', { name: 'Add', exact: true }).click()
      const value = page.getByTestId(`studio-field-/parameters/${parameter}`)
      await expect(value.locator('.cm-editor')).toBeVisible()
      await value.getByRole('textbox').fill(parameter)
    }
    await save(page)

    await page.getByTestId('studio-group-event').getByRole('button', { name: 'Placeholder', exact: true }).click()
    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmDialog(page, 'Delete')
    await expect(page.getByTestId('studio-group-event')).toHaveCount(0)
  })

  test('the author builds the three access policies in the policy matrix', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)
    await page.getByText('Policy matrix', { exact: true }).click()

    // The matrix appends each grant to the end of the policy, so the order here is the order of the sample.
    await grantIn(page, 'requester', 'entity', 'Ticket', [
      { actions: ['read'], filter: 'requester == currentUser()' },
      {
        actions: ['create', 'update'],
        filter: "requester == currentUser() and status == 'draft'",
        fields: ['title', 'description', 'category', 'priority', 'status'],
      },
      { actions: ['delete'], filter: "requester == currentUser() and status == 'draft'" },
    ])
    await grantIn(page, 'requester', 'entity', 'Category', [{ actions: ['read'] }])
    await grantIn(page, 'requester', 'dataSource', 'MyTicketList', [{ actions: ['read'] }])
    await grantIn(page, 'requester', 'process', 'TicketHandling', [{ actions: ['start'] }])

    await grantIn(page, 'agent', 'entity', 'Ticket', [{ actions: ['read'], filter: 'assignedAgent == currentUser()' }])
    await grantIn(page, 'agent', 'entity', 'Category', [{ actions: ['read'] }])
    await grantIn(page, 'agent', 'dataSource', 'MyTicketList', [{ actions: ['read'] }])

    await grantIn(page, 'service-desk-lead', 'entity', 'Ticket', [{ actions: ['read'] }])
    await grantIn(page, 'service-desk-lead', 'entity', 'Category', [{ actions: ['read'] }])
    await grantIn(page, 'service-desk-lead', 'dataSource', 'AllTicketList', [{ actions: ['read'] }])
    await grantIn(page, 'service-desk-lead', 'dataSource', 'OpenTicketTotals', [{ actions: ['read'] }])
    await grantIn(page, 'service-desk-lead', 'dataSource', 'UrgentTicketTotals', [{ actions: ['read'] }])
    await grantIn(page, 'service-desk-lead', 'dataSource', 'OpenTicketsByCategory', [{ actions: ['read'] }])
    await grantIn(page, 'service-desk-lead', 'process', 'TicketHandling', [{ actions: ['reassign'] }])
  })

  for (const [name, file] of scenarios) {
    test(`the author writes the ${name} scenario in the scenario editor`, async ({ page }) => {
      await useJourneySession(page, 'author')
      await page.goto(`${journeyURL}/studio?app=ServiceDesk`)
      const ids = await shownCategoryIds(page)

      await createResource(page, 'scenario', name)
      await expect(page.getByTestId('studio-resource-details')).toContainText(file)
      await expectForm(page)

      const scenario = await sample<SampleScenario>(file)
      const ticket = await sample<{ fields: { name: string; type: string }[] }>('entities/ticket.json')
      const enumFields = new Set(ticket.fields.filter((field) => field.type === 'enum').map((field) => field.name))

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
        await pickOptions(row.getByRole('combobox', { name: 'Roles', exact: true }), roles)
        for (const role of roles) {
          await expect(row).toContainText(role)
        }
      }

      for (const [index, step] of scenario.steps.entries()) {
        if (index > 0) {
          await page.getByRole('button', { name: 'Add step', exact: true }).click()
        }
        await writeScenarioStep(page, index, step, ids, enumFields)
      }

      const diagnostics = await save(page)
      const errors = diagnostics.filter((diagnostic) => diagnostic.file === file && diagnostic.severity === 'error')
      expect(errors).toEqual([])
      await expectForm(page)
    })
  }

  test('the whole built folder matches the sample', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await checkBuilt(page)
  })

  test('the author runs the twelve test scenarios from Studio', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)
    await page.getByTestId('studio-run-tests').click()

    // Each scenario creates and drops a database, and the publisher queue is shared with other specs.
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 240_000,
    })
    await expect(page.getByTestId('studio-test-result')).toHaveCount(scenarios.length)
    await expect(page.locator('[data-testid="studio-test-result"][data-outcome="passed"]')).toHaveCount(
      scenarios.length,
    )
  })

  test('the author publishes ServiceDesk, and release 1 is active', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

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


  /** Opens the task `label` of the ticket `title` in My tasks. It waits until the worker creates the task. */
  async function openTask(page: Page, title: string, label: string) {
    const row = page.getByRole('row', { name: new RegExp(title) })
    await expect(async () => {
      await page.goto(`${journeyURL}/service-desk/mytasks?pageSize=100`)
      await expect(row).toBeVisible({ timeout: 1_000 })
    }).toPass({ timeout: 30_000 })
    await row.getByRole('link', { name: label, exact: true }).click()
  }

  test('requester submits a ticket on the published site, lead triages it to agent, agent resolves it, requester opens it from the bell and confirms it closed', async ({
    page,
  }) => {
    const title = `Zz Journey ${Date.now()}`
    // Shared journey users keep notifications from earlier runs, so the title makes the resolution, and the bell row, this run's.
    const resolution = `Replaced the battery for ${title}.`
    const bell = (page: Page) => page.getByRole('button', { name: /^Notifications(, \d+ unread)?$/ })
    const resolvedRow = (dialog: Locator) => dialog.getByRole('button').filter({ hasText: resolution })

    await useJourneySession(page, 'requester')
    await page.goto(`${journeyURL}/service-desk/mytickets`)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/ticketform\/new$/)
    await page.getByRole('textbox', { name: 'Title', exact: true }).fill(title)
    await page.getByRole('textbox', { name: 'Description', exact: true }).fill('It shows a black screen.')
    await page.getByRole('button', { name: 'Next', exact: true }).click()
    // The requester's create grant only allows the status draft.
    await pickOption(page.getByRole('combobox', { name: 'Status', exact: true }), 'draft')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytickets$/)

    await page.getByRole('row', { name: new RegExp(title) }).getByRole('link').click()
    await expect(page).toHaveURL(/\/service-desk\/ticketform\/[^/]+$/)
    const id = page.url().split('/').pop()
    await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue(title)
    // Submit shows on the last step only.
    await page.getByRole('button', { name: 'Next', exact: true }).click()
    await page.getByRole('button', { name: 'Submit', exact: true }).click()
    await expect(page.getByTestId('form-started')).toBeVisible()

    await useJourneySession(page, 'lead')
    await openTask(page, title, 'Triage ticket')
    await expectShown(fieldValue(page, 'title'), title)
    await page.getByLabel(fieldLabel('Assigned agent')).fill('agent')
    await page.getByRole('button', { name: 'Assign', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytasks$/)

    await useJourneySession(page, 'agent')
    await openTask(page, title, 'Resolve ticket')
    await page.getByLabel(fieldLabel('Resolution')).fill(resolution)
    await page.getByRole('button', { name: 'Resolve', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytasks$/)

    // The worker notifies the requester some time after the decision. The bell opens the ticket on its detail page.
    await useJourneySession(page, 'requester')
    let dialog: Locator
    await expect(async () => {
      await page.goto(`${journeyURL}/service-desk/mytickets`)
      await bell(page).click({ timeout: 1_000 })
      dialog = await waitForDialog(page, 'Notifications')
      await expect(resolvedRow(dialog)).toBeVisible({ timeout: 1_000 })
    }).toPass({ timeout: 60_000 })
    await resolvedRow(dialog!).click()
    await expect(page).toHaveURL(new RegExp(`/service-desk/ticketdetail\\?ticket=${id}$`))
    await expect(page.getByText(title, { exact: true }).first()).toBeVisible()

    // The lead is not told about a ticket that is on time.
    await useJourneySession(page, 'lead')
    await page.goto(`${journeyURL}/service-desk/mytasks`)
    await bell(page).click()
    const leadDialog = await waitForDialog(page, 'Notifications')
    await expect(resolvedRow(leadDialog)).toHaveCount(0)

    await useJourneySession(page, 'requester')
    await openTask(page, title, 'Confirm resolution')
    await expectShown(fieldValue(page, 'resolution'), resolution)
    await page.getByRole('button', { name: 'Confirm', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytasks$/)

    // The worker ends the process some time after the decision.
    await expect(async () => {
      await page.goto(`${journeyURL}/service-desk/ticketform/${id}`)
      await page.getByRole('button', { name: 'Next', exact: true }).click()
      await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), 'closed', { timeout: 1_000 })
    }).toPass({ timeout: 30_000 })
  })
})
