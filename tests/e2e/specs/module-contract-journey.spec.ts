import { randomUUID } from 'node:crypto'
import type { Page } from '@playwright/test'
import { expect, test } from '../test'
import { chooseDepartment } from '../department-budgets'
import { csrfHeaders } from '../csrf'
import { confirmDialog, waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { journeyURL } from '../server-settings'
import { useJourneySession } from '../sessions'
import { createResource, openKind, openTab } from '../studio-resources'

// The journey server is the second server of the run, and a publish request waits behind the ones that other
// specs queue.
test.setTimeout(300_000)

interface ResourceItem {
  id: string
  kind: string
  file: string
  version: string
}

test.describe('an author publishes a module application on the journey tenant, a manager is refused its contract, and a relaxed release approves the next budget', () => {
  test.describe.configure({ mode: 'serial' })

  // The Engineering record of the sample seeds, which the department-budgets contract scenarios name.
  const engineeringId = '949c1d11-aaba-44aa-b0d1-6c9d659d6bd5'
  // Letters and digits, starting with a letter.
  const name = `Budgets${Date.now().toString(36)}`
  // The blank application's site path is the kebab-case name, which for these lower-case digits is the name in lower case.
  const sitePath = name.toLowerCase()
  const studioApi = `${journeyURL}/api/studio/apps/${name}`

  async function resources(page: Page): Promise<ResourceItem[]> {
    const response = await page.request.get(`${studioApi}/resources`)
    expect(response.status()).toBe(200)
    return ((await response.json()) as { items: ResourceItem[] }).items
  }

  async function patch(page: Page, item: ResourceItem, operations: unknown[]) {
    const response = await page.request.patch(`${studioApi}/resources/${item.id}`, {
      headers: {
        ...csrfHeaders,
        'Content-Type': 'application/json-patch+json',
        'If-Match': `"${item.version}"`,
      },
      data: JSON.stringify(operations),
    })
    expect(response.status(), await response.text()).toBe(200)
  }

  async function writeText(page: Page, key: string, text: string) {
    const response = await page.request.put(`${studioApi}/texts/${key}`, {
      headers: csrfHeaders,
      data: { texts: { en: text } },
    })
    expect(response.status(), await response.text()).toBe(200)
  }

  async function publishWithLabel(page: Page, label: string) {
    await page.goto(`${journeyURL}/studio?app=${name}`)
    await expect(page.getByRole('button', { name: 'Publish…', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Publish…', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Label (optional)', exact: true }).fill(label)
    await confirmDialog(page, 'Publish')
  }

  async function start(page: Page, budgetId: string): Promise<string> {
    const response = await page.request.post(`${journeyURL}/api/apps/${name}/processes/QuickBudgetApproval/instances`, {
      data: { subjectId: budgetId },
      headers: csrfHeaders,
    })
    expect(response.status(), await response.text()).toBe(201)
    return ((await response.json()) as { id: string }).id
  }

  async function createBudget(page: Page): Promise<{ budget: string; id: string }> {
    const budget = `Budget ${Date.now()}-${test.info().retry}`
    await page.goto(`${journeyURL}/${sitePath}/department-budgets/budgets`)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill(budget)
    await page.getByLabel(fieldLabel('Year')).fill('2027')
    await page.getByLabel(fieldLabel('Amount')).fill('1500.00')
    await chooseDepartment(page, 'Engineering')
    await page.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(page).toHaveURL(new RegExp(`/${sitePath}/department-budgets/budgets$`))
    const row = page.getByRole('row', { name: new RegExp(budget) })
    await expect(row).toBeVisible()
    const id = await row.getAttribute('data-row-key')
    expect(id).toBeTruthy()
    return { budget, id: id! }
  }

  test('an author creates a blank application, pins the modules and publishes release 1 on the journey host', async ({
    page,
  }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application…', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`app=${name}$`))

    const application = (await resources(page)).find((item) => item.file === 'application.json')!
    await patch(page, application, [
      {
        op: 'add',
        path: '/modules',
        value: [
          {
            id: 'organization',
            version: '^1.0.0',
            roles: { admin: 'department-administrator', member: 'department-manager' },
          },
          {
            id: 'department-budgets',
            version: '^1.1.0',
            roles: { approver: 'department-manager', administrator: 'department-administrator' },
          },
        ],
      },
    ])

    for (const [roleName, textKey, text] of [
      ['department-manager', 'role.departmentManager', 'Department manager'],
      ['department-administrator', 'role.departmentAdministrator', 'Department administrator'],
    ]) {
      const role = await page.request.post(`${studioApi}/resources`, {
        headers: csrfHeaders,
        data: { id: randomUUID(), kind: 'role', name: roleName, formatVersion: 1, label: { textKey } },
      })
      expect(role.status(), await role.text()).toBe(201)
      await writeText(page, textKey, text)
    }

    const policy = await page.request.post(`${studioApi}/resources`, {
      headers: csrfHeaders,
      data: {
        id: randomUUID(),
        kind: 'policy',
        name: 'DepartmentManagerAccess',
        formatVersion: 1,
        roles: ['department-manager'],
        grants: [{ name: 'budgetApprovalStart', process: 'department-budgets/BudgetApproval', actions: ['start'] }],
      },
    })
    expect(policy.status(), await policy.text()).toBe(201)

    const seed = await page.request.post(`${studioApi}/resources`, {
      headers: csrfHeaders,
      data: {
        id: randomUUID(),
        kind: 'seed',
        name: 'Departments',
        formatVersion: 1,
        entity: 'organization/Department',
        sync: true,
        records: [{ id: engineeringId, values: { code: 'ENG', name: 'Engineering' } }],
      },
    })
    expect(seed.status(), await seed.text()).toBe(201)

    const site = (await resources(page)).find((item) => item.kind === 'site')!
    await patch(page, site, [
      { op: 'add', path: '/navigation/-', value: { page: 'department-budgets/Budgets', label: { textKey: 'nav.budgets' } } },
    ])
    await writeText(page, 'nav.budgets', 'Budgets')

    await page.goto(`${journeyURL}/studio?app=${name}`)
    await createResource(page, 'process', 'QuickBudgetApproval')
    const quickApproval = (await resources(page)).find((item) => item.file === 'processes/quick-budget-approval.json')!
    await openTab(page, 'JSON')
    const content = {
      id: quickApproval.id,
      kind: 'process',
      name: 'QuickBudgetApproval',
      formatVersion: 1,
      entity: 'department-budgets/Budget',
      start: 'approve',
      steps: [
        {
          name: 'approve',
          type: 'operation',
          operation: 'updateRecord',
          set: { status: "'approved'" },
          next: 'done',
        },
        { name: 'done', type: 'end' },
      ],
    }
    await page.getByRole('textbox', { name: 'JSON', exact: true }).fill(JSON.stringify(content, null, 2))
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    const starters = await page.request.post(`${studioApi}/resources`, {
      headers: csrfHeaders,
      data: {
        id: randomUUID(),
        kind: 'policy',
        name: 'BudgetStarters',
        formatVersion: 1,
        roles: ['department-manager'],
        grants: [{ name: 'quickApprovalStart', process: 'QuickBudgetApproval', actions: ['start'] }],
      },
    })
    expect(starters.status(), await starters.text()).toBe(201)

    const diagnostics = await page.request.get(`${studioApi}/diagnostics`)
    expect(diagnostics.status()).toBe(200)
    const { diagnostics: items } = (await diagnostics.json()) as { diagnostics: { severity: string }[] }
    expect(items.filter((item) => item.severity === 'error')).toEqual([])

    const label = `Modules ${name}`
    await publishWithLabel(page, label)
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 120_000,
    })
    const row = page.getByTestId('studio-release-row').filter({ hasText: label })
    await expect(row).toBeVisible()
    await expect(row).not.toHaveAttribute('data-off-standard', 'true')
  })

  test('a department manager creates a budget on the module page, and it reads draft', async ({ page }) => {
    await useJourneySession(page, 'manager')
    const { budget } = await createBudget(page)
    await expect(page.getByRole('row', { name: new RegExp(`${budget}.*draft`, 'i') })).toBeVisible()
  })

  test('a process that approves a budget at once fails on the contract, and the platform operator sees the named point', async ({
    page,
  }) => {
    await useJourneySession(page, 'manager')
    const { budget, id } = await createBudget(page)
    const instanceId = await start(page, id)

    await useJourneySession(page, 'platform-operator')
    await expect(async () => {
      await page.goto(`${journeyURL}/console`)
      const row = page.getByTestId('console-failed-work').locator(`tr[data-row-key="${instanceId}"]`)
      await expect(row).toContainText('QuickBudgetApproval', { timeout: 2_000 })
      await expect(row).toContainText('named point Budget/transitions/approve', { timeout: 2_000 })
    }).toPass({ timeout: 30_000 })
    await expect(page.locator('[data-theme-mode="light"]').first()).toBeVisible()

    await useJourneySession(page, 'manager')
    await page.goto(`${journeyURL}/${sitePath}/department-budgets/budgets`)
    await expect(page.getByRole('row', { name: new RegExp(`${budget}.*draft`, 'i') })).toBeVisible()
  })

  test('a department manager is refused Studio and cannot add a relax entry to the module contract', async ({ page }) => {
    await useJourneySession(page, 'author')
    const application = (await resources(page)).find((item) => item.file === 'application.json')!

    await useJourneySession(page, 'manager')
    await page.goto(`${journeyURL}/studio?app=${name}`)
    await expect(page.getByTestId('studio-forbidden')).toBeVisible()
    await expect(page.getByTestId(/^studio-contract-relax-/)).toHaveCount(0)

    const response = await page.request.patch(`${studioApi}/resources/${application.id}`, {
      headers: {
        ...csrfHeaders,
        'Content-Type': 'application/json-patch+json',
        'If-Match': `"${application.version}"`,
      },
      // Index 1 is department-budgets in the application's modules.
      data: JSON.stringify([
        {
          op: 'add',
          path: '/modules/1/relax',
          value: [{ point: 'Budget/transitions/approve', reason: 'x' }],
        },
      ]),
    })
    expect(response.status(), await response.text()).toBe(403)
  })

  test('an author relaxes the approve point and publishes, and Studio and the console mark the release off-standard', async ({
    page,
  }) => {
    const stamp = Date.now().toString(36)
    const label = `Relaxed ${stamp}`
    const point = 'Budget/transitions/approve'
    const reason = `Approvers decide alone ${stamp}`

    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=${name}`)
    await openKind(page, 'contract')
    await page
      .getByTestId('studio-group-module-contracts')
      .getByRole('button', { name: 'department-budgets/BudgetContract', exact: true })
      .click()
    await page.getByTestId(`studio-contract-relax-${point}`).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Reason', exact: true }).fill(reason)
    await confirmDialog(page, 'Relax')
    await expect(page.getByTestId(`studio-contract-relaxed-${point}`)).toBeVisible()

    await publishWithLabel(page, label)
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 120_000,
    })
    const row = page.getByTestId('studio-release-row').filter({ hasText: label })
    await expect(row).toHaveAttribute('data-off-standard', 'true')
    await expect(row.getByTestId('release-off-standard')).toHaveText('Off-standard')
    await expect(row.getByTestId('release-relaxed-point')).toContainText('department-budgets')
    await expect(row.getByTestId('release-relaxed-point')).toContainText(point)
    await expect(row.getByTestId('release-relaxed-point')).toContainText(reason)
    await expect(page.locator('[data-theme-mode="light"]').first()).toBeVisible()

    await useJourneySession(page, 'platform-operator')
    await page.goto(`${journeyURL}/console`)
    const publishing = page.getByTestId('console-publishing')
    await publishing.getByRole('combobox', { name: 'Application', exact: true }).click()
    // A selected application shares its title with the option, and the dropdown renders last.
    await page.getByTitle(name, { exact: true }).last().click()
    await expect(publishing.getByTestId('console-publishing-releases')).toContainText('#')
    const consoleRow = page.getByTestId('console-publishing-releases').getByRole('row').filter({ hasText: label })
    await expect(consoleRow.getByTestId('release-off-standard')).toHaveText('Off-standard')
    await expect(consoleRow.getByTestId('release-relaxed-point')).toContainText('department-budgets')
    await expect(consoleRow.getByTestId('release-relaxed-point')).toContainText(point)
    await expect(consoleRow.getByTestId('release-relaxed-point')).toContainText(reason)
    await expect(page.locator('[data-theme-mode="light"]').first()).toBeVisible()
  })

  test('a manager starts the approval on a new budget after the relaxed release, and the budget reads approved', async ({
    page,
  }) => {
    await useJourneySession(page, 'manager')
    const { budget, id } = await createBudget(page)
    await start(page, id)

    await expect(async () => {
      await page.goto(`${journeyURL}/${sitePath}/department-budgets/budgets`)
      await expect(page.getByRole('row', { name: new RegExp(`${budget}.*approved`, 'i') })).toBeVisible({
        timeout: 2_000,
      })
    }).toPass({ timeout: 30_000 })
  })
})
