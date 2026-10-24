import { expect, test } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { noSideways, phoneWidth } from '../phone'
import { pickOption } from '../select'
import { sessionFile, useSession } from '../sessions'

// At phone width, anna creates a draft ticket and opens it from the folded row actions, and linh sees the Dashboard.
test.use({ storageState: sessionFile('anna'), viewport: { width: phoneWidth, height: 812 } })

const appPath = '/api/apps/ServiceDesk'
const recordsPath = `${appPath}/entities/Ticket/records`

test('anna creates a draft ticket at phone width and opens it from the folded row actions', async ({ page }) => {
  const title = `Zz Phone ${Date.now()}-${test.info().retry}`
  await page.goto('/service-desk/mytickets?pageSize=100')
  await expect(page.getByRole('table').first()).toBeVisible()
  await noSideways(page)

  const menu = page.getByRole('button', { name: 'Menu', exact: true })
  const myTickets = page.getByRole('link', { name: 'My tickets', exact: true })
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  await menu.click()
  await expect(menu).toHaveAttribute('aria-expanded', 'true')
  await expect(myTickets).toBeVisible()
  await noSideways(page)
  await menu.click()
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  await expect(myTickets).toBeHidden()

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/service-desk\/ticketform\/new$/)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await page.getByRole('textbox', { name: 'Description', exact: true }).fill('It shows a black screen.')
  await noSideways(page)
  await page.getByRole('button', { name: 'Next', exact: true }).click()
  await pickOption(page.getByRole('combobox', { name: 'Status', exact: true }), 'draft')
  await noSideways(page)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(/\/service-desk\/mytickets\?pageSize=100$/)

  // The row actions fold into one Actions menu on a phone.
  const row = page.getByRole('row', { name: new RegExp(title) })
  await expect(row.getByRole('button')).toHaveCount(1)
  await row.getByRole('button', { name: 'Actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'View', exact: true }).click()

  await expect(page).toHaveURL(/\/service-desk\/ticketdetail\?ticket=[^&]+$/)
  await expect(page.getByTestId('detail')).toContainText(title)
  await expect(page.getByRole('textbox')).toHaveCount(0)
  await noSideways(page)
})

test('linh sees the KPI tiles and the chart stacked below them on the Dashboard at phone width', async ({
  page,
  request,
}) => {
  // One open ticket, so the tiles and the chart have something to count.
  const created = await request.post(recordsPath, {
    data: {
      values: {
        title: `Zz Phone dashboard ${Date.now()}-${test.info().retry}`,
        description: 'It shows a black screen.',
        status: 'draft',
      },
    },
    headers: csrfHeaders,
  })
  expect(created.status()).toBe(201)
  const { id } = (await created.json()) as { id: string }
  const started = await request.post(`${appPath}/processes/TicketHandling/instances`, {
    data: { subjectId: id },
    headers: csrfHeaders,
  })
  expect(started.status()).toBe(201)

  await useSession(page, 'linh')
  await page.goto('/service-desk/dashboard')

  const tiles = page.getByTestId('kpi-tile')
  const chart = page.getByTestId('chart-widget')
  // The chart shows its title while it loads, so wait for its data. The worker moves the ticket to
  // submitted some time after the start, so the chart can show its empty state first and needs a reload.
  await expect(async () => {
    await page.reload()
    await expect(chart).toHaveAttribute('aria-busy', 'false', { timeout: 5_000 })
    await expect(page.getByTestId('chart-empty')).toHaveCount(0)
  }).toPass({ timeout: 30_000 })
  await expect(chart).toContainText('Open tickets by category')
  await expect(page.getByTestId('chart-empty')).toHaveCount(0)
  await expect(page.getByTestId('chart-error')).toHaveCount(0)
  // Specs create tickets in parallel, so the numbers change from run to run. The test checks that they load.
  await expect(tiles).toHaveCount(2)
  await expect(tiles.nth(0)).toContainText('Open tickets')
  await expect(tiles.nth(1)).toContainText('Urgent tickets')
  await expect(page.getByTestId('kpi-value')).toHaveCount(2)
  await expect(page.getByTestId('kpi-error')).toHaveCount(0)

  const kpiColumn = await page.getByTestId('column').filter({ has: tiles.first() }).boundingBox()
  const chartColumn = await page.getByTestId('column').filter({ has: chart }).boundingBox()
  expect(kpiColumn).not.toBeNull()
  expect(chartColumn).not.toBeNull()
  // The chart column starts at the same left edge and sits below the KPI column.
  expect(Math.abs(chartColumn!.x - kpiColumn!.x)).toBeLessThanOrEqual(1)
  expect(chartColumn!.y).toBeGreaterThanOrEqual(kpiColumn!.y + kpiColumn!.height - 1)
  await noSideways(page)
})
