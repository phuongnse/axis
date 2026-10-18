import { expect, test } from '@playwright/test'
import { useSession } from '../sessions'

test('an author follows the Studio link, searches for a resource and opens it', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const entity = items.find((item) => item.file === 'entities/purchase-request.json')!

  await page.goto('/')
  await page.getByRole('banner').getByRole('link', { name: 'Studio' }).click()
  // The sample is the only application, so it is picked without a choice.
  await expect(page).toHaveURL(/\/studio\?app=PurchaseRequests$/)
  await page.getByRole('searchbox', { name: 'Search by name or id' }).fill('PurchaseRequest')
  await page.getByTestId(`studio-resource-${entity.id}`).click()

  await expect(page.getByTestId('studio-resource-details')).toContainText('entities/purchase-request.json')
  await expect(page).toHaveURL(/resource=/)
  await expect(page.getByTestId('studio-diagnostics')).toBeVisible()
})

test('someone without the author role sees no Studio link and a forbidden page', async ({ page }) => {
  await useSession(page, 'anna')

  await page.goto('/')
  const banner = page.getByRole('banner')
  await expect(banner.getByRole('button', { name: 'Anna Employee' })).toBeVisible()
  await expect(banner.getByRole('link', { name: 'Studio' })).toHaveCount(0)
  await page.goto('/studio')

  await expect(page.getByTestId('studio-forbidden')).toBeVisible()
})
