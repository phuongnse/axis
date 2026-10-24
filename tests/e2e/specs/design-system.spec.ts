import { expect, test, type Locator, type Page } from '@playwright/test'
import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { waitForDialog } from '../dialogs'

// The design-system bundle is a separate build, which scripts/e2e.sh makes with scripts/design-system.sh. The page
// web/design-system.html is its harness. The bundle answers every API call of the fixture application from fixtures,
// so the spec serves both from disk and needs no server.
const origin = 'http://design-system.test'
const repositoryRoot = path.resolve(import.meta.dirname, '..', '..', '..')
const harnessFile = path.join(repositoryRoot, 'web', 'design-system.html')
const bundleFolder = path.join(repositoryRoot, 'artifacts', 'design-system')
const bundleFile = path.join(bundleFolder, 'axis-design-system.js')
const harnessUrl = `${origin}/design-system.html`
const bundleUrl = `${origin}/artifacts/design-system/axis-design-system.js`
const productionBuild = path.join(repositoryRoot, 'src', 'Axis.Server', 'wwwroot')
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

/** What a page records while it loads the harness: console errors, page errors and every request. */
interface Recording {
  errors: string[]
  requests: string[]
}

/** Opens the harness from disk with the bundle next to it, and records errors and requests. */
async function openHarness(page: Page, query: string): Promise<Recording> {
  const recording: Recording = { errors: [], requests: [] }
  page.on('console', (message) => {
    if (message.type() === 'error') {
      recording.errors.push(message.text())
    }
  })
  page.on('pageerror', (error) => recording.errors.push(error.message))
  page.on('request', (request) => recording.requests.push(request.url()))
  await page.route(`${origin}/**`, async (route) => {
    const { pathname } = new URL(route.request().url())
    if (pathname === '/design-system.html') {
      await route.fulfill({ path: harnessFile })
    } else if (pathname.startsWith('/artifacts/design-system/')) {
      const file = path.join(bundleFolder, path.basename(pathname))
      await route.fulfill({ path: file }).catch(() => route.fulfill({ status: 404 }))
    } else {
      await route.fulfill({ status: 404 })
    }
  })
  await page.goto(`${harnessUrl}${query}`)
  return recording
}

for (const [name, widget] of Object.entries(previews)) {
  for (const mode of modes) {
    test(`the ${name} preview mounts its widget from the bundle in ${mode} mode with no error`, async ({ page }) => {
      const recording = await openHarness(page, `?preview=${name}&mode=${mode}`)

      await expect(widget(page)).toBeVisible()
      await page.waitForLoadState('networkidle')

      if (name.startsWith('field-')) {
        await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0)
        await expect(page.getByRole('button', { name: 'Submit for approval', exact: true })).toHaveCount(0)
        // The history card's title is not an ARIA heading, so the test id and the text are checked.
        await expect(page.getByTestId('record-history')).toHaveCount(0)
        await expect(page.getByText('History', { exact: true })).toHaveCount(0)
      }

      expect(recording.errors).toEqual([])
      // The fixtures answer every API call, so nothing but the harness and the bundle is requested.
      expect(recording.requests.filter((url) => !url.startsWith('data:'))).toEqual([
        `${harnessUrl}?preview=${name}&mode=${mode}`,
        bundleUrl,
      ])
    })
  }
}

test('the field-date-time preview opens its date picker', async ({ page }) => {
  const recording = await openHarness(page, '?preview=field-date-time&mode=light')
  await page.getByLabel('Submitted at', { exact: true }).click()

  await expect(page.getByRole('button', { name: 'Next month', exact: true })).toBeVisible()
  expect(recording.errors).toEqual([])
})

test('the field-reference preview lists the fixture categories in its lookup', async ({ page }) => {
  const recording = await openHarness(page, '?preview=field-reference&mode=light')
  await page.getByTestId('reference-category').getByRole('button', { name: 'Choose', exact: true }).click()

  const dialog = await waitForDialog(page, 'Category')
  await expect(dialog).toContainText('Office furniture')
  expect(recording.errors).toEqual([])
})

test('the field-child-collection preview adds a row', async ({ page }) => {
  const recording = await openHarness(page, '?preview=field-child-collection&mode=light')
  await expect(page.getByLabel('Description 2', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Add row', exact: true }).click()

  await expect(page.getByLabel('Description 3', { exact: true })).toBeVisible()
  expect(recording.errors).toEqual([])
})

test('a mounted table redraws in the other mode when the page changes data-theme', async ({ page }) => {
  const recording = await openHarness(page, '?preview=table&mode=light')
  const header = page.getByRole('columnheader', { name: 'Title', exact: true })
  await expect(header).toBeVisible()
  const background = () => header.evaluate((cell) => getComputedStyle(cell).backgroundColor)
  const light = await background()

  await page.evaluate(() => {
    document.documentElement.dataset.theme = 'dark'
  })

  await expect.poll(background).not.toBe(light)
  expect(recording.errors).toEqual([])
})

test("a mounted form uses the page's --primary and redraws when it changes", async ({ page }) => {
  const recording = await openHarness(page, '?preview=table&mode=light')
  await expect(previews.table(page)).toBeVisible()

  // The harness mounts its previews at load, so the test sets the property and then mounts the form itself.
  await page.evaluate(() => {
    document.documentElement.style.setProperty('--primary', '#13c2c2')
    const element = document.createElement('div')
    document.getElementById('previews')!.appendChild(element)
    ;(window as unknown as { Axis: { form: (element: HTMLElement) => void } }).Axis.form(element)
  })

  const save = page.getByRole('button', { name: 'Save', exact: true })
  const background = () => save.evaluate((button) => getComputedStyle(button).backgroundColor)
  await expect.poll(background).toBe('rgb(19, 194, 194)')

  await page.evaluate(() => document.documentElement.style.setProperty('--primary', '#eb2f96'))

  await expect.poll(background).toBe('rgb(235, 47, 150)')
  expect(recording.errors).toEqual([])
})

test('the bundle sets window.Axis with one mount for each preview', async ({ page }) => {
  const recording = await openHarness(page, '')

  const mounts = await page.evaluate(() => Object.keys((window as unknown as { Axis: object }).Axis))
  expect(mounts.sort()).toEqual(Object.keys(previews).sort())
  await expect(page.locator('[data-preview]')).toHaveCount(Object.keys(previews).length)
  await page.waitForLoadState('networkidle')
  expect(recording.errors).toEqual([])
})

test('only the bundle holds the fixtures, and the production SPA does not', () => {
  expect(readFileSync(bundleFile, 'utf8')).toContain(fixtureMarker)

  const productionFiles = filesUnder(productionBuild)
  // An empty web root would pass the checks below for no reason.
  expect(productionFiles).toContain('index.html')
  expect(productionFiles.filter((file) => path.basename(file) === 'design-system.html')).toEqual([])
  expect(
    productionFiles.filter((file) => readFileSync(path.join(productionBuild, file), 'utf8').includes(fixtureMarker)),
  ).toEqual([])
})
