import { expect, test } from '../test'
import { useSession } from '../sessions'
import { openTab } from '../studio-resources'

// A page in the purchase request sample, which Studio on the main E2E server edits as a copy.
const pageId = '9b707fe9-a837-433e-8062-48a2a9b4d62a'

// The test saves nothing, so the copy of the sample that the other specs use stays as it is.
test.describe('the tab helper selects a tab when a click is lost', () => {
  test('the JSON tab opens although its first click is dropped', async ({ page }) => {
    await useSession(page, 'author')
    await page.goto(`/studio?app=PurchaseRequests&resource=${pageId}`)
    await expect(page.getByRole('tab', { name: 'JSON', exact: true })).toBeVisible()
    await page.evaluate(() => {
      window.addEventListener(
        'click',
        (event) => {
          const tab = (event.target as Element).closest('[role="tab"]')
          if (tab?.textContent === 'JSON' && !document.body.dataset.dropped) {
            document.body.dataset.dropped = '1'
            event.stopImmediatePropagation()
          }
        },
        { capture: true },
      )
    })

    await openTab(page, 'JSON')

    await expect(page.getByRole('textbox', { name: 'JSON', exact: true })).toBeVisible()
    expect(await page.evaluate(() => document.body.dataset.dropped)).toBe('1')
  })
})
