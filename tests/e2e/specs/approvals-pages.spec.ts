import { expect, test } from '../test'
import type { APIRequestContext, Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { pickOption } from '../select'
import { useSession } from '../sessions'
import { chooseTheme } from '../shell'

// The approvals fixture declares the built-in modules `organization` and `approvals`. It maps the slot admin of both to
// department-manager and the slot member of both to employee. The manager, who holds the admin slot, adds a request type
// with two steps. The employee, who holds the member slot, submits a request of that type. The manager decides both
// steps from To decide, as the employee's line manager and as the head of the employee's department. Then the employee
// sees the request approved with its two decisions. All requests and Request types are refused to the employee.
const site = '/approvals'
const organization = '/api/apps/ApprovalsApp/entities/organization'

interface Row {
  id: string
  values: { user?: string | null; code?: string }
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

/** Lists the records of an organization entity. Employees are never deleted, because requests refer to them. */
async function list(request: APIRequestContext, entity: string) {
  const response = await request.get(`${organization}/${entity}/records?pageSize=100`)
  expect(response.status()).toBe(200)
  return ((await response.json()) as { items: Row[] }).items
}

async function create(request: APIRequestContext, entity: string, values: object) {
  const response = await request.post(`${organization}/${entity}/records`, { data: { values }, headers: csrfHeaders })
  expect(response.status()).toBe(201)
  return ((await response.json()) as Row).id
}

async function update(request: APIRequestContext, entity: string, id: string, values: object) {
  const current = await request.get(`${organization}/${entity}/records/${id}`)
  expect(current.status()).toBe(200)
  const { version } = (await current.json()) as { version: number }
  const response = await request.patch(`${organization}/${entity}/records/${id}`, {
    data: { version, values },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(200)
}

/**
 * Makes sure the employee `manager` is the head of a department and that the employee `employee` works there under
 * that manager. It finds what an earlier run left and creates what is missing, so it is safe to rerun.
 */
async function prepareOrganization(request: APIRequestContext, boss: string, member: string) {
  const employees = await list(request, 'Employee')
  const bossId =
    employees.find((employee) => employee.values.user === 'manager')?.id ??
    (await create(request, 'Employee', { fullName: boss, workEmail: 'approvals.boss@example.com', user: 'manager' }))
  const departments = await list(request, 'Department')
  const departmentId =
    departments.find((department) => department.values.code === 'APPRE2E')?.id ??
    (await create(request, 'Department', { code: 'APPRE2E', name: 'Approvals E2E', head: bossId }))
  await update(request, 'Department', departmentId, { head: bossId })
  const memberId =
    employees.find((employee) => employee.values.user === 'employee')?.id ??
    (await create(request, 'Employee', { fullName: member, workEmail: 'approvals.member@example.com', user: 'employee' }))
  await update(request, 'Employee', memberId, { manager: bossId, department: departmentId })
}

test('a manager adds a two-step request type, an employee submits a request, the manager decides both steps and the employee sees it approved', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const typeName = `Purchase ${run}`
  const typeCode = `AP${Date.now().toString(36).toUpperCase()}${test.info().retry}`
  const title = `Laptop ${run}`

  await useSession(page, 'manager')
  await prepareOrganization(page.request, 'Approvals Boss', 'Approvals Member')

  await page.goto(site)
  await expect(page.getByRole('menuitem', { name: 'All requests', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Request types', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'My requests', exact: true })).toHaveCount(0)

  await page.getByRole('menuitem', { name: 'Request types', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/requesttypes$/)
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/requesttypeform\/new$/)
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill(typeName)
  await page.getByRole('textbox', { name: 'Code', exact: true }).fill(typeCode)
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByLabel(fieldLabel('Sequence 1')).fill('1')
  await pickOption(page.getByRole('combobox', { name: 'Approver 1', exact: true }), 'Line manager')
  await page.getByLabel(fieldLabel('Sequence 2')).fill('2')
  await pickOption(page.getByRole('combobox', { name: 'Approver 2', exact: true }), 'Department head')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/requesttypes$/)
  await expect(page.getByRole('row', { name: new RegExp(typeName) })).toBeVisible()

  await useSession(page, 'employee')
  await page.goto(site)
  await expect(page.getByRole('menuitem', { name: 'My requests', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'To decide', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'All requests', exact: true })).toHaveCount(0)
  await expect(page.getByRole('menuitem', { name: 'Request types', exact: true })).toHaveCount(0)
  expect((await page.request.get('/api/sites/approvals/pages/approvals/AllRequests')).status()).toBe(403)
  expect((await page.request.get('/api/sites/approvals/pages/approvals/RequestTypes')).status()).toBe(403)

  await page.getByRole('menuitem', { name: 'My requests', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/myrequests$/)
  await page.getByRole('button', { name: 'New request', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/requestform\/new$/)
  await choose(page, 'type', 'Type', typeName)
  await page.getByRole('textbox', { name: 'Title', exact: true }).fill(title)
  await page.getByLabel(fieldLabel('Amount')).fill('1200')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/myrequests$/)
  const row = page.getByRole('row', { name: new RegExp(title) })
  await expect(row).toContainText('Draft')

  // The Open action is the link on the first cell, the request's number.
  await row.getByRole('link').first().click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/requestform\/[0-9a-f-]{36}$/)
  await page.getByRole('button', { name: 'Submit', exact: true }).click()
  await expect(page.getByTestId('form-started')).toHaveText('The process has started.')

  // The worker runs each step some time after the submit and after each decision, so To decide is read until the task shows.
  await useSession(page, 'manager')
  await decideFromInbox(page, title)
  await decideFromInbox(page, title)

  await useSession(page, 'employee')
  await page.goto(`${site}/approvals/myrequests`)
  await expect(async () => {
    await page.reload()
    await expect(page.getByRole('row', { name: new RegExp(title) })).toContainText('Approved', { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })

  await page.getByRole('row', { name: new RegExp(title) }).getByRole('button', { name: 'View', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/requestdetail\?request=[0-9a-f-]{36}$/)
  await expectApproved(page, title)

  await chooseTheme(page, 'Dark')
  await page.reload()
  await expect(page.locator('[data-theme-mode="dark"]').first()).toBeVisible()
  await expectApproved(page, title)
  await chooseTheme(page, 'Light')
})

/** Opens the task for the request `title` from To decide, once the worker has created it, and approves it. */
async function decideFromInbox(page: Page, title: string) {
  await page.goto(`${site}/approvals/todecide`)
  await expect(async () => {
    await page.reload()
    await expect(page.getByRole('row', { name: new RegExp(title) })).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
  await page.getByRole('row', { name: new RegExp(title) }).getByRole('link').first().click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/todecide\/[0-9a-f-]{36}$/)
  await page.getByRole('button', { name: 'Approve', exact: true }).click()
  await expect(page).toHaveURL(/\/approvals\/approvals\/todecide$/)
}

/** Checks the Request page: the approval path of an approved request with two steps, and its two decisions. */
async function expectApproved(page: Page, title: string) {
  const detail = page.getByTestId('detail')
  await expect(detail).toContainText(title)
  await expect(detail).toContainText('Approval path')
  await expect(detail).toContainText('Approved')
  await expect(detail).toContainText('Steps required')
  const decisions = page.locator('tbody tr[data-row-key]')
  await expect(decisions).toHaveCount(2)
  await expect(decisions.nth(0)).toContainText('Approved')
  await expect(decisions.nth(1)).toContainText('Approved')
  await expect(decisions.nth(0)).toContainText('manager')
}
