import { expect, type Locator, type Page } from '@playwright/test'
import { confirmDialog, waitForDialog } from './dialogs'
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

/**
 * Deletes the open resource: it opens the "…" menu of the page heading, picks Delete… and confirms the dialog. It returns
 * once the dialog is gone.
 */
export async function deleteResource(page: Page) {
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Delete…', exact: true }).click()
  await confirmDialog(page, 'Delete')
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
export async function openTab(page: Page, name: 'Design' | 'Form' | 'JSON' | 'Used by') {
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
  // An item's dialog is drawn outside the editor, so its expression editors are looked for in the dialog too.
  await expect(
    page.locator('[data-testid="studio-editor"] [aria-busy="true"], [role="dialog"] [aria-busy="true"]'),
  ).toHaveCount(0)
}

/**
 * Clicks Save and waits for the save to finish. A save waits for the Studio write lock, which can take up to 10 seconds,
 * and the server then compiles the application, so this waits up to 30 seconds. A save error or a conflict fails at once
 * with the alert's text.
 */
export async function saveResource(page: Page) {
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const saved = page.getByTestId('studio-saved')
  const error = page.getByTestId('studio-save-error')
  const conflict = page.getByTestId('studio-conflict')
  await expect(saved.or(error).or(conflict)).toBeVisible({ timeout: 30_000 })
  for (const failure of [error, conflict]) {
    if (await failure.isVisible()) {
      throw new Error(`The save failed: ${await failure.textContent()}`)
    }
  }
}

/**
 * Opens the dialog that adds an item to a list of objects, by the list's Add…, such as Add module…, and returns the
 * dialog. `list` is the list's JSON Pointer. A list that is the only one in its section has its Add… in the section's
 * heading, and any other list has it above its table. The fields of the new item sit at the pointer after the last row.
 */
export async function addListItem(page: Page, list: string): Promise<Locator> {
  const field = page.getByTestId(`studio-field-${list}`)
  const add = /^Add .+…$/
  const inList = field.getByRole('button', { name: add })
  const button =
    (await inList.count()) > 0
      ? inList
      : field.locator('xpath=ancestor::section[1]').getByRole('button', { name: add })
  await button.click()
  return waitForDialog(page)
}

/**
 * Opens the dialog of row `index` of a list of objects, which counts from 0, by the link in the row's first cell, and
 * returns the dialog.
 */
export async function openListItem(page: Page, list: string, index: number): Promise<Locator> {
  const row = page.getByTestId(`studio-field-${list}`).getByRole('row').nth(index + 1)
  await row.getByRole('cell').first().getByRole('button').first().click()
  return waitForDialog(page)
}

/** Clicks Done in the open item dialog, which writes the item and closes the dialog. */
export async function doneListItem(page: Page) {
  await confirmDialog(page, 'Done')
}

/** Clicks Add row in the open item dialog, for the nested list at `list`, a JSON Pointer. */
export async function addListRow(page: Page, list: string) {
  await page.getByTestId(`studio-field-${list}`).getByRole('button', { name: 'Add row', exact: true }).click()
}

/** Clicks Add mapping in `scope` once the expression editors are still. */
export async function addMapping(scope: Locator) {
  await waitForExpressions(scope.page())
  await scope.getByRole('button', { name: 'Add mapping', exact: true }).click()
}

/**
 * Types the texts of a label, one per locale, in its Translations… dialog, and keeps the key that Studio makes for it.
 * The label editor makes the key when Done keeps the texts. An editor that was replaced starts again from the draft,
 * so the texts are typed again until the box in the author's language shows its text.
 */
export async function writeLabel(scope: Page | Locator, field: string, texts: Record<string, string>) {
  const page = 'goto' in scope ? scope : scope.page()
  const label = scope.getByTestId(field).getByTestId('studio-label')
  const languages = new Intl.DisplayNames(['en'], { type: 'language' })
  await expect(async () => {
    // A label can sit in an item dialog, such as a form's section title, so only a leftover Translations is closed.
    const leftover = page.getByRole('dialog', { name: 'Translations', exact: true })
    if ((await leftover.count()) > 0) {
      await page.keyboard.press('Escape')
      await expect(leftover).toHaveCount(0, { timeout: 2_000 })
    }
    await label.getByRole('button', { name: 'Translations…', exact: true }).click()
    const dialog = await waitForDialog(page, 'Translations')
    for (const [locale, text] of Object.entries(texts)) {
      const box = dialog.getByRole('textbox', { name: languages.of(locale), exact: true })
      await box.fill(text)
      await expect(box).toHaveValue(text, { timeout: 2_000 })
    }
    await dialog.getByRole('button', { name: 'Done', exact: true }).click()
    await expect(dialog).toHaveCount(0, { timeout: 2_000 })
    if (texts.en !== undefined) {
      await expect(label.getByRole('textbox', { name: 'English', exact: true })).toHaveValue(texts.en, {
        timeout: 2_000,
      })
    }
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
