import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { compareApplicationFolders } from '../folder-comparison'
import { expectChosen, pickAction, pickOption, pickOptions } from '../select'
import { journeyApplications, journeyURL, serviceDesk } from '../server-settings'
import { useJourneySession } from '../sessions'
import { createResource } from '../studio-resources'

// Typing the sample one key at a time is slow, and the journey server is shared with other specs.
test.setTimeout(300_000)

const built = path.join(journeyApplications, 'service-desk')

interface SampleDataSource {
  entity: string
  fields: { name: string; path: string }[]
}

interface SampleForm {
  entity: string
  sections: { title: { textKey: string }; fields: { field: string; readOnly?: boolean; visibleWhen?: string }[] }[]
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
  ['ReopenedTicketIsResubmitted', 'scenarios/reopened-ticket-is-resubmitted.json'],
  ['AgentCannotActOnAnotherAgentsTicket', 'scenarios/agent-cannot-act-on-another-agents-ticket.json'],
  ['LeadCannotActForTheRequester', 'scenarios/lead-cannot-act-for-the-requester.json'],
  ['RequesterCannotReachAnotherTicket', 'scenarios/requester-cannot-reach-another-ticket.json'],
] as const

test.describe('the author builds the service desk with Studio only', () => {
  // Each step builds on the folder the one before created, so they run in order.
  test.describe.configure({ mode: 'serial' })

  async function createPage(page: Page, name: string) {
    await createResource(page, 'page', name)
    await expect(page.getByTestId('studio-page-editor').getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(name)
  }

  async function pick(page: Page, testId: string, option: string, filter?: string) {
    await pickOption(page.getByTestId(testId).getByRole('combobox'), option, filter)
  }

  // A row with more than one select names the one to pick, since pick() would match them all.
  async function pickNamed(page: Page, testId: string, name: string, option: string) {
    await pickOption(page.getByTestId(testId).getByRole('combobox', { name, exact: true }), option)
  }

  async function save(page: Page, answers: [field: string, choice: string][] = []) {
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
    for (const [field, choice] of answers) {
      await expect(page.getByTestId('studio-migration-field')).toHaveText(field)
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
      const { user } = step.assignee as { user: string }
      await pickNamed(page, `${at}/assignee`, 'assignee kind', 'User')
      await pickNamed(page, `${at}/assignee`, 'user', user)
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
    compareApplicationFolders(serviceDesk, built)
    const response = await page.request.get(`${journeyURL}/api/studio/apps/ServiceDesk/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  }

  test('the author creates ServiceDesk as a blank application', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('ServiceDesk')
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(/app=ServiceDesk$/)
  })

  test('the author builds the en and vi texts in the text editor', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const starter = page.getByTestId('studio-group-text')
    await starter.getByRole('button').click()
    await expectForm(page)

    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
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
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await createResource(page, 'sequence', 'TicketNumber')
    await expectForm(page)
    await page.getByTestId('studio-field-/format').getByRole('textbox').fill('SD-{yyyy}-{n:5}')
    await save(page)
    await expectForm(page)
  })

  test('the author builds the Category entity in the entity designer', async ({ page }) => {
    await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
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
      ['Hardware', 'khoa'],
      ['Software', 'khoa'],
      ['Access', 'khoa'],
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
    await useJourneySession(page, 'hoa')
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
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(migrations).toHaveCount(0)
  })

  test('the author builds the two ticket lists as data sources', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    for (const [name, file] of [
      ['MyTicketList', 'data-sources/my-ticket-list.json'],
      ['AllTicketList', 'data-sources/all-ticket-list.json'],
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
      await save(page)
      await expectForm(page)
    }
  })

  test('the author builds the ticket form and the three task forms', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    for (const [name, file] of [
      ['TicketEdit', 'forms/ticket-edit.json'],
      ['TicketTriage', 'forms/ticket-triage.json'],
      ['TicketResolution', 'forms/ticket-resolution.json'],
      ['TicketConfirmation', 'forms/ticket-confirmation.json'],
    ]) {
      const form = await sample<SampleForm>(file)
      await createResource(page, 'form', name)
      await expectForm(page)
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
          if (field.visibleWhen) {
            const condition = page.getByTestId(`studio-field-/sections/${sectionIndex}/fields/${fieldIndex}/visibleWhen`)
            await expect(condition.locator('.cm-editor')).toBeVisible()
            await condition.getByRole('textbox').fill(field.visibleWhen)
          }
        }
      }
      await save(page)
      await expectForm(page)
    }
  })

  test('the author builds the TicketHandling process in the process editor', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await createResource(page, 'process', 'TicketHandling')
    await expectForm(page)
    await pick(page, 'studio-field-/entity', 'Ticket')
    await page.getByTestId('studio-step-0').getByRole('button', { name: 'Remove step', exact: true }).click()

    const stepCard = (index: number) => page.getByTestId(`studio-step-${index}`)
    const steps: [name: string, type: string][] = [
      ['submit', 'operation'],
      ['triage', 'task'],
      ['markAssigned', 'operation'],
      ['resolve', 'task'],
      ['markResolved', 'operation'],
      ['confirm', 'task'],
      ['markClosed', 'operation'],
      ['markRejected', 'operation'],
      ['markReopened', 'operation'],
      ['done', 'end'],
    ]
    // Every name exists before any transition is picked, because the pickers offer only named steps.
    for (const [index, [name, type]] of steps.entries()) {
      await pickAction(page.getByRole('combobox', { name: 'Add step', exact: true }), type, stepCard(index))
      await stepCard(index).getByRole('textbox', { name: 'Step name', exact: true }).fill(name)
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
      outcomes: [{ name: 'resolve', label: 'task.resolve', next: 'markResolved' }],
    })
    await operation(4, [['status', "'resolved'"]], 'confirm')
    await task(5, {
      label: 'ticket.confirm',
      assignee: { user: 'requester' },
      form: 'TicketConfirmation',
      dueIn: 'P3D',
      outcomes: [
        { name: 'confirm', label: 'task.confirm', next: 'markClosed' },
        { name: 'reopen', label: 'task.reopen', next: 'markReopened' },
      ],
    })
    await operation(6, [['status', "'closed'"]], 'done')
    await operation(7, [['status', "'rejected'"]], 'done')
    await operation(8, [['status', "'reopened'"]], 'done')

    await pick(page, 'studio-field-/start', 'submit')
    const condition = page.getByTestId('studio-field-/startCondition/expression')
    await expect(condition.locator('.cm-editor')).toBeVisible()
    await condition.getByRole('textbox').fill("status in ('draft', 'reopened')")
    await writeLabel(page, 'studio-field-/startCondition/message', 'ticket.cannotSubmit', en['ticket.cannotSubmit'])
    await save(page)
    await expectForm(page)
  })

  test('the author builds the ticket form and the task inbox pages', async ({ page }) => {
    await useJourneySession(page, 'hoa')
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
  })

  test('the author builds the two ticket table pages', async ({ page }) => {
    await useJourneySession(page, 'hoa')
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
      // A row action opens the ticket, and a toolbar action starts a new one.
      for (const [kind, key, record] of [
        ['rowActions', 'actions.open', 'id'],
        ['toolbarActions', 'actions.new', undefined],
      ] as const) {
        const action = `studio-field-/widgets/0/${kind}/0`
        await page
          .getByTestId(`studio-field-/widgets/0/${kind}`)
          .getByRole('button', { name: 'Add navigate action', exact: true })
          .click()
        await pick(page, `${action}/page`, 'TicketForm')
        await writeLabel(page, `${action}/label`, key, en[key])
        if (record) {
          await page.getByTestId(`${action}/record`).getByRole('textbox').fill(record)
        }
      }
      await save(page)
    }
  })

  test('the author finishes the site and deletes the Home page', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await page.getByTestId('studio-group-site').getByRole('button').click()
    await expectForm(page)

    await page.getByTestId('studio-field-/locales/available').getByRole('button', { name: 'Add', exact: true }).click()
    await page.getByTestId('studio-field-/locales/available/1').getByRole('textbox').fill('vi')

    // The starter site has the entry for Home, which the author turns into the first entry.
    const navigation: [page: string, key: string][] = [
      ['MyTickets', 'nav.myTickets'],
      ['MyTasks', 'nav.myTasks'],
      ['AllTickets', 'nav.allTickets'],
    ]
    for (const [index, [target, key]] of navigation.entries()) {
      if (index > 0) {
        await page.getByTestId('studio-field-/navigation').getByRole('button', { name: 'Add', exact: true }).click()
      }
      await pick(page, `studio-field-/navigation/${index}/page`, target)
      await writeLabel(page, `studio-field-/navigation/${index}/label`, key, en[key])
    }
    await save(page)
    await expectForm(page)

    // The sample has no Home page, and nothing points to it now.
    const home = page.getByTestId('studio-group-page').getByRole('button', { name: 'Home', exact: true })
    await home.click()
    await expectForm(page)
    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(page.getByTestId("studio-group-page").getByRole("button", { name: /Home/i })).toHaveCount(0)
  })

  test('the author builds the three access policies in the policy matrix', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)
    await page.getByText('Policy matrix', { exact: true }).click()

    // The matrix appends each grant to the end of the policy, so the order here is the order of the sample.
    await grantIn(page, 'requester', 'entity', 'Ticket', [
      { actions: ['read'], filter: 'requester == currentUser()' },
      {
        actions: ['create', 'update'],
        filter: "requester == currentUser() and status in ('draft', 'reopened')",
        fields: ['title', 'description', 'status'],
      },
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
  })

  for (const [name, file] of scenarios) {
    test(`the author writes the ${name} scenario in the scenario editor`, async ({ page }) => {
      await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await checkBuilt(page)
  })

  test('the author runs the six test scenarios from Studio', async ({ page }) => {
    await useJourneySession(page, 'hoa')
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
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await page.getByRole('button', { name: 'Publish', exact: true }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Publish', exact: true }).click()
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

  test('anna submits a ticket on the published site, linh triages it to khoa, khoa resolves it, and anna confirms it closed', async ({
    page,
  }) => {
    const title = `Zz Journey ${Date.now()}`

    await useJourneySession(page, 'anna')
    await page.goto(`${journeyURL}/service-desk/mytickets`)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/ticketform\/new$/)
    await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
    await page.getByRole('textbox', { name: 'Description', exact: true }).fill('It shows a black screen.')
    // The requester's create grant only allows the statuses draft and reopened.
    await pickOption(page.getByRole('combobox', { name: 'Status', exact: true }), 'draft')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytickets$/)

    await page.getByRole('row', { name: new RegExp(title) }).getByRole('button', { name: 'Open', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/ticketform\/[^/]+$/)
    const id = page.url().split('/').pop()
    await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
    await page.getByRole('button', { name: 'Submit', exact: true }).click()
    await expect(page.getByTestId('form-started')).toBeVisible()

    await useJourneySession(page, 'linh')
    await openTask(page, title, 'Triage ticket')
    await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
    await page.getByLabel('Assigned agent', { exact: true }).fill('khoa')
    await page.getByRole('button', { name: 'Assign', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytasks$/)

    await useJourneySession(page, 'khoa')
    await openTask(page, title, 'Resolve ticket')
    await page.getByLabel('Resolution', { exact: true }).fill('Replaced the battery.')
    await page.getByRole('button', { name: 'Resolve', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytasks$/)

    await useJourneySession(page, 'anna')
    await openTask(page, title, 'Confirm resolution')
    await expect(page.getByLabel('Resolution', { exact: true })).toHaveValue('Replaced the battery.')
    await page.getByRole('button', { name: 'Confirm', exact: true }).click()
    await expect(page).toHaveURL(/\/service-desk\/mytasks$/)

    // The worker ends the process some time after the decision.
    await expect(async () => {
      await page.goto(`${journeyURL}/service-desk/ticketform/${id}`)
      await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), 'closed', { timeout: 1_000 })
    }).toPass({ timeout: 30_000 })
  })
})
