import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { confirmDialog, waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { expectChosen, pickOption } from '../select'
import { journeyURL } from '../server-settings'
import { useJourneySession } from '../sessions'

// The publisher queue is shared with the service desk journey, so the Studio steps can wait a while.
test.setTimeout(300_000)

test.describe('the author builds an application from the request-approval template', () => {
  // Each step builds on the application the one before created, so they run in order.
  test.describe.configure({ mode: 'serial' })

  async function waitForTask(page: Page, title: string) {
    await expect(async () => {
      await page.goto(`${journeyURL}/equipment-requests/mytasks?pageSize=100`)
      await expect(page.getByRole('row', { name: new RegExp(title) })).toBeVisible({ timeout: 1_000 })
    }).toPass({ timeout: 30_000 })
  }

  /** Opens the task `label` of the request `title` in My tasks. It waits until the worker creates the task. */
  async function openTask(page: Page, title: string, label: string) {
    await waitForTask(page, title)
    await page.getByRole('row', { name: new RegExp(title) }).getByRole('link', { name: label, exact: true }).click()
  }

  test('the author creates EquipmentRequests from the Request approval template', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('EquipmentRequests')
    await dialog.getByRole('radio', { name: new RegExp('^Request approval') }).click()
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(/app=EquipmentRequests$/)

    const response = await page.request.get(`${journeyURL}/api/studio/apps/EquipmentRequests/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  })

  test('the author runs the test scenario from Studio, and it passes', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=EquipmentRequests`)
    await page.getByTestId('studio-run-tests').click()

    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 240_000,
    })
    await expect(page.getByTestId('studio-test-result')).toHaveCount(1)
    await expect(page.locator('[data-testid="studio-test-result"][data-outcome="passed"]')).toHaveCount(1)
  })

  test('the author publishes EquipmentRequests, and release 1 is active', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio?app=EquipmentRequests`)

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

  test('anna submits a request on the published site, and minh approves it', async ({ page }) => {
    const title = `Zz Request ${Date.now()}`

    await useJourneySession(page, 'anna')
    await page.goto(`${journeyURL}/equipment-requests/myrequests`)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(/\/equipment-requests\/requestform\/new$/)
    await page.getByRole('textbox', { name: 'Title', exact: true }).fill(title)
    await page.getByRole('textbox', { name: 'Description', exact: true }).fill('The current one is too slow.')
    // The requester's create grant only allows the status draft.
    await pickOption(page.getByRole('combobox', { name: 'Status', exact: true }), 'draft')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page).toHaveURL(/\/equipment-requests\/myrequests$/)

    await page.getByRole('row', { name: new RegExp(title) }).getByRole('link').click()
    await expect(page).toHaveURL(/\/equipment-requests\/requestform\/[^/]+$/)
    const id = page.url().split('/').pop()
    await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue(title)
    await page.getByRole('button', { name: 'Submit', exact: true }).click()
    await expect(page.getByTestId('form-started')).toBeVisible()

    await useJourneySession(page, 'minh')
    await waitForTask(page, title)

    await useJourneySession(page, 'anna')
    await page.goto(`${journeyURL}/equipment-requests/mytasks?pageSize=100`)
    await expect(page.getByRole('table')).toBeVisible()
    await expect(page.getByRole('row', { name: new RegExp(title) })).toHaveCount(0)

    await useJourneySession(page, 'minh')
    await openTask(page, title, 'Approval')
    await page.getByLabel(fieldLabel('Decision comment')).fill('Approved')
    await page.getByRole('button', { name: 'Approve', exact: true }).click()
    await expect(page).toHaveURL(/\/equipment-requests\/mytasks$/)

    // The worker ends the process some time after the decision.
    await useJourneySession(page, 'anna')
    await expect(async () => {
      await page.goto(`${journeyURL}/equipment-requests/requestform/${id}`)
      await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), 'approved', { timeout: 1_000 })
    }).toPass({ timeout: 30_000 })
  })
})
