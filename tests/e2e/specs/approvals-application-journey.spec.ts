import { expect, test } from '../test'
import { confirmDialog, waitForDialog } from '../dialogs'
import { journeyURL } from '../server-settings'
import { useJourneySession } from '../sessions'

// The publisher runs the contract scenarios of both modules, and its queue is shared with the other journeys.
test.setTimeout(420_000)

test.describe('the author builds an application from the built-in Approvals application', () => {
  // Each step builds on the application the one before created, so they run in order.
  test.describe.configure({ mode: 'serial' })

  test('the author creates OfficeApprovals from Approvals, and it compiles without errors', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application…', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('OfficeApprovals')
    await dialog.getByRole('radio', { name: new RegExp('^Approvals') }).click()
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(/app=OfficeApprovals$/)

    const response = await page.request.get(`${journeyURL}/api/studio/apps/OfficeApprovals/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  })

  test('the author publishes OfficeApprovals, and release 1 is active', async ({ page }) => {
    await useJourneySession(page, 'author')
    await page.goto(`${journeyURL}/studio?app=OfficeApprovals&view=releases`)

    await page.getByRole('button', { name: 'Publish…', exact: true }).click()
    await confirmDialog(page, 'Publish')
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 300_000,
    })
    const rows = page.getByTestId('studio-release-row')
    await expect(rows).toHaveCount(1)
    await expect(rows).toHaveAttribute('data-number', '1')
    await expect(rows).toHaveAttribute('data-active', 'true')
  })

  test('an employee opens My requests and is refused All requests', async ({ page }) => {
    await useJourneySession(page, 'employee')
    await page.goto(`${journeyURL}/office-approvals`)

    await expect(page.getByRole('menuitem', { name: 'All requests', exact: true })).toHaveCount(0)
    await page.getByRole('menuitem', { name: 'My requests', exact: true }).click()
    await expect(page).toHaveURL(/\/office-approvals\/approvals\/myrequests$/)
    await expect(page.getByRole('table')).toBeVisible()

    const refused = await page.request.get(`${journeyURL}/api/sites/office-approvals/pages/approvals/AllRequests`)
    expect(refused.status()).toBe(403)
  })
})
