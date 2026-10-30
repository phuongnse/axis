import { expect, test } from '../test'
import type { FrameLocator, Locator, Page } from '@playwright/test'
import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { waitForDialog } from '../dialogs'
import { cellValue, expectShown, fieldLabel, fieldValue } from '../fields'
import { cardGroups, cardName } from '../../../web/src/designSystem/cardOrder'

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

/** The figure of one state of a preview, found by its caption. */
const figure = (page: Page | FrameLocator, caption: string) => page.getByRole('figure', { name: caption, exact: true })

// Each preview and a locator for its widget. The table and the kpi also draw an empty state, so they are looked up in
// the "With data" figure. A field preview's first state is its editable one.
const previews: Record<string, (page: Page | FrameLocator) => Locator> = {
  table: (page) => figure(page, 'With data').getByRole('columnheader', { name: 'Title', exact: true }),
  form: (page) => page.getByRole('button', { name: 'Save', exact: true }).first(),
  taskInbox: (page) => page.getByRole('link', { name: 'Review the request', exact: true }).first(),
  processInspector: (page) => page.getByRole('combobox', { name: 'State', exact: true }),
  detail: (page) => page.getByText('Standing desks for the new office', { exact: true }),
  'chart-bar': (page) => page.getByRole('img', { name: 'Requests by status', exact: true }).locator('svg'),
  'chart-line': (page) => page.getByRole('img', { name: 'Requests by status', exact: true }).locator('svg'),
  'chart-pie': (page) => page.getByRole('img', { name: 'Requests by status', exact: true }).locator('svg'),
  kpi: (page) => figure(page, 'With data').getByTestId('kpi-value'),
  columns: (page) => page.getByRole('region', { name: 'Tasks', exact: true }),
  tabs: (page) => page.getByRole('tab', { name: 'Requests', exact: true }),
  section: (page) => page.getByRole('region', { name: 'Tasks', exact: true }),
  states: (page) => page.getByRole('alert').filter({ hasText: 'The records could not be loaded.' }),
  'field-text': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Title')),
  'field-integer': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Quantity')),
  'field-decimal': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Amount')),
  'field-boolean': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Urgent')),
  'field-date': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Needed on')),
  'field-date-time': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Submitted at')),
  'field-enum': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Status')),
  'field-reference': (page) => figure(page, 'Editable').getByLabel(fieldLabel('Category')),
  'field-child-collection': (page) =>
    figure(page, 'Reference and computed cells').getByRole('columnheader', { name: 'Description', exact: true }),
  shell: (page) => figure(page, 'Application site').getByRole('menu'),
  signIn: (page) => figure(page, 'Signed out').getByRole('button', { name: 'Sign in', exact: true }),
  identitySignIn: (page) => figure(page, 'Sign in').getByLabel('Email or username', { exact: true }),
}

/** A key element of each state of the shell, sign-in and identity sign-in previews, relative to the state's figure. */
const stateKeys: Record<string, Record<string, (state: Locator) => Locator>> = {
  shell: {
    'Application site': (state) => state.getByRole('menu'),
    'Navigation collapsed': (state) => state.locator('button[aria-controls="shell-sidebar"][aria-expanded="false"]'),
    'Form page': (state) => state.getByRole('button', { name: 'Save', exact: true }).first(),
    'Platform site': (state) => state.getByText('Purchase requests', { exact: true }).first(),
    'App switcher': (state) =>
      state
        .getByRole('menu', { name: 'Switch to', exact: true })
        .getByRole('link', { name: 'Purchase requests', exact: true })
        .and(state.locator('[aria-current="page"]')),
    'Language and theme': (state) => state.getByRole('menuitemradio', { name: 'System', exact: true }),
  },
  signIn: {
    'Signed out': (state) => state.getByRole('button', { name: 'Sign in', exact: true }),
    'Sign-in refused': (state) => state.getByTestId('sign-in-failed'),
    'Session ended': (state) => state.getByTestId('sign-in-prompt'),
  },
  identitySignIn: {
    'Sign in': (state) => state.getByLabel('Email or username', { exact: true }),
    'Wrong password': (state) => state.getByText('Invalid email, username or password.', { exact: true }),
    'Organization sign-in only': (state) =>
      state.getByRole('button', { name: 'Continue with Contoso ID', exact: true }),
    'Two-step verification': (state) => state.getByText('Verification code', { exact: true }),
    'Forgot password': (state) => state.getByRole('button', { name: 'Send link', exact: true }),
    'Set password': (state) => state.getByLabel('Confirm password', { exact: true }),
    'Add an authenticator': (state) => state.getByText('Device name', { exact: true }),
  },
}

/** The text a preview's empty state shows, or none for a preview that draws no empty state. */
function emptyTextOf(name: string): string | undefined {
  if (name === 'table') {
    return 'No records yet.'
  }
  if (name === 'taskInbox') {
    return 'No open tasks.'
  }
  if (name === 'detail') {
    // Ant Design's placeholder for the empty Lines table.
    return 'No data'
  }
  if (name === 'kpi') {
    // The dash is what the eye sees. The words are visually hidden for assistive technology.
    return '—'
  }
  if (name.startsWith('chart-')) {
    return 'No data to show'
  }
  return undefined
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

/**
 * Opens the harness from disk with the bundle next to it, and records errors and requests. When `sandboxed`, it opens
 * a page that holds the harness in an iframe sandboxed as the Design System artifact's card frames are, without
 * `allow-forms`. The iframe is `frameWidth` pixels wide, or the width of the page when none is given.
 */
async function openHarness(
  page: Page,
  query: string,
  options: { sandboxed?: boolean; frameWidth?: number } = {},
): Promise<Recording> {
  const width = options.frameWidth === undefined ? '100%' : `${options.frameWidth}px`
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
    } else if (pathname === '/sandboxed.html') {
      await route.fulfill({
        contentType: 'text/html',
        body: `<iframe sandbox="allow-scripts allow-same-origin" src="/design-system.html${query}" style="width:${width};height:100vh;border:0"></iframe>`,
      })
    } else if (pathname.startsWith('/artifacts/design-system/')) {
      const file = path.join(bundleFolder, path.basename(pathname))
      await route.fulfill({ path: file }).catch(() => route.fulfill({ status: 404 }))
    } else {
      await route.fulfill({ status: 404 })
    }
  })
  await page.goto(options.sandboxed ? `${origin}/sandboxed.html` : `${harnessUrl}${query}`)
  return recording
}

for (const [name, widget] of Object.entries(previews)) {
  for (const mode of modes) {
    test(`the ${name} preview mounts its widget in a 992px frame sandboxed without allow-forms in ${mode} mode with no error`, async ({
      page,
    }) => {
      const query = `?preview=${name}&mode=${mode}`
      const recording = await openHarness(page, query, { sandboxed: true, frameWidth: 992 })
      const frame = page.frameLocator('iframe')

      await expect(widget(frame)).toBeVisible()
      if (name === 'states') {
        // The loading state's request never finishes, so its table stays busy.
        await expect(
          figure(frame, 'Loading').locator('[aria-busy="true"]').first(),
        ).toBeVisible()
      }
      const emptyText = emptyTextOf(name)
      if (emptyText !== undefined) {
        await expect(
          figure(frame, 'Empty')
            .getByText(emptyText, { exact: true })
            .filter({ visible: true })
            .first(),
        ).toBeVisible()
      }
      const withData = figure(frame, 'With data')
      if (name === 'table') {
        await expect(withData.getByRole('link').first()).toBeVisible()
        await expect(withData.getByRole('button', { name: 'New request', exact: true })).toBeVisible()
        // The table folds the row actions into the "…" menu, so the delete action is not a button of the row.
        const many = figure(frame, 'Many row actions')
        await expect(many.getByRole('button', { name: 'Actions', exact: true }).first()).toBeVisible()
        await expect(many.getByRole('button', { name: 'Delete', exact: true })).toHaveCount(0)
      }
      if (name === 'taskInbox') {
        await expect(withData.getByText('Overdue', { exact: true })).toBeVisible()
        await expect(withData.getByText('Claimed by you', { exact: true })).toBeVisible()
      }
      if (name === 'processInspector') {
        await expect(frame.getByRole('cell', { name: 'Failed', exact: true }).first()).toBeVisible()
        await expect(frame.getByRole('button', { name: 'Redeliver', exact: true })).toBeVisible()
        await expect(frame.getByRole('button', { name: 'Discard', exact: true })).toBeVisible()
      }
      await page.waitForLoadState('networkidle')

      if (name.startsWith('field-')) {
        await expect(frame.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0)
        await expect(frame.getByRole('button', { name: 'Submit for approval', exact: true })).toHaveCount(0)
        // The history card's title is not an ARIA heading, so the test id and the text are checked.
        await expect(frame.getByTestId('record-history')).toHaveCount(0)
        await expect(frame.getByText('History', { exact: true })).toHaveCount(0)
      }

      // A card never scrolls sideways at 992px.
      const preview = page.frame({ url: (url) => url.pathname === '/design-system.html' })!
      expect(
        await preview.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)

      expect(recording.errors).toEqual([])
      // The fixtures answer every API call, so nothing but the page, the harness and the bundle is requested.
      expect(recording.requests.filter((url) => !url.startsWith('data:'))).toEqual([
        `${origin}/sandboxed.html`,
        `${harnessUrl}${query}`,
        bundleUrl,
      ])
    })
  }
}

for (const name of Object.keys(previews).filter((key) => key.startsWith('field-') && key !== 'field-child-collection')) {
  for (const mode of modes) {
    test(`the ${name} preview draws its read-only and required states in ${mode} mode`, async ({ page }) => {
      const recording = await openHarness(page, `?preview=${name}&mode=${mode}`)

      await expect(figure(page, 'Read-only').getByTestId(/^field-value-/)).toBeVisible()
      await expect(figure(page, 'Read-only').getByRole('textbox')).toHaveCount(0)
      const required = figure(page, 'Required, help text and error')
      // The required mark is a hidden element after the label text, so screen readers skip it.
      await expect(required.locator('label').first().locator('span[aria-hidden="true"]').last()).toHaveText('*')
      await expect(required.getByText('A short note that explains the field.', { exact: true })).toBeVisible()
      await expect(required.getByRole('alert')).toHaveText('This value is not valid.')
      expect(recording.errors).toEqual([])
    })
  }
}

for (const mode of modes) {
  test(`the field-child-collection preview draws its states in ${mode} mode`, async ({ page }) => {
    const recording = await openHarness(page, `?preview=field-child-collection&mode=${mode}`)

    const reference = figure(page, 'Reference and computed cells')
    await expectShown(cellValue(reference, 'lines', 0, 'amount'), '1,700.00')
    await expect(
      reference.getByTestId('reference-lines-0-category').getByRole('button', { name: 'Choose Category 1', exact: true }),
    ).toBeVisible()
    await expect(figure(page, 'Cell error').getByTestId('field-error-lines-1-unitPrice')).toHaveText(
      'Unit price cannot be negative.',
    )
    const readOnly = figure(page, 'Read-only')
    await expectShown(cellValue(readOnly, 'lines', 0, 'description'), 'Standing desk')
    await expect(readOnly.getByRole('textbox')).toHaveCount(0)
    await expect(readOnly.getByRole('button', { name: 'Add row', exact: true })).toHaveCount(0)
    const empty = figure(page, 'Empty')
    await expect(empty.getByRole('button', { name: 'Add row', exact: true })).toBeVisible()
    await expect(empty.getByLabel(fieldLabel('Description 1'))).toHaveCount(0)
    expect(recording.errors).toEqual([])
  })
}

for (const mode of modes) {
  test(`the form preview draws its validation, read-only, wizard, columns and many-steps states in ${mode} mode`, async ({ page }) => {
    const recording = await openHarness(page, `?preview=form&mode=${mode}`)

    await expect(figure(page, 'Validation errors').getByTestId('field-error-title')).toHaveText('Enter a title.')
    const locked = figure(page, 'Read-only by a condition')
    await expect(fieldValue(locked, 'amount')).toBeVisible()
    await expect(locked.locator('input#amount')).toHaveCount(0)
    const wizard = figure(page, 'Wizard steps')
    await expect(wizard.getByTestId('form-steps')).toBeVisible()
    await wizard.getByRole('button', { name: 'Next', exact: true }).click()
    await expect(wizard.locator('#quantity')).toBeVisible()
    // Fields of several widths share rows, and some of them show messages.
    const columns = figure(page, 'Fields in columns, with errors')
    await expect(columns.getByTestId('field-error-title')).toHaveText('Enter a title.')
    await expect(columns.getByTestId('field-error-amount')).toHaveText('This value is not valid.')
    const top = async (field: string) => (await columns.locator(`label[for="${field}"]`).boundingBox())!.y
    expect(await top('title')).toBe(await top('status'))
    // A wizard with seven steps shows the number of each step.
    const many = figure(page, 'Many steps')
    const steps = many.getByTestId('form-steps')
    await expect(steps).toBeVisible()
    await expect(steps.locator('li')).toHaveCount(7)
    await expect(steps.getByText('2', { exact: true })).toBeVisible()
    expect(recording.errors).toEqual([])
  })
}

test('the form preview validates and saves inside a frame sandboxed without allow-forms', async ({ page }) => {
  const messages: string[] = []
  page.on('console', (message) => messages.push(message.text()))
  const recording = await openHarness(page, '?preview=form&mode=light', { sandboxed: true })
  const frame = page.frameLocator('iframe')

  const validation = figure(frame, 'Validation errors')
  await expect(validation.getByRole('alert').filter({ hasText: 'Enter a title.' })).toBeVisible()
  await expect(validation.getByRole('alert').filter({ hasText: 'This value is not valid.' })).toBeVisible()

  // The fixtures answer inside the bundle, so no request reaches the network. The test wraps fetch to see the save.
  const preview = page.frame({ url: (url) => url.pathname === '/design-system.html' })!
  await preview.evaluate(() => {
    const saves: string[] = []
    const holder = window as unknown as { saves: string[] }
    holder.saves = saves
    const original = window.fetch
    window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PATCH') {
        saves.push(String(input))
      }
      return original.call(window, input, init)
    }
  })
  await figure(frame, 'Read-only by a condition').getByRole('button', { name: 'Save', exact: true }).click()

  await expect
    .poll(() => preview.evaluate(() => (window as unknown as { saves: string[] }).saves))
    .toEqual([expect.stringContaining('?form=LockedRequestForm')])
  expect(messages.filter((text) => text.includes('Blocked form submission'))).toEqual([])
  expect(recording.errors).toEqual([])
})

for (const [name, keys] of Object.entries(stateKeys)) {
  for (const mode of modes) {
    test(`the ${name} preview draws every state in a 992px frame sandboxed without allow-forms in ${mode} mode`, async ({
      page,
    }) => {
      const recording = await openHarness(page, `?preview=${name}&mode=${mode}`, {
        sandboxed: true,
        frameWidth: 992,
      })
      const frame = page.frameLocator('iframe')

      for (const [caption, key] of Object.entries(keys)) {
        await expect(key(figure(frame, caption))).toBeVisible()
      }
      expect(recording.errors).toEqual([])
    })
  }
}

for (const [name, keys] of Object.entries(stateKeys)) {
  for (const mode of modes) {
    test(`the ${name} preview shows every state's caption above its frame at 992px in ${mode} mode`, async ({
      page,
    }) => {
      const recording = await openHarness(page, `?preview=${name}&mode=${mode}`, {
        sandboxed: true,
        frameWidth: 992,
      })
      const frame = page.frameLocator('iframe')
      const captions = Object.keys(keys)

      for (const [caption, key] of Object.entries(keys)) {
        await expect(key(figure(frame, caption))).toBeVisible()
      }
      const boxOf = async (locator: Locator) => {
        const box = await locator.boundingBox()
        if (!box) {
          throw new Error('A caption, frame or drawn page has no box')
        }
        return box
      }
      const bottom = (box: { y: number; height: number }) => box.y + box.height

      for (const [index, caption] of captions.entries()) {
        const state = figure(frame, caption)
        await expect(state.locator('figcaption')).toBeVisible()
        const captionBox = await boxOf(state.locator('figcaption'))
        const figureBox = await boxOf(state)
        const frameBox = await boxOf(state.locator(':scope > div'))
        const pageBox = await boxOf(state.locator(':scope > div > :first-child'))

        expect(frameBox.y).toBeGreaterThanOrEqual(bottom(captionBox))
        expect(bottom(frameBox)).toBeLessThanOrEqual(bottom(figureBox))
        expect(pageBox.y).toBeGreaterThanOrEqual(frameBox.y)
        expect(bottom(pageBox)).toBeLessThanOrEqual(bottom(frameBox))
        if (index < captions.length - 1) {
          const nextCaptionBox = await boxOf(figure(frame, captions[index + 1]).locator('figcaption'))
          expect(bottom(frameBox)).toBeLessThanOrEqual(nextCaptionBox.y)
        }
      }
      expect(recording.errors).toEqual([])
    })
  }
}

for (const mode of modes) {
  test(`the shell preview's Form page breadcrumb links back to Requests in ${mode} mode`, async ({ page }) => {
    const recording = await openHarness(page, `?preview=shell&mode=${mode}`, { sandboxed: true, frameWidth: 992 })
    const breadcrumb = figure(page.frameLocator('iframe'), 'Form page').getByRole('navigation', {
      name: 'Breadcrumb',
      exact: true,
    })

    await expect(breadcrumb.getByRole('link', { name: 'Requests', exact: true })).toHaveAttribute('href', '/sample/requests')
    expect(recording.errors).toEqual([])
  })
}

for (const mode of modes) {
  test(`the shell preview shows each state's footer inside its figure at 992px in ${mode} mode`, async ({ page }) => {
    const recording = await openHarness(page, `?preview=shell&mode=${mode}`, { sandboxed: true, frameWidth: 992 })
    const frame = page.frameLocator('iframe')

    for (const caption of Object.keys(stateKeys.shell)) {
      // The harness wraps the previews in a main landmark, which takes the footer out of the contentinfo role.
      const footer = figure(frame, caption).locator('footer')
      await expect(footer).toBeVisible()
      const figureBox = await figure(frame, caption).boundingBox()
      const footerBox = await footer.boundingBox()
      if (!figureBox || !footerBox) {
        throw new Error(`The ${caption} figure or its footer has no box`)
      }
      expect(footerBox.y).toBeGreaterThanOrEqual(figureBox.y)
      expect(footerBox.y + footerBox.height).toBeLessThanOrEqual(figureBox.y + figureBox.height)
    }
    expect(recording.errors).toEqual([])
  })
}

test('the field-date-time preview opens its date picker', async ({ page }) => {
  const recording = await openHarness(page, '?preview=field-date-time&mode=light')
  await figure(page, 'Editable').getByLabel(fieldLabel('Submitted at')).click()

  await expect(page.getByRole('button', { name: 'Next month', exact: true })).toBeVisible()
  expect(recording.errors).toEqual([])
})

test('the field-reference preview lists the fixture categories in its lookup', async ({ page }) => {
  const recording = await openHarness(page, '?preview=field-reference&mode=light')
  await figure(page, 'Editable')
    .getByTestId('reference-category')
    .getByRole('button', { name: 'Choose', exact: true })
    .click()

  const dialog = await waitForDialog(page, 'Category')
  await expect(dialog).toContainText('Office furniture')
  expect(recording.errors).toEqual([])
})

test('the field-child-collection preview adds a row', async ({ page }) => {
  const recording = await openHarness(page, '?preview=field-child-collection&mode=light')
  const reference = figure(page, 'Reference and computed cells')
  await expect(reference.getByLabel(fieldLabel('Description 2'))).toBeVisible()
  await reference.getByRole('button', { name: 'Add row', exact: true }).click()

  await expect(reference.getByLabel(fieldLabel('Description 3'))).toBeVisible()
  expect(recording.errors).toEqual([])
})

test('a mounted table redraws in the other mode when the page changes data-theme', async ({ page }) => {
  const recording = await openHarness(page, '?preview=table&mode=light')
  const header = page
    .getByRole('figure', { name: 'With data', exact: true })
    .getByRole('columnheader', { name: 'Title', exact: true })
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

  const save = page.getByRole('button', { name: 'Save', exact: true }).first()
  const background = () => save.evaluate((button) => getComputedStyle(button).backgroundColor)
  await expect.poll(background).toBe('rgb(19, 194, 194)')

  await page.evaluate(() => document.documentElement.style.setProperty('--primary', '#eb2f96'))

  await expect.poll(background).toBe('rgb(235, 47, 150)')
  expect(recording.errors).toEqual([])
})

for (const mode of modes) {
  test(`a form validation message follows the page's --dangerText in ${mode} mode`, async ({ page }) => {
    const recording = await openHarness(page, `?preview=form&mode=${mode}`)
    const message = figure(page, 'Validation errors').getByText('Enter a title.', { exact: true })
    await expect(message).toBeVisible()
    await page.evaluate(() => document.documentElement.style.setProperty('--dangerText', '#13c2c2'))

    await expect.poll(() => message.evaluate((element) => getComputedStyle(element).color)).toBe('rgb(19, 194, 194)')
    expect(recording.errors).toEqual([])
  })
}

// The WCAG contrast ratio of two colours written as rgb() or rgba().
function contrast(foreground: string, background: string): number {
  const luminance = (colour: string) => {
    const [red, green, blue] = (colour.match(/[\d.]+/g) ?? []).slice(0, 3).map((channel) => {
      const value = Number(channel) / 255
      return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
    })
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue
  }
  const [lighter, darker] = [luminance(foreground), luminance(background)].sort((a, b) => b - a)
  return (lighter + 0.05) / (darker + 0.05)
}

test('the dark table draws the Delete icon and the current page at 4.5:1 or more against their surface', async ({
  page,
}) => {
  const recording = await openHarness(page, '?preview=table&mode=dark')
  const withData = figure(page, 'With data')
  const deleteIcon = withData.getByRole('button', { name: 'Delete', exact: true }).first()
  const currentPage = withData.locator('[aria-current="page"]')
  await expect(deleteIcon).toBeVisible()
  await expect(currentPage).toBeVisible()

  // The text colour and the first ancestor with an opaque background, which is the surface the text sits on.
  const colours = (locator: Locator) =>
    locator.evaluate((element) => {
      const alpha = (colour: string) => Number(colour.match(/[\d.]+/g)?.[3] ?? 1)
      let surface: Element | null = element
      while (surface && alpha(getComputedStyle(surface).backgroundColor) < 1) {
        surface = surface.parentElement
      }
      return {
        foreground: getComputedStyle(element).color,
        background: surface ? getComputedStyle(surface).backgroundColor : '',
      }
    })

  for (const text of [deleteIcon, currentPage]) {
    const { foreground, background } = await colours(text)
    expect(contrast(foreground, background)).toBeGreaterThanOrEqual(4.5)
  }
  expect(recording.errors).toEqual([])
})

test('the bundle sets window.Axis with one mount for each preview', async ({ page }) => {
  const recording = await openHarness(page, '')

  const mounts = await page.evaluate(() => Object.keys((window as unknown as { Axis: object }).Axis))
  expect(mounts.sort()).toEqual(Object.keys(previews).sort())
  // The bundle's first line names the card of each window.Axis preview, in the order of cardOrder.ts.
  const firstLine = readFileSync(bundleFile, 'utf8').split('\n', 1)[0]
  const header = JSON.parse(/^\/\* @ds-bundle: (\{.*\}) \*\/$/.exec(firstLine)![1]) as {
    format: number
    namespace: string
    components: { name: string }[]
  }
  const cards = cardGroups.flatMap((group) => group.previews).map(cardName)
  expect(header).toEqual({ format: 4, namespace: 'Axis', components: cards.map((name) => ({ name })) })
  expect(mounts.map(cardName).sort()).toEqual(header.components.map(({ name }) => name).sort())
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
