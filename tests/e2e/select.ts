import { expect, type Locator } from '@playwright/test'

/**
 * Opens a select and returns its list of options. The list is found through the combobox's
 * `aria-controls`, so no class of the UI library is needed.
 */
export async function openSelect(combobox: Locator): Promise<Locator> {
  // The chosen value is drawn over the combobox's input and takes a click, so the keyboard opens the select.
  // Enter on an open select would pick its active option, so an open one is left as it is.
  if ((await combobox.getAttribute('aria-expanded')) !== 'true') {
    await combobox.focus()
    await combobox.press('Enter')
  }
  const list = combobox.page().locator(`[id="${await combobox.getAttribute('aria-controls')}"]`)
  await expect(list).toBeVisible()
  return list
}

/**
 * Picks the option `option` of a select. `filter` is typed first to narrow a long list. A page that
 * renders again just after the select opened closes it, so the pick starts again then.
 */
export async function pickOption(combobox: Locator, option: string, filter?: string) {
  await expect(async () => {
    const list = await openSelect(combobox)
    if (filter !== undefined) {
      await combobox.fill(filter)
    }
    await list.getByRole('option', { name: option, exact: true }).click({ timeout: 2_000 })
  }).toPass({ timeout: 20_000 })
}

/** Picks every option in `options` of a select that allows several, and closes it. It leaves a chosen option as it is. */
export async function pickOptions(combobox: Locator, options: string[]) {
  await expect(async () => {
    const list = await openSelect(combobox)
    for (const option of options) {
      const item = list.getByRole('option', { name: option, exact: true })
      if ((await item.getAttribute('aria-selected', { timeout: 2_000 })) !== 'true') {
        await item.click({ timeout: 2_000 })
      }
    }
  }).toPass({ timeout: 20_000 })
  await combobox.press('Escape')
}

/** Expects the option `option` to be the chosen one: it opens the select, looks and closes it again. */
export async function expectChosen(combobox: Locator, option: string, options?: { timeout?: number }) {
  const list = await openSelect(combobox)
  try {
    await expect(list.getByRole('option', { name: option, exact: true })).toHaveAttribute('aria-selected', 'true', options)
  } finally {
    await combobox.press('Escape')
  }
  await expect(list).toBeHidden()
}
