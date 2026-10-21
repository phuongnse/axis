import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { compareApplicationFolders } from '../folder-comparison'
import { journeyApplications, journeyURL, serviceDesk } from '../server-settings'
import { useJourneySession } from '../sessions'

// Typing the sample's 37 keys one by one is slow, and the journey server is shared with other specs.
test.setTimeout(300_000)

const built = path.join(journeyApplications, 'service-desk')

interface SampleDataSource {
  entity: string
  fields: { name: string; path: string }[]
}

interface SampleForm {
  entity: string
  sections: { title: { textKey: string }; fields: { field: string; readOnly?: boolean }[] }[]
}

test.describe('the author builds the service desk with Studio only', () => {
  // CI retries once, but a retry would meet the ServiceDesk folder from the first attempt and get 409, and removing
  // that folder would be a file write. So retries are off.
  test.describe.configure({ mode: 'serial', retries: 0 })

  async function create(page: Page, kind: string, label: string, value: string) {
    await page.getByRole('button', { name: 'New resource' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Kind' }).click()
    await page.getByTitle(kind, { exact: true }).click()
    await dialog.getByRole('textbox', { name: label }).fill(value)
    await dialog.getByRole('button', { name: 'Create' }).click()
  }

  async function pick(page: Page, testId: string, option: string, filter?: string) {
    const field = page.getByTestId(testId)
    const combobox = field.getByRole('combobox')
    await field.locator('.ant-select-selector').click()
    if (filter !== undefined) {
      await combobox.fill(filter)
    }
    // An earlier select's dropdown can stay in the DOM while it leaves, so the option is looked up in this select's list.
    const list = page.locator(`[id="${await combobox.getAttribute('aria-controls')}"]`)
    await list.getByRole('option', { name: option, exact: true }).click()
  }

  async function save(page: Page, answers: [field: string, choice: string][] = []) {
    // A save with a migration posts one changes call, and a save without one patches the resource.
    const answer = page.waitForResponse((response) => {
      const url = response.url()
      const method = response.request().method()
      return (
        (method === 'PATCH' && url.includes('/api/studio/apps/ServiceDesk/resources/')) ||
        (method === 'POST' && url.endsWith('/api/studio/apps/ServiceDesk/changes'))
      )
    })
    await page.getByRole('button', { name: 'Save' }).click()
    for (const [field, choice] of answers) {
      await expect(page.getByTestId('studio-migration-field')).toHaveText(field)
      await page.getByRole('button', { name: choice, exact: true }).click()
    }
    await expect(page.getByTestId('studio-saved')).toBeVisible()
    const { diagnostics } = (await (await answer).json()) as { diagnostics: { file: string }[] }
    return diagnostics
  }

  async function expectForm(page: Page) {
    await expect(page.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true')
  }

  async function writeLabel(page: Page, field: string, key: string, en: string) {
    const label = page.getByTestId(field).getByTestId('studio-label')
    await label.getByRole('textbox', { name: 'Text key', exact: true }).fill(key)
    await expect(label.getByRole('textbox', { name: 'en', exact: true })).toHaveValue(en)
    await label.getByRole('button', { name: 'Write texts' }).click()
    await expect(label.getByTestId('studio-label-written')).toBeVisible()
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

  async function checkBuilt(page: Page, files: string[]) {
    compareApplicationFolders(serviceDesk, built, { files })
    const response = await page.request.get(`${journeyURL}/api/studio/apps/ServiceDesk/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string; file: string }[] }
    expect(
      diagnostics.filter((diagnostic) => diagnostic.severity === 'error' && files.includes(diagnostic.file)),
    ).toEqual([])
  }

  test('the author creates ServiceDesk as a blank application', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('textbox', { name: 'Name' }).fill('ServiceDesk')
    await dialog.getByRole('button', { name: 'Create' }).click()
    await expect(page).toHaveURL(/app=ServiceDesk$/)
  })

  test('the author builds the en and vi texts in the text editor', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const starter = page.getByTestId('studio-group-text')
    await starter.getByRole('button').click()
    await expectForm(page)

    await page.getByRole('button', { name: 'Delete' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click()
    await expect(starter.getByRole('button')).toHaveCount(0)

    await create(page, 'text', 'Locale', 'en')
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/en.json')
    await expectForm(page)

    await create(page, 'text', 'Locale', 'vi')
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/vi.json')
    await expectForm(page)

    const english = await texts('en')
    const vietnamese = await texts('vi')

    const add = page.getByTestId('studio-text-add')
    for (const [key, text] of Object.entries(english)) {
      await add.getByRole('textbox', { name: 'New key', exact: true }).fill(key)
      await add.getByRole('textbox', { name: 'en', exact: true }).fill(text)
      await add.getByRole('textbox', { name: 'vi', exact: true }).fill(vietnamese[key])
      await add.getByRole('button', { name: 'Add key' }).click()
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
      await create(page, 'role', 'Name', name)
      await expectForm(page)
      await writeLabel(page, 'studio-field-/label', key, en[key])
      await save(page)
      await expectForm(page)
    }
  })

  test('the author adds the TicketNumber sequence', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await create(page, 'sequence', 'Name', 'TicketNumber')
    await expectForm(page)
    await page.getByTestId('studio-field-/format').getByRole('textbox').fill('SD-{yyyy}-{n:5}')
    await save(page)
    await expectForm(page)
  })

  test('the author builds the Category entity in the entity designer', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    await create(page, 'entity', 'Name', 'Category')
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

    await create(page, 'seed', 'Name', 'Categories')
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
    await create(page, 'entity', 'Name', 'Ticket')
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
    await page.getByRole('button', { name: 'Delete' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click()
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
      await create(page, 'dataSource', 'Name', name)
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

  test('the author builds the three task forms', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    const en = await texts('en')
    for (const [name, file] of [
      ['TicketTriage', 'forms/ticket-triage.json'],
      ['TicketResolution', 'forms/ticket-resolution.json'],
      ['TicketConfirmation', 'forms/ticket-confirmation.json'],
    ]) {
      const form = await sample<SampleForm>(file)
      await create(page, 'form', 'Name', name)
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
    await create(page, 'process', 'Name', 'TicketHandling')
    await expectForm(page)
    await pick(page, 'studio-field-/entity', 'Ticket')
    await page.getByTestId('studio-step-0').getByRole('button', { name: 'Remove step' }).click()

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
      await page.getByRole('combobox', { name: 'Add step' }).click()
      await page.locator('.ant-select-dropdown:visible').getByRole('option', { name: type, exact: true }).click()
      await stepCard(index).getByRole('textbox', { name: 'Step name' }).fill(name)
    }

    async function operation(index: number, sets: [field: string, expression: string][], next: string) {
      await pick(page, `studio-field-/steps/${index}/operation`, 'updateRecord')
      for (const [field, expression] of sets) {
        await stepCard(index).getByRole('combobox', { name: 'Add mapping Set' }).click()
        await page.locator('.ant-select-dropdown:visible').getByRole('option', { name: field, exact: true }).click()
        const row = page.getByTestId(`studio-field-/steps/${index}/set/${field}`)
        await expect(row.locator('.cm-editor')).toBeVisible()
        await row.getByRole('textbox', { name: 'Expression' }).fill(expression)
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

  test('the built files match the sample', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=ServiceDesk`)

    await checkBuilt(page, [
      'application.json',
      'data-sources/all-ticket-list.json',
      'data-sources/my-ticket-list.json',
      'entities/category.json',
      'entities/ticket.json',
      'forms/ticket-confirmation.json',
      'forms/ticket-resolution.json',
      'forms/ticket-triage.json',
      'processes/ticket-handling.json',
      'roles/agent.json',
      'roles/requester.json',
      'roles/service-desk-lead.json',
      'seeds/categories.json',
      'sequences/ticket-number.json',
      'texts/en.json',
      'texts/vi.json',
    ])
    const migrations = await readdir(path.join(built, 'migrations')).catch((error: NodeJS.ErrnoException) => {
      if (error.code === 'ENOENT') return []
      throw error
    })
    expect(migrations).toEqual([])
  })
})
