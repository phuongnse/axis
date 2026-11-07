import { expect, test } from '../test'
import { useSession } from '../sessions'
import { openFormTab, openTab } from '../studio-resources'

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

test.describe('the Form tab helper waits for the expression checks', () => {
  test('the Form tab opens only after every held check has answered', async ({ page }) => {
    await useSession(page, 'author')
    const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const id = items.find((item) => item.file === 'processes/purchase-request-approval.json')!.id
    let sent = 0
    let answered = 0
    await page.route('**/expressions/check', async (route) => {
      sent++
      try {
        await new Promise((resolve) => setTimeout(resolve, 1500))
        const response = await route.fetch()
        answered++
        await route.fulfill({ response })
      } catch {
        // The page aborts a check when its editor goes away.
      }
    })

    await page.goto(`/studio?app=PurchaseRequests&resource=${id}`)
    await openFormTab(page)

    expect(sent).toBeGreaterThan(0)
    expect(answered).toBe(sent)
    await page.unrouteAll({ behavior: 'ignoreErrors' })
  })
})
