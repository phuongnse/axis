import type { Page } from '@playwright/test'
import { expect, test } from '../test'
import { publish } from '../approvals'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { pickAction, pickOption } from '../select'
import { journeyURL } from '../server-settings'
import { useJourneySession } from '../sessions'
import {
  addListItem,
  addListRow,
  createResource,
  doneListItem,
  grantIn,
  openFormTab,
  openKind,
  openListItem,
  writeLabel,
} from '../studio-resources'

// Each step is a long Studio session, and the publish waits for the worker.
test.setTimeout(300_000)

const app = 'ExpenseClaims'
const site = 'expense-claims'
const title = `Zz Expense ${Date.now()}`

// The English text of each key the tutorial uses. The texts step adds only the picked keys below to texts/en.json. The
// other entries are label texts, which each label gets when its text is typed into it.
const en = {
  'expense.label': 'Expense',
  'expense.name': 'Name',
  'expense.amount': 'Amount',
  'expense.status': 'Status',
  'expense.details': 'Details',
  'expense.review': 'Review',
  'expense.submit': 'Submit',
  'expense.cannotSubmit': 'Only a draft expense can be submitted.',
  'role.employee': 'Employee',
  'role.departmentManager': 'Department manager',
  'task.review': 'Review expense',
  'task.approve': 'Approve',
  'task.reject': 'Reject',
  'pages.expenseForm.title': 'Expense',
  'pages.expenses.title': 'Expenses',
  'nav.expenses': 'Expenses',
  'actions.new': 'New',
  'actions.open': 'Open',
}

// The keys that a field or a task picks in a label. Every other label types its own texts.
const picked: string[] = ['expense.name', 'expense.amount', 'expense.status', 'task.review', 'task.approve', 'task.reject']

test.describe('the tutorial builds a first application in Studio', () => {
  // Each step builds on the application the one before created, so they run in order.
  test.describe.configure({ mode: 'serial' })

  async function pick(page: Page, testId: string, option: string, filter?: string) {
    await pickOption(page.getByTestId(testId).getByRole('combobox'), option, filter)
  }

  async function save(page: Page, answers: [field: string, choice: string][] = []) {
    // A save without a migration patches the resource, and a save with one posts one changes call.
    const answer = page.waitForResponse(
      (response) => {
        const url = response.url()
        const method = response.request().method()
        return (
          (method === 'PATCH' && url.includes(`/api/studio/apps/${app}/resources/`)) ||
          (method === 'POST' && url.endsWith(`/api/studio/apps/${app}/changes`))
        )
      },
      { timeout: 60_000 },
    )
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    for (const [field, choice] of answers) {
      // A click while the dialog zooms in is lost, so wait until it has finished opening.
      await waitForDialog(page)
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

  async function createPage(page: Page, name: string) {
    await createResource(page, 'page', name)
    await openFormTab(page)
    await expect(
      page.getByTestId('studio-page-editor').getByRole('textbox', { name: 'Name', exact: true }),
    ).toHaveValue(name)
  }

  async function openTask(page: Page, label: string) {
    const row = page.getByRole('row', { name: new RegExp(title) })
    await expect(async () => {
      await page.goto(`${journeyURL}/${site}`)
      await expect(row).toBeVisible({ timeout: 1_000 })
    }).toPass({ timeout: 60_000 })
    await row.getByRole('link', { name: label, exact: true }).click()
  }

  test('1. Create the application', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application…', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(app)
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`app=${app}$`))
  })

  test('2. Add the texts', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)

    await openKind(page, 'text')
    await page.getByTestId('studio-group-text').getByRole('button').click()
    await expectForm(page)

    const add = page.getByTestId('studio-text-add')
    for (const [key, text] of Object.entries(en)) {
      if (!picked.includes(key)) continue
      await add.getByRole('textbox', { name: 'New key', exact: true }).fill(key)
      await add.getByRole('textbox', { name: 'en', exact: true }).fill(text)
      // Ant Design adds "loading" to the name while the button is busy.
      await add.getByRole('button', { name: /^(loading )?Add key$/ }).click()
      await expect(add.getByRole('textbox', { name: 'New key', exact: true })).toHaveValue('')
      await expectForm(page)
    }
  })

  test('3. Add the roles', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)

    for (const [name, key] of [
      ['employee', 'role.employee'],
      ['department-manager', 'role.departmentManager'],
    ] as const) {
      await createResource(page, 'role', name)
      await expect(page.getByTestId('studio-resource-details')).toContainText(`roles/${name}.json`)
      await expectForm(page)
      await writeLabel(page, 'studio-field-/label', { en: en[key] })
      await save(page)
      await expectForm(page)
    }
  })

  test('4. Build the Expense entity', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)

    await createResource(page, 'entity', 'Expense')
    await expectForm(page)
    await writeLabel(page, 'studio-field-/label', { en: en['expense.label'] })

    // Row 0 is the starter `name` field, which keeps its name. The display field must be required.
    await page.getByTestId('studio-field-/fields/0/required').getByRole('checkbox').check()
    await pick(page, 'studio-field-/fields/0/label', 'expense.name', 'expense.name')

    await page.getByRole('button', { name: 'Add field', exact: true }).click()
    await page.getByTestId('studio-field-/fields/1/name').getByRole('textbox').fill('amount')
    await pick(page, 'studio-field-/fields/1/type', 'decimal')
    await pick(page, 'studio-field-/fields/1/label', 'expense.amount', 'expense.amount')

    await page.getByRole('button', { name: 'Add field', exact: true }).click()
    await page.getByTestId('studio-field-/fields/2/name').getByRole('textbox').fill('status')
    await pick(page, 'studio-field-/fields/2/type', 'enum')
    const values = page.getByTestId('studio-field-/fields/2/values').getByRole('combobox')
    for (const value of ['draft', 'submitted', 'approved', 'rejected']) {
      await values.fill(value)
      await values.press('Enter')
    }
    await values.press('Escape')
    const status = page.getByTestId('studio-field-/fields/2/default')
    await expect(status.locator('.cm-editor')).toBeVisible()
    await status.getByRole('textbox').fill("'draft'")
    await pick(page, 'studio-field-/fields/2/label', 'expense.status', 'expense.status')

    await pick(page, 'studio-field-/displayField', 'name')
    // The expense has no records yet, so the required name leaves its nulls as they are.
    const diagnostics = await save(page, [['name', 'Leave nulls']])
    expect(
      diagnostics.filter(
        (diagnostic) => diagnostic.file === 'entities/expense.json' && diagnostic.severity === 'error',
      ),
    ).toEqual([])
    await expectForm(page)
  })

  test('5. Lay out the two forms', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)

    const forms: {
      name: string
      sectionKey: 'expense.details' | 'expense.review'
      fields: { field: string; readOnly: boolean }[]
    }[] = [
      {
        name: 'ExpenseEdit',
        sectionKey: 'expense.details',
        fields: [
          { field: 'name', readOnly: false },
          { field: 'amount', readOnly: false },
        ],
      },
      {
        name: 'ExpenseReview',
        sectionKey: 'expense.review',
        fields: [
          { field: 'name', readOnly: true },
          { field: 'amount', readOnly: true },
          { field: 'status', readOnly: true },
        ],
      },
    ]
    for (const { name, sectionKey, fields } of forms) {
      await createResource(page, 'form', name)
      await openFormTab(page)
      await pick(page, 'studio-field-/entity', 'Expense')
      await openListItem(page, '/sections', 0)
      await page.getByTestId('studio-field-/sections/0/name').getByRole('textbox').fill('details')
      await writeLabel(page, 'studio-field-/sections/0/title', { en: en[sectionKey] })
      for (const [index, { field, readOnly }] of fields.entries()) {
        if (index > 0) {
          await addListRow(page, '/sections/0/fields')
        }
        await pick(page, `studio-field-/sections/0/fields/${index}/field`, field)
        if (readOnly) {
          await page.getByTestId(`studio-field-/sections/0/fields/${index}/readOnly`).getByRole('checkbox').check()
        }
      }
      await doneListItem(page)
      await save(page)
      await expectForm(page)
    }
  })

  test('6. Build the approval process', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)

    await createResource(page, 'process', 'ExpenseApproval')
    await openFormTab(page)
    await pick(page, 'studio-field-/entity', 'Expense')
    await page.getByTestId('studio-step-0').getByRole('button', { name: 'Remove step', exact: true }).click()

    const stepCard = (index: number) => page.getByTestId(`studio-step-${index}`)
    const steps: [name: string, type: string][] = [
      ['submit', 'operation'],
      ['review', 'task'],
      ['markApproved', 'operation'],
      ['markRejected', 'operation'],
      ['done', 'end'],
    ]
    // Every name exists before any transition is picked, because the pickers offer only named steps.
    for (const [index, [name, type]] of steps.entries()) {
      await pickAction(page.getByRole('combobox', { name: 'Add step', exact: true }), type, stepCard(index))
      await stepCard(index).getByRole('textbox', { name: 'Step name', exact: true }).fill(name)
    }

    async function setStatus(index: number, expression: string, next: string) {
      await pick(page, `studio-field-/steps/${index}/operation`, 'updateRecord')
      const row = page.getByTestId(`studio-field-/steps/${index}/set/status`)
      await pickAction(stepCard(index).getByRole('combobox', { name: 'Add mapping Set', exact: true }), 'status', row)
      await expect(row.locator('.cm-editor')).toBeVisible()
      await row.getByRole('textbox', { name: 'Expression', exact: true }).fill(expression)
      await pick(page, `studio-field-/steps/${index}/next`, next)
    }

    await setStatus(0, "'submitted'", 'review')

    await pick(page, 'studio-field-/steps/1/label/textKey', 'task.review', 'task.review')
    await page.getByTestId('studio-field-/steps/1/assignee').getByText('Role', { exact: true }).click()
    await pick(page, 'studio-field-/steps/1/assignee/role', 'department-manager')
    await pick(page, 'studio-field-/steps/1/form', 'ExpenseReview')
    await page.getByTestId('studio-field-/steps/1/dueIn').getByRole('textbox').fill('P3D')
    for (const [position, [name, label, next]] of [
      ['approve', 'task.approve', 'markApproved'],
      ['reject', 'task.reject', 'markRejected'],
    ].entries()) {
      await stepCard(1).getByRole('button', { name: 'Add outcome', exact: true }).click()
      await page.getByTestId(`studio-field-/steps/1/outcomes/${position}/name`).getByRole('textbox').fill(name)
      await pick(page, `studio-field-/steps/1/outcomes/${position}/label/textKey`, label, label)
      await pick(page, `studio-field-/steps/1/outcomes/${position}/next`, next)
    }

    await setStatus(2, "'approved'", 'done')
    await setStatus(3, "'rejected'", 'done')

    await pick(page, 'studio-field-/start', 'submit')
    const condition = page.getByTestId('studio-field-/startCondition/expression')
    await expect(condition.locator('.cm-editor')).toBeVisible()
    await condition.getByRole('textbox').fill("status == 'draft'")
    await writeLabel(page, 'studio-field-/startCondition/message', { en: en['expense.cannotSubmit'] })
    await save(page)
    await expectForm(page)
  })

  test('7. Build the pages and the site', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)

    await createResource(page, 'dataSource', 'ExpenseList')
    await expectForm(page)
    await pick(page, 'studio-field-/entity', 'Expense')
    for (const [index, field] of ['name', 'amount', 'status'].entries()) {
      // The data source starts with its first field, which is opened, and Add… adds each of the others.
      await (index === 0 ? openListItem(page, '/fields', 0) : addListItem(page, '/fields'))
      await page.getByTestId(`studio-field-/fields/${index}/name`).getByRole('textbox').fill(field)
      await page.getByTestId(`studio-field-/fields/${index}/path`).getByRole('textbox').fill(field)
      await doneListItem(page)
    }
    await save(page)
    await expectForm(page)

    await createPage(page, 'ExpenseForm')
    await writeLabel(page, 'studio-field-/title', { en: en['pages.expenseForm.title'] })
    await pick(page, 'studio-field-/widgets/0/type', 'form')
    await pick(page, 'studio-field-/widgets/0/form', 'ExpenseEdit')
    await page.getByRole('button', { name: 'Add action', exact: true }).click()
    await pick(page, 'studio-field-/widgets/0/actions/0/process', 'ExpenseApproval')
    await writeLabel(page, 'studio-field-/widgets/0/actions/0/label', { en: en['expense.submit'] })
    await save(page)

    await createPage(page, 'Expenses')
    await writeLabel(page, 'studio-field-/title', { en: en['pages.expenses.title'] })
    await pick(page, 'studio-field-/widgets/0/type', 'table')
    await pick(page, 'studio-field-/widgets/0/dataSource', 'ExpenseList')
    // The toolbar's New opens a new expense. The row's Open opens the expense it shows, by its id.
    for (const [kind, key, recordInput] of [
      ['toolbarActions', 'actions.new', false],
      ['rowActions', 'actions.open', true],
    ] as const) {
      const action = `studio-field-/widgets/0/${kind}/0`
      await page
        .getByTestId(`studio-field-/widgets/0/${kind}`)
        .getByRole('button', { name: 'Add navigate action', exact: true })
        .click()
      await pick(page, `${action}/page`, 'ExpenseForm')
      await writeLabel(page, `${action}/label`, { en: en[key] })
      if (recordInput) {
        await page.getByTestId(`${action}/record`).getByRole('textbox').fill('id')
      }
    }
    await save(page)
    await expectForm(page)

    await openKind(page, 'site')
    await page.getByTestId('studio-group-site').getByRole('button').click()
    await expectForm(page)
    await addListItem(page, '/navigation')
    await pick(page, 'studio-field-/navigation/1/page', 'Expenses')
    await writeLabel(page, 'studio-field-/navigation/1/label', { en: en['nav.expenses'] })
    await doneListItem(page)
    await save(page)
    await expectForm(page)
  })

  test('8. Grant access in the policy matrix', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)
    await page.getByText('Policy matrix', { exact: true }).click()

    await grantIn(page, 'employee', 'entity', 'Expense', [
      { actions: ['read'] },
      { actions: ['create', 'update'], filter: "status == 'draft'" },
    ])
    await grantIn(page, 'employee', 'dataSource', 'ExpenseList', [{ actions: ['read'] }])
    await grantIn(page, 'employee', 'process', 'ExpenseApproval', [{ actions: ['start'] }])

    await grantIn(page, 'department-manager', 'entity', 'Expense', [{ actions: ['read'] }])
    await grantIn(page, 'department-manager', 'dataSource', 'ExpenseList', [{ actions: ['read'] }])
  })

  test('9. Check the application', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${app}`)

    await page.getByRole('button', { name: 'Validate', exact: true }).click()
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 120_000,
    })
    await expect(page.getByTestId('studio-problems')).toHaveCount(0)

    const response = await page.request.get(`${journeyURL}/api/studio/apps/${app}/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  })

  test('10. Publish the application', async ({ page }) => {
    await useJourneySession(page, 'author')
    await publish(page, app, 1)

    const rows = page.getByTestId('studio-release-row')
    await expect(rows).toHaveAttribute('data-number', '1')
    await expect(rows).toHaveAttribute('data-active', 'true')
  })

  test('11. Submit an expense as an employee', async ({ page }) => {
    await useJourneySession(page, 'employee')
    await page.goto(`${journeyURL}/${site}/expenses`)

    await page.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/${site}/expenseform/new$`))
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill(title)
    await page.getByLabel(fieldLabel('Amount')).fill('42.50')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/${site}/expenses$`))

    await page
      .getByRole('row', { name: new RegExp(title) })
      .getByRole('link')
      .first()
      .click()
    await expect(page).toHaveURL(new RegExp(`/${site}/expenseform/[0-9a-f-]{36}$`))
    await page.getByRole('button', { name: 'Submit', exact: true }).click()
    await expect(page.getByTestId('form-started')).toHaveText('The process has started.')
  })

  test('12. Approve the expense as a department manager', async ({ page }) => {
    await useJourneySession(page, 'manager')
    await openTask(page, en['task.review'])
    await page.getByRole('button', { name: en['task.approve'], exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/${site}/home$`))

    await page.goto(`${journeyURL}/${site}/expenses`)
    await expect(page.getByRole('row', { name: new RegExp(title) })).toBeVisible()
    await expect(page.getByRole('button', { name: 'New', exact: true })).toHaveCount(0)

    await useJourneySession(page, 'employee')
    await expect(async () => {
      await page.goto(`${journeyURL}/${site}/expenses`)
      await expect(page.getByRole('row', { name: new RegExp(title) })).toContainText('approved', { timeout: 1_000 })
    }).toPass({ timeout: 60_000 })
  })
})
