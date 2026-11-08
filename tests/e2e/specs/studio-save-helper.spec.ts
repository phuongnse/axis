import { expect, test } from '../test'
import { useSession } from '../sessions'
import { openFormTab, saveResource } from '../studio-resources'

// The process of the purchase request sample, which Studio on the main E2E server edits as a copy.
const processFile = 'processes/purchase-request-approval.json'

test.describe('the save helper waits for a save that is still running', () => {
  test('a save held for seven seconds still shows Saved', async ({ page }) => {
    await useSession(page, 'author')
    const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const id = items.find((item) => item.file === processFile)!.id
    let held = false
    // The held save is answered here and never reaches the server, so the shared copy of the sample stays as it is.
    await page.route('**/api/studio/apps/PurchaseRequests/resources/*', async (route) => {
      if (route.request().method() !== 'PATCH') {
        await route.fallback()
        return
      }
      held = true
      await new Promise((resolve) => setTimeout(resolve, 7000))
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ version: 'held', diagnostics: [] }),
      })
    })

    await page.goto(`/studio?app=PurchaseRequests&resource=${id}`)
    await openFormTab(page)
    await page
      .getByTestId('studio-step-1')
      .getByRole('textbox', { name: 'Due in', exact: true })
      .fill('P4D')
    await saveResource(page)

    expect(held).toBe(true)
    await page.unrouteAll({ behavior: 'ignoreErrors' })
  })
})
