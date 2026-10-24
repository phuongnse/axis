import { expect, test, type Locator, type Page } from '@playwright/test'
import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'

// The design-system preview page is a separate build, which scripts/e2e.sh makes with `npm run build:design-system`.
// It answers every API call from fixtures, so the spec serves it from disk and needs no server.
const repositoryRoot = path.resolve(import.meta.dirname, '..', '..', '..')
const previewBuild = path.join(repositoryRoot, 'artifacts', 'design-system-app')
const productionBuild = path.join(repositoryRoot, 'src', 'Axis.Server', 'wwwroot')
const origin = 'http://design-system.test'
// The name of the fixture application in web/src/designSystem/fixtures.ts.
const fixtureMarker = 'DesignSystemSample'

const modes = ['light', 'dark'] as const

// Each preview and a locator for its widget.
const previews: Record<string, (page: Page) => Locator> = {
  table: (page) => page.getByRole('columnheader', { name: 'Title', exact: true }),
  form: (page) => page.getByRole('button', { name: 'Save', exact: true }),
  taskInbox: (page) => page.getByRole('link', { name: 'Review the request', exact: true }).first(),
  processInspector: (page) => page.getByRole('combobox', { name: 'State', exact: true }),
  detail: (page) => page.getByText('Standing desks for the new office', { exact: true }),
  'chart-bar': (page) => page.getByRole('img', { name: 'Requests by status', exact: true }).locator('svg'),
  'chart-line': (page) => page.getByRole('img', { name: 'Requests by status', exact: true }).locator('svg'),
  'chart-pie': (page) => page.getByRole('img', { name: 'Requests by status', exact: true }).locator('svg'),
  kpi: (page) => page.getByTestId('kpi-value'),
  columns: (page) => page.getByRole('region', { name: 'Tasks', exact: true }),
  tabs: (page) => page.getByRole('tab', { name: 'Requests', exact: true }),
  section: (page) => page.getByRole('region', { name: 'Tasks', exact: true }),
  'field-text': (page) => page.getByLabel('Title', { exact: true }),
  'field-integer': (page) => page.getByLabel('Quantity', { exact: true }),
  'field-decimal': (page) => page.getByLabel('Amount', { exact: true }),
  'field-boolean': (page) => page.getByLabel('Urgent', { exact: true }),
  'field-date': (page) => page.getByLabel('Needed on', { exact: true }),
  'field-date-time': (page) => page.getByLabel('Submitted at', { exact: true }),
  'field-enum': (page) => page.getByLabel('Status', { exact: true }),
  'field-reference': (page) => page.getByLabel('Category', { exact: true }),
  'field-child-collection': (page) => page.getByRole('columnheader', { name: 'Description', exact: true }),
}

/** Files of a folder with their paths relative to it, or none when the folder does not exist. */
function filesUnder(folder: string): string[] {
  try {
    return readdirSync(folder, { recursive: true, withFileTypes: true })
      .filter((entry) => entry.isFile())
      .map((entry) => path.relative(folder, path.join(entry.parentPath, entry.name)))
  } catch {
    return []
  }
}

/** Opens the preview build from disk and records every console error and page error. */
async function openPreview(page: Page, query: string): Promise<string[]> {
  const errors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error') {
      errors.push(message.text())
    }
  })
  page.on('pageerror', (error) => errors.push(error.message))
  await page.route(`${origin}/**`, async (route) => {
    const { pathname } = new URL(route.request().url())
    const file = path.join(previewBuild, pathname === '/' ? 'design-system.html' : pathname)
    if (path.relative(previewBuild, file).startsWith('..')) {
      await route.fulfill({ status: 404 })
      return
    }
    await route.fulfill({ path: file }).catch(() => route.fulfill({ status: 404 }))
  })
  await page.goto(`${origin}/design-system.html${query}`)
  return errors
}

for (const [name, widget] of Object.entries(previews)) {
  for (const mode of modes) {
    test(`the ${name} preview draws its widget in ${mode} mode with no error`, async ({ page }) => {
      const errors = await openPreview(page, `?preview=${name}&mode=${mode}`)

      await expect(widget(page)).toBeVisible()
      await page.waitForLoadState('networkidle')

      expect(errors).toEqual([])
    })
  }
}

test('the index lists each preview in light and dark mode', async ({ page }) => {
  const errors = await openPreview(page, '')

  await expect(page.getByRole('heading', { name: 'Axis design system', exact: true })).toBeVisible()
  const hrefs = await page.getByRole('link').evaluateAll((links) => links.map((link) => link.getAttribute('href')))
  expect(hrefs).toEqual(
    Object.keys(previews).flatMap((name) => modes.map((mode) => `?preview=${name}&mode=${mode}`)),
  )
  expect(errors).toEqual([])
})

test('light and dark mode draw different background colours', async ({ page }) => {
  const errors = await openPreview(page, '?preview=table&mode=light')
  await expect(page.getByRole('main')).toBeVisible()
  const light = await page.getByRole('main').evaluate((main) => getComputedStyle(main).backgroundColor)

  await page.goto(`${origin}/design-system.html?preview=table&mode=dark`)
  await expect(page.getByRole('main')).toBeVisible()
  const dark = await page.getByRole('main').evaluate((main) => getComputedStyle(main).backgroundColor)

  expect(dark).not.toBe(light)
  expect(errors).toEqual([])
})

test('only the preview build holds the previews, and the production SPA does not', () => {
  const previewFiles = filesUnder(previewBuild)
  expect(previewFiles).toContain('design-system.html')
  expect(previewFiles.some((file) => readFileSync(path.join(previewBuild, file), 'utf8').includes(fixtureMarker))).toBe(
    true,
  )

  const productionFiles = filesUnder(productionBuild)
  // An empty web root would pass the checks below for no reason.
  expect(productionFiles).toContain('index.html')
  expect(productionFiles.filter((file) => path.basename(file) === 'design-system.html')).toEqual([])
  expect(
    productionFiles.filter((file) => readFileSync(path.join(productionBuild, file), 'utf8').includes(fixtureMarker)),
  ).toEqual([])
})
