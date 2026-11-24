import { expect, test } from '../test'
import { useSession } from '../sessions'

// The publish page of the purchase request sample on the main E2E server. The test saves nothing. It changes two answers
// on the way: the releases answer, so the folder differs from the live release, and the live diff, which gains one
// changed adapter. The server itself never holds a package file, as no package can be published yet.
test.describe('the publish page draws a changed package file as a line diff', () => {
  test('the removed and added lines of a changed adapter show in the publish page', async ({ page }) => {
    await useSession(page, 'author')
    await page.route('**/api/studio/apps/PurchaseRequests/releases', async (route) => {
      const response = await route.fetch()
      const body = (await response.json()) as {
        active: { contentHash: string } | null
        folderContentHash: string | null
      }
      if (body.active !== null) {
        body.folderContentHash = `${body.active.contentHash}-changed`
      }
      await route.fulfill({ response, json: body })
    })
    await page.route('**/api/studio/apps/PurchaseRequests/releases/live/diff*', async (route) => {
      const response = await route.fetch()
      // A folder with no revision history has no live diff, which answers 404. Then the page gets the adapter alone.
      const body = response.ok()
        ? ((await response.json()) as { release: number; resources: unknown[]; cursor: string | null })
        : { release: 0, resources: [], cursor: null }
      // The live diff comes in pages, so the adapter joins the last one only, once.
      if (body.cursor === null) {
        body.resources.push({
          file: 'packages/approval-rules/adapter.py',
          change: 'changed',
          id: null,
          kind: null,
          name: null,
          titles: null,
          target: null,
          by: [],
          before: 'def limit():\n    return 1000\n',
          after: 'def limit():\n    return 5000\n',
        })
      }
      await route.fulfill({ json: body })
    })

    await page.goto('/studio?app=PurchaseRequests&view=publish')

    const adapter = page.getByRole('textbox', { name: 'packages/approval-rules/adapter.py', exact: true })
    await expect(adapter).toBeVisible()
    await expect(adapter.getByText('return 1000', { exact: true })).toBeVisible()
    await expect(adapter.getByText('return 5000', { exact: true })).toBeVisible()

    await page.unrouteAll({ behavior: 'ignoreErrors' })
  })
})
