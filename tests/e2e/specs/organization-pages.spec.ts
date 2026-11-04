import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { useSession } from '../sessions'
import { pickOption } from '../select'
import { chooseTheme } from '../shell'

// The organization fixture declares the built-in module `organization` and maps its slot admin to department-manager
// and its slot member to employee. A manager adds a department and an employee with a manager on the Departments and
// Employees pages. The manager picks the member's account by name in the Account picker, which stores the user id. An
// employee, who holds the member slot, sees only their own row on the Out of office page and sets a delegate and dates
// there. The Departments and Employees pages are refused to the employee.
const site = '/organization'
const records = '/api/apps/OrganizationApp/entities/organization/Employee/records'

interface Employee {
  id: string
  values: { fullName: string; user: string | null }
}

/** Opens the lookup of the reference field `field`, searches for `name` and picks the row. */
async function choose(page: Page, field: string, label: string, name: string) {
  await page.getByTestId(`reference-${field}`).getByRole('button', { name: 'Choose…', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByTestId('reference-lookup-search').fill(name)
  await dialog.getByRole('row', { name: new RegExp(name) }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByLabel(fieldLabel(label))).toHaveValue(name)
}

/** Sets a date field of a form, which the picker writes in the shown pattern. */
async function setDate(page: Page, label: string, value: string) {
  const field = page.getByLabel(fieldLabel(label))
  await field.fill(value)
  await field.press('Enter')
  await expect(field).toHaveValue(value)
}

test('a manager adds a department and an employee with a manager, and an employee sets their own absence', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const code = `D${Date.now().toString(36).toUpperCase()}${test.info().retry}`
  const department = `Department ${run}`
  const boss = `Boss ${run}`
  const member = `Member ${run}`
  const from = '11/10/2026'
  const until = '11/12/2026'

  await useSession(page, 'manager')

  // The member's record is the only one with user `employee`, and that field is unique, so a rerun removes the earlier one.
  const listed = await page.request.get(`${records}?pageSize=100`)
  expect(listed.status()).toBe(200)
  for (const earlier of ((await listed.json()) as { items: Employee[] }).items.filter((item) => item.values.user === 'employee')) {
    const removed = await page.request.delete(`${records}/${earlier.id}`, { headers: csrfHeaders })
    expect(removed.status()).toBe(204)
  }

  await page.goto(site)
  await expect(page.getByRole('menuitem', { name: 'Departments', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Employees', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Out of office', exact: true })).toHaveCount(0)

  await page.getByRole('menuitem', { name: 'Departments', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/departments$/)
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/departmentform\/new$/)
  await page.getByRole('textbox', { name: 'Code', exact: true }).fill(code)
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill(department)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/departments$/)
  await expect(page.getByRole('row', { name: new RegExp(department) })).toBeVisible()

  await page.getByRole('menuitem', { name: 'Employees', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/employees$/)
  await addEmployee(page, boss)
  await addEmployee(page, member, { user: 'employee', manager: boss, department })
  await expect(page.getByRole('row', { name: new RegExp(member) })).toContainText(boss)

  // The picker stored the user id, not the name it showed.
  const stored = await page.request.get(`${records}?pageSize=100`)
  expect(stored.status()).toBe(200)
  const saved = ((await stored.json()) as { items: Employee[] }).items.filter((item) => item.values.fullName === member)
  expect(saved.map((item) => item.values.user)).toEqual(['employee'])

  await chooseTheme(page, 'Dark')
  await page.reload()
  await expect(page.locator('[data-theme-mode="dark"]').first()).toBeVisible()
  await expect(page.getByRole('row', { name: new RegExp(member) })).toContainText(boss)
  await expect(page.getByRole('row', { name: new RegExp(boss) }).first()).toBeVisible()
  // In dark mode the member's form shows the name of the stored account.
  await page.getByRole('row', { name: new RegExp(member) }).getByRole('link', { name: member, exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/employeeform\/[0-9a-f-]{36}$/)
  await expect(accountSelect(page)).toContainText('Employee User')
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/employees$/)
  await chooseTheme(page, 'Light')

  await useSession(page, 'employee')
  await page.goto(site)
  await expect(page.getByRole('menuitem', { name: 'Departments', exact: true })).toHaveCount(0)
  await expect(page.getByRole('menuitem', { name: 'Employees', exact: true })).toHaveCount(0)
  // The site opens the first page the employee may read, which is the only one.
  await expect(page).toHaveURL(/\/organization\/organization\/outofoffice$/)
  await expect(page.locator('[data-theme-mode="light"]').first()).toBeVisible()
  const rows = page.locator('tbody tr[data-row-key]')
  await expect(rows).toHaveCount(1)
  await expect(rows).toContainText(member)

  await rows.getByRole('link', { name: member, exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/absenceform\/[0-9a-f-]{36}$/)
  await expect(page.getByRole('textbox', { name: 'Full name', exact: true })).toHaveCount(0)
  await choose(page, 'delegate', 'Delegate', boss)
  await setDate(page, 'Absent from', from)
  await setDate(page, 'Absent until', until)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/outofoffice$/)
  await expectAbsence(page, member, boss, from, until)

  await page.goto('/organization/organization/departments')
  await expect(page.getByTestId('shell-error')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  expect((await page.request.get('/api/sites/organization/pages/organization/Departments')).status()).toBe(403)
  expect((await page.request.get('/api/sites/organization/pages/organization/Employees')).status()).toBe(403)

  await chooseTheme(page, 'Dark')
  await page.goto('/organization/organization/outofoffice')
  await expect(page.locator('[data-theme-mode="dark"]').first()).toBeVisible()
  await expect(page.locator('tbody tr[data-row-key]')).toHaveCount(1)
  await expectAbsence(page, member, boss, from, until)
})

/** Checks that the one row of the Out of office page shows the employee, the delegate and both dates. */
async function expectAbsence(page: Page, member: string, delegate: string, from: string, until: string) {
  const row = page.locator('tbody tr[data-row-key]')
  await expect(row).toHaveCount(1)
  await expect(row).toContainText(member)
  await expect(row).toContainText(delegate)
  await expect(row).toContainText(from)
  await expect(row).toContainText(until)
}

/** The shown selection of the Account picker, which holds the combobox's input beside the name. */
function accountSelect(page: Page) {
  return page.getByRole('combobox', { name: 'Account', exact: true }).locator('..')
}

/** Adds an employee on the Employees page and waits for the table. */
async function addEmployee(
  page: Page,
  name: string,
  options: { user?: string; manager?: string; department?: string } = {},
) {
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/employeeform\/new$/)
  await page.getByRole('textbox', { name: 'Full name', exact: true }).fill(name)
  await page.getByRole('textbox', { name: 'Work email', exact: true }).fill(`${name.replaceAll(' ', '.').toLowerCase()}@example.com`)
  if (options.user) {
    await pickOption(page.getByRole('combobox', { name: 'Account', exact: true }), 'Employee User', 'Employee U')
    await expect(accountSelect(page)).toContainText('Employee User')
  }
  if (options.department) {
    await choose(page, 'department', 'Department', options.department)
  }
  if (options.manager) {
    await choose(page, 'manager', 'Manager', options.manager)
  }
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(/\/organization\/organization\/employees$/)
  await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible()
}
