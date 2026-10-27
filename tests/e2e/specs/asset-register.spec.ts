import { readFile } from 'node:fs/promises'
import { expect, test } from '../test'
import { csrfHeaders } from '../csrf'
import { useSession } from '../sessions'

// The asset register runs on the same platform as the other samples. quan, the asset manager, sees every
// asset, where an employee sees only the assets they hold. Specs run in parallel against one database, so
// this test relies only on the assets it creates, each named with a unique part of its own.
const assetsPath = '/api/apps/AssetManagement/entities/Asset/records'

test('the asset register finds an asset by name from its search box', async ({ page }) => {
  await useSession(page, 'quan')
  const run = `${Date.now()}-${test.info().retry}`
  const laptop = `Laptop ${run}`
  const monitor = `Monitor ${run}`
  for (const name of [laptop, monitor]) {
    const response = await page.request.post(assetsPath, {
      data: { values: { name, currency: 'USD' } },
      headers: csrfHeaders,
    })
    expect(response.status()).toBe(201)
  }

  await page.goto('/asset-management/assets')
  await page.getByRole('searchbox', { name: 'Search', exact: true }).fill(laptop)
  await page.keyboard.press('Enter')

  await expect(page).toHaveURL(/[?&]search=/)
  await expect(page.getByRole('row', { name: new RegExp(laptop) })).toHaveCount(1)
  await expect(page.getByRole('row', { name: new RegExp(monitor) })).toHaveCount(0)
})

test('an asset manager exports the searched assets to CSV and finds only the match', async ({ page }) => {
  await useSession(page, 'quan')
  const run = `${Date.now()}-${test.info().retry}`
  const printer = `Printer ${run}`
  const scanner = `Scanner ${run}`
  for (const name of [printer, scanner]) {
    const response = await page.request.post(assetsPath, {
      data: { values: { name, currency: 'USD' } },
      headers: csrfHeaders,
    })
    expect(response.status()).toBe(201)
  }

  await page.goto('/asset-management/assets')
  await page.getByRole('searchbox', { name: 'Search', exact: true }).fill(printer)
  await page.keyboard.press('Enter')
  await expect(page.getByRole('row', { name: new RegExp(printer) })).toHaveCount(1)

  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Export', exact: true }).click()
  await page.getByRole('menuitem', { name: 'CSV', exact: true }).click()
  const download = await downloading

  expect(download.suggestedFilename()).toBe('Asset.csv')
  const file = await readFile((await download.path())!, 'utf8')
  expect(file).toContain(printer)
  expect(file).not.toContain(scanner)
})

test('an employee sees the assets and no Export button', async ({ page }) => {
  await useSession(page, 'lan')

  await page.goto('/asset-management/assets')

  await expect(page.getByRole('table')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Export', exact: true })).toHaveCount(0)
})
