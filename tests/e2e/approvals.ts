import { randomUUID } from 'node:crypto'
import type { APIRequestContext, Page } from '@playwright/test'
import { expect } from './test'
import { csrfHeaders } from './csrf'
import { confirmDialog, waitForDialog } from './dialogs'
import { fieldLabel } from './fields'
import { journeyURL } from './server-settings'
import { useJourneySession } from './sessions'

interface ResourceItem {
  id: string
  kind: string
  file: string
  version: string
}

const studioApi = (app: string) => `${journeyURL}/api/studio/apps/${app}`
const organization = (app: string, entity: string) =>
  `${journeyURL}/api/apps/${app}/entities/organization/${entity}/records`

async function resources(page: Page, app: string): Promise<ResourceItem[]> {
  const response = await page.request.get(`${studioApi(app)}/resources`)
  expect(response.status()).toBe(200)
  return ((await response.json()) as { items: ResourceItem[] }).items
}

async function writeText(page: Page, app: string, key: string, texts: Record<string, string>) {
  const response = await page.request.put(`${studioApi(app)}/texts/${key}`, {
    headers: csrfHeaders,
    data: { texts },
  })
  expect(response.status(), await response.text()).toBe(200)
}

async function expectNoErrors(page: Page, app: string) {
  const response = await page.request.get(`${studioApi(app)}/diagnostics`)
  expect(response.status()).toBe(200)
  const { diagnostics } = (await response.json()) as {
    diagnostics: { severity: string }[]
  }
  expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
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

/** Creates the application `name` from the built-in Approvals application and returns the path of its site. */
export async function createFromBuiltIn(page: Page, name: string): Promise<string> {
  await page.goto(`${journeyURL}/studio`)
  await page.getByRole('button', { name: 'New application…', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
  await dialog.getByRole('radio', { name: new RegExp('^Approvals') }).click()
  await dialog.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`app=${name}$`))

  const site = (await resources(page, name)).find((item) => item.kind === 'site')!
  const response = await page.request.get(`${studioApi(name)}/resources/${site.id}`)
  expect(response.status()).toBe(200)
  return ((await response.json()) as { content: { path: string } }).content.path
}

/** Maps the admin slot of both modules to the new role department-manager, and checks that the application compiles. */
export async function remapAdmin(page: Page, name: string) {
  const application = (await resources(page, name)).find((item) => item.file === 'application.json')!
  const patch = await page.request.patch(`${studioApi(name)}/resources/${application.id}`, {
    headers: {
      ...csrfHeaders,
      'Content-Type': 'application/json-patch+json',
      'If-Match': `"${application.version}"`,
    },
    data: JSON.stringify([
      {
        op: 'replace',
        path: '/modules/0/roles/admin',
        value: 'department-manager',
      },
      {
        op: 'replace',
        path: '/modules/1/roles/admin',
        value: 'department-manager',
      },
    ]),
  })
  expect(patch.status(), await patch.text()).toBe(200)

  const role = await page.request.post(`${studioApi(name)}/resources`, {
    headers: csrfHeaders,
    data: {
      id: randomUUID(),
      kind: 'role',
      name: 'department-manager',
      formatVersion: 1,
      label: { textKey: 'role.departmentManager' },
    },
  })
  expect(role.status(), await role.text()).toBe(201)
  await writeText(page, name, 'role.departmentManager', { en: 'Department manager', vi: 'Trưởng phòng' })
  await expectNoErrors(page, name)
}

/** Publishes the application `name` and waits until the publish succeeds and `expectedReleases` rows show. */
export async function publish(page: Page, name: string, expectedReleases: number) {
  await page.goto(`${journeyURL}/studio?app=${name}`)
  await page.getByRole('button', { name: 'Publish…', exact: true }).click()
  await confirmDialog(page, 'Publish')
  await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
    timeout: 300_000,
  })
  await expect(page.getByTestId('studio-release-row')).toHaveCount(expectedReleases)
}

async function create(request: APIRequestContext, app: string, entity: string, values: object): Promise<string> {
  const response = await request.post(organization(app, entity), {
    data: { values },
    headers: csrfHeaders,
  })
  expect(response.status(), await response.text()).toBe(201)
  return ((await response.json()) as { id: string }).id
}

async function update(request: APIRequestContext, app: string, entity: string, id: string, values: object) {
  const current = await request.get(`${organization(app, entity)}/${id}`)
  expect(current.status()).toBe(200)
  const { version } = (await current.json()) as { version: number }
  const response = await request.patch(`${organization(app, entity)}/${id}`, {
    data: { version, values },
    headers: csrfHeaders,
  })
  expect(response.status(), await response.text()).toBe(200)
}

function dateOffset(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10)
}

/**
 * Creates the employees `manager-2`, `approver`, `manager` and `employee`, and the department that `employee` heads.
 * It makes `employee` a member of that department, under `manager`.
 */
export async function prepareOrganization(request: APIRequestContext, app: string, stamp: string) {
  const managerTwo = await create(request, app, 'Employee', {
    fullName: `Manager two ${stamp}`,
    workEmail: `manager-2.${stamp}@example.com`,
    user: 'manager-2',
  })
  const approver = await create(request, app, 'Employee', {
    fullName: `Approver ${stamp}`,
    workEmail: `approver.${stamp}@example.com`,
    user: 'approver',
    delegate: managerTwo,
    delegateFrom: dateOffset(-1),
    delegateUntil: dateOffset(1),
  })
  const manager = await create(request, app, 'Employee', {
    fullName: `Manager ${stamp}`,
    workEmail: `manager.${stamp}@example.com`,
    user: 'manager',
  })
  const employee = await create(request, app, 'Employee', {
    fullName: `Employee ${stamp}`,
    workEmail: `employee.${stamp}@example.com`,
    user: 'employee',
    manager,
  })
  const department = await create(request, app, 'Department', {
    code: `HEAD${stamp.toUpperCase()}`,
    name: `Head ${stamp}`,
    head: employee,
  })
  await update(request, app, 'Employee', employee, { department })
  return { approver, manager, employee, department }
}

/** Opens To decide and waits up to 30 seconds for the row of the request `title`. */
async function waitInInbox(page: Page, sitePath: string, title: string) {
  await page.goto(`${journeyURL}/${sitePath}/approvals/todecide`)
  await expect(async () => {
    await page.reload()
    await expect(page.getByRole('row', { name: new RegExp(title) })).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
}

/** Waits for the task of the request `title` in To decide, opens it and approves it. */
async function decideFromInbox(page: Page, sitePath: string, title: string) {
  await waitInInbox(page, sitePath, title)
  await page
    .getByRole('row', { name: new RegExp(title) })
    .getByRole('link')
    .first()
    .click()
  await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/todecide/[0-9a-f-]{36}$`))
  await page.getByRole('button', { name: 'Approve', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/todecide$`))
}

/**
 * Has `employee` submit a request of the type `typeName`, which heads the requester's department. The request must not
 * show in the employee's To decide, and `manager` must approve it there. The employee then sees it approved.
 */
export async function expectHeadRequestGoesToAdmin(page: Page, sitePath: string, typeName: string) {
  const title = `Head request ${Date.now().toString(36)}`

  await useJourneySession(page, 'employee')
  await page.goto(`${journeyURL}/${sitePath}/approvals/myrequests`)
  await page.getByRole('button', { name: 'New request', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/requestform/new$`))
  await choose(page, 'type', 'Type', typeName)
  await page.getByRole('textbox', { name: 'Title', exact: true }).fill(title)
  await page.getByLabel(fieldLabel('Amount')).fill('800')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/myrequests$`))

  await page
    .getByRole('row', { name: new RegExp(title) })
    .getByRole('link')
    .first()
    .click()
  await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/requestform/[0-9a-f-]{36}$`))
  await page.getByRole('button', { name: 'Submit', exact: true }).click()
  await expect(page.getByTestId('form-started')).toHaveText('The process has started.')

  await useJourneySession(page, 'manager')
  await waitInInbox(page, sitePath, title)

  await useJourneySession(page, 'employee')
  await page.goto(`${journeyURL}/${sitePath}/approvals/todecide`)
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0)
  await expect(page.getByRole('table')).toBeVisible()
  await expect(page.getByRole('row', { name: new RegExp(title) })).toHaveCount(0)

  await useJourneySession(page, 'manager')
  await decideFromInbox(page, sitePath, title)

  await useJourneySession(page, 'employee')
  await page.goto(`${journeyURL}/${sitePath}/approvals/myrequests`)
  await expect(async () => {
    await page.reload()
    await expect(page.getByRole('row', { name: new RegExp(title) })).toContainText('Approved', { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })

  await page
    .getByRole('row', { name: new RegExp(title) })
    .getByRole('button', { name: 'View', exact: true })
    .click()
  await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/requestdetail\\?request=[0-9a-f-]{36}$`))
  const detail = page.getByTestId('detail')
  await expect(detail).toContainText(title)
  await expect(detail).toContainText('Approved')
  const decisions = page.locator('tbody tr[data-row-key]')
  await expect(decisions).toHaveCount(1)
  await expect(decisions.first()).toContainText('Approved')
  await expect(decisions.first()).toContainText('manager')
}
