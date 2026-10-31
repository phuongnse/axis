import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { appPath } from '../purchase-requests'
import { useSession } from '../sessions'

// The purchase request sample declares the department-budgets module and maps its approver slot to
// department-manager. A manager creates a budget on the module's Budgets page, submits it and decides its task. An
// employee, who does not hold the role, is refused.
const budgetsPath = '/purchasing/department-budgets/budgets'
const tasksPath = `${appPath}/tasks?pageSize=100`

interface TaskItem {
  id: string
  subject: { id: string }
}

test('a manager creates, submits and approves a budget of the module and an employee is refused', async ({ page }) => {
  const name = `Budget ${Date.now()}-${test.info().retry}`

  await useSession(page, 'manager')
  await page.goto('/purchasing/purchaserequests')
  await page.getByRole('menuitem', { name: 'Budgets', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/department-budgets\/budgets$/)

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
  await page.getByLabel(fieldLabel('Year')).fill('2027')
  await page.getByLabel(fieldLabel('Amount')).fill('1500.00')
  await chooseDepartment(page, 'Engineering')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(/\/purchasing\/department-budgets\/budgets$/)
  const row = page.getByRole('row', { name: new RegExp(name) })
  await expect(row).toBeVisible()
  const budgetId = await row.getAttribute('data-row-key')
  expect(budgetId).toBeTruthy()

  // The page must not change before the start has answered, or the browser drops the request.
  const started = page.waitForResponse(
    (response) => response.request().method() === 'POST' && response.url().endsWith('/processes/department-budgets/BudgetApproval/instances'),
  )
  await row.getByRole('button', { name: 'Submit for approval', exact: true }).click()
  expect((await started).status()).toBe(201)

  // The worker creates the task some time after the start.
  const taskRow = page.getByRole('row', { name: new RegExp(name) })
  await expect(async () => {
    await page.goto('/purchasing/mytasks?pageSize=100')
    await expect(taskRow).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
  let taskId = ''
  await expect(async () => {
    const response = await page.request.get(tasksPath)
    expect(response.status()).toBe(200)
    const task = ((await response.json()) as { items: TaskItem[] }).items.find((item) => item.subject.id === budgetId)
    expect(task).toBeDefined()
    taskId = task!.id
  }).toPass({ timeout: 30_000 })

  await taskRow.getByRole('link', { name: 'Review the budget', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(name)
  await page.getByRole('button', { name: 'Approve', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/mytasks$/)

  // The worker marks the budget approved some time after the decision.
  await expect(async () => {
    await page.goto(budgetsPath)
    await expect(page.getByRole('row', { name: new RegExp(`${name}.*approved`, 'i') })).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })

  await useSession(page, 'employee')
  await page.goto('/purchasing/mytasks')
  await expect(page.getByRole('menuitem', { name: 'My tasks', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Budgets', exact: true })).toHaveCount(0)

  await page.goto(budgetsPath)
  await expect(page.getByTestId('shell-error')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  expect((await page.request.get('/api/sites/purchasing/pages/department-budgets/Budgets')).status()).toBe(403)

  const tasks = await page.request.get(tasksPath)
  expect(tasks.status()).toBe(200)
  expect(((await tasks.json()) as { items: TaskItem[] }).items.some((item) => item.subject.id === budgetId)).toBe(false)
  expect((await page.request.get(`${appPath}/tasks/${taskId}`)).status()).toBe(403)
})

/** Opens the lookup of the Department field and picks the row named `department`. */
async function chooseDepartment(page: Page, department: string) {
  await page.getByTestId('reference-department').getByRole('button', { name: 'Choose', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('row', { name: new RegExp(department) }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByLabel(fieldLabel('Department'))).toHaveValue(department)
}
