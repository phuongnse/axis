import { expect, type Locator, type Page } from '@playwright/test'
import { waitForDialog } from './dialogs'
import { pickOption, pickOptions } from './select'

/**
 * Opens the Problems panel from the band's problems button, unless it is open, and returns the panel. The button
 * shows only while the application has a problem, so this waits for one.
 */
export async function openProblems(page: Page): Promise<Locator> {
  const button = page.getByTestId('studio-problems')
  await expect(button).toBeVisible()
  if ((await button.getAttribute('aria-expanded')) !== 'true') {
    await button.click()
  }
  const panel = page.getByTestId('studio-diagnostics')
  await expect(panel).toBeVisible()
  return panel
}

/** Like `openProblems`, but without waiting for a problem: with none, the band has no button and the panel is absent, so test it with `filter` and `toHaveCount`. */
export async function openProblemsIfAny(page: Page): Promise<Locator> {
  if ((await page.getByTestId('studio-problems').count()) > 0) {
    return openProblems(page)
  }
  return page.getByTestId('studio-diagnostics')
}

/** The sidebar entry of a kind. Its address ends in `&kind=<kind>`, so it is found by that and not by its label. */
function kindEntry(page: Page, kind: string) {
  return page.getByRole('navigation', { name: 'Menu', exact: true }).locator(`a[href$="&kind=${kind}"]`)
}

/** Whether the sidebar has an entry for a kind, which it shows only while the application holds a resource of that kind. */
export async function hasKind(page: Page, kind: string) {
  return (await kindEntry(page, kind).count()) > 0
}

/** Opens a kind's list from its entry in the sidebar, and waits until the address shows the kind. */
export async function openKind(page: Page, kind: string) {
  await kindEntry(page, kind).click()
  await expect(page).toHaveURL(new RegExp(`[?&]kind=${kind}(&|$)`))
}

/** The English singular name of each kind, as the New resource dialog lists it. */
const kindNames: Record<string, string> = {
  entity: 'Entity',
  seed: 'Seed',
  sequence: 'Sequence',
  dataSource: 'Data source',
  migration: 'Migration',
  site: 'Site',
  page: 'Page',
  form: 'Form',
  process: 'Process',
  rule: 'Rule',
  event: 'Event',
  trigger: 'Trigger',
  role: 'Role',
  policy: 'Policy',
  identityProvider: 'Identity provider',
  connector: 'Connector',
  endpoint: 'Endpoint',
  webhook: 'Webhook',
  text: 'Text',
  scenario: 'Scenario',
}

/**
 * Creates a resource of the kind `kind` through the New resource dialog. It returns once the dialog is gone, the
 * address holds the new resource, and the kind's entry in the sidebar is marked. `value` is typed into the dialog.
 */
export async function createResource(page: Page, kind: string, value: string) {
  await page.getByRole('button', { name: 'New resource…', exact: true }).click()
  const dialog = await waitForDialog(page, 'New resource')
  await pickOption(dialog.getByRole('combobox', { name: 'Kind', exact: true }), kindNames[kind] ?? kind)
  await dialog.getByRole('textbox', { name: kind === 'text' ? 'Locale' : 'Name', exact: true }).fill(value)
  const create = dialog.getByRole('button', { name: 'Create', exact: true })
  await expect(create).toBeEnabled()
  await create.click()
  await expect(dialog).toBeHidden()
  await expect(page).toHaveURL(/[?&]resource=/)
  await expect(kindEntry(page, kind)).toHaveAttribute('aria-current', 'page')
}

/** Opens the New application… dialog from the application row of the sidebar, on a page that has an application open. */
export async function openNewApplication(page: Page) {
  await page.getByTestId('studio-application-row').click()
  await page.getByRole('menuitem', { name: 'New application…', exact: true }).click()
}

/**
 * Selects a tab of the open resource and waits until it is selected. It clicks again when the click did not select the
 * tab. The layout can shift just before or during the click, as the web font arrives or as the release history above
 * the editor loads, and a click that lands during the shift can miss.
 */
export async function openTab(page: Page, name: 'Design' | 'Form' | 'JSON') {
  const tab = page.getByRole('tab', { name, exact: true })
  await page.evaluate(() => document.fonts.ready)
  await expect(async () => {
    await tab.click()
    await expect(tab).toHaveAttribute('aria-selected', 'true', { timeout: 2000 })
  }).toPass()
}

/**
 * The whole text of the JSON view. The editor draws only the lines near the window, so `textContent` can miss the rest
 * and `inputValue` does not work on it. This reads the document from CodeMirror's own view.
 */
export async function jsonViewText(json: Locator): Promise<string> {
  return json.evaluate((content) => {
    const tile = (content as unknown as { cmTile?: { root?: { view?: { state: { doc: { toString(): string } } } } } })
      .cmTile
    const view = tile?.root?.view
    if (!view) {
      throw new Error('The JSON view has no CodeMirror view.')
    }
    return view.state.doc.toString()
  })
}

/**
 * Selects the Form tab of the open resource, then waits until no expression editor of the resource is loading or
 * checking. Under D65 and D66 a page or a form opens on its Design tab, so a spec that uses the fields of its Form tab
 * selects Form first. It does no harm when Form is selected.
 */
export async function openFormTab(page: Page) {
  await openTab(page, 'Form')
  await waitForExpressions(page)
}

/**
 * Waits until no expression editor of the open resource is loading or waiting for its check. A check's answer adds a
 * line under its editor and moves what is below, so a click during it can miss.
 */
export async function waitForExpressions(page: Page) {
  await expect(page.getByTestId('studio-editor').locator('[aria-busy="true"]')).toHaveCount(0)
}

/** Clicks Add mapping in `scope` once the expression editors are still. */
export async function addMapping(scope: Locator) {
  await waitForExpressions(scope.page())
  await scope.getByRole('button', { name: 'Add mapping', exact: true }).click()
}

export async function writeLabel(scope: Page | Locator, field: string, key: string, en: string) {
  const label = scope.getByTestId(field).getByTestId('studio-label')
  const keyBox = label.getByRole('textbox', { name: 'Text key', exact: true })
  const english = label.getByRole('textbox', { name: 'en', exact: true })
  // The label editor fills the texts when the key is typed, from the names it has then. An editor that was replaced
  // or got its names later starts over, so the key is typed again until the text shows.
  await expect(async () => {
    await keyBox.fill('')
    await keyBox.fill(key)
    await expect(english).toHaveValue(en, { timeout: 2_000 })
  }).toPass({ timeout: 20_000 })
}

export async function grantIn(
  page: Page,
  role: string,
  kind: string,
  target: string,
  grants: { actions: string[]; filter?: string; fields?: string[] }[],
) {
  const cell = page.getByTestId(`studio-matrix-cell-${role}-${kind}-${target}`)
  await cell.click()
  for (const [index, grant] of grants.entries()) {
    await page.getByRole('button', { name: 'Add grant', exact: true }).click()
    const own = page.getByTestId('studio-matrix-own-grant').nth(index)
    for (const action of grant.actions) {
      // The matrix ticks the box a moment after the click, which check() would report as no change.
      const box = own.getByTestId('studio-matrix-actions').getByRole('checkbox', { name: action, exact: true })
      await box.click()
      await expect(box).toBeChecked()
    }
    if (grant.filter !== undefined) {
      await expect(own.locator('.cm-editor')).toBeVisible()
      await own.getByRole('textbox', { name: 'Record filter', exact: true }).fill(grant.filter)
    }
    if (grant.fields !== undefined) {
      await pickOptions(own.getByRole('combobox'), grant.fields)
    }
  }
  await page.getByTestId('studio-matrix-own-grants').getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByTestId('studio-matrix-saved')).toBeVisible()
  // The matrix loads again after a save. A cell clicked before that could lose its new draft.
  for (const action of grants.flatMap((grant) => grant.actions)) {
    await expect(cell).toContainText(action)
  }
}
