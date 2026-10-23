import { expect, type Locator } from '@playwright/test'

/**
 * Opens a select and returns its list of options. The list is found through the combobox's
 * `aria-controls`, so no class of the UI library is needed. A select that has just taken a pick still reports itself
 * open for a moment and then closes, and a closed select has no `aria-controls`. So the opening starts again whenever
 * the select closed under it.
 */
export async function openSelect(combobox: Locator): Promise<Locator> {
  let list!: Locator
  await expect(async () => {
    // The chosen value is drawn over the combobox's input and takes a click, so the keyboard opens the select.
    // Enter on an open select would pick its active option, so an open one is left as it is.
    if ((await combobox.getAttribute('aria-expanded', { timeout: 2_000 })) !== 'true') {
      await combobox.focus({ timeout: 2_000 })
      await combobox.press('Enter', { timeout: 2_000 })
    }
    const id = await combobox.getAttribute('aria-controls', { timeout: 2_000 })
    expect(id, 'an open select names its list').not.toBeNull()
    list = combobox.page().locator(`[id="${id}"]`)
    await expect(list).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 10_000 })
  return list
}

async function selectName(combobox: Locator) {
  return (await combobox.getAttribute('aria-label')) ?? String(combobox)
}

/**
 * Makes the option `item` of the open list the active one by pressing ArrowDown, which wraps around at the end of the
 * list. The active option is the one `aria-activedescendant` of the combobox names. The option's id is read again on
 * each try, because a filter renumbers the options.
 */
async function activateOption(combobox: Locator, item: Locator) {
  await expect(async () => {
    const id = await item.getAttribute('id', { timeout: 1_000 })
    for (let presses = 0; ; presses++) {
      const active = await combobox.getAttribute('aria-activedescendant', { timeout: 1_000 })
      if (active === id) {
        return
      }
      expect(presses, 'ArrowDown reaches the option within one round of the list').toBeLessThan(30)
      await combobox.press('ArrowDown', { timeout: 1_000 })
      // The press is done when the active option has changed, so the next look is not at the old one.
      await expect(combobox).not.toHaveAttribute('aria-activedescendant', active ?? '', { timeout: 1_000 })
    }
  }).toPass({ timeout: 2_000 })
}

/**
 * Chooses the option `option` in the open list `list` from the keyboard: ArrowDown to the option, then Enter. An
 * editable select gets `filter` typed first. A click on an option can land without choosing it, because the list can be
 * placed again between the mouse going down and up, as it is when the filter narrows it or the browser scrolls it. The
 * keyboard does not depend on where the list is, so no select takes a click.
 */
async function chooseOption(combobox: Locator, list: Locator, option: string, filter: string) {
  if (await combobox.isEditable()) {
    await combobox.fill(filter)
  }
  await activateOption(combobox, list.getByRole('option', { name: option, exact: true }))
  await combobox.press('Enter', { timeout: 2_000 })
}

/**
 * Picks the option `option` of a select that keeps the chosen value. An editable select gets `filter`, which defaults
 * to the option, typed first so the option shows at the top of the list. The pick is made from the keyboard. A pick
 * that did not land is made again, since choosing the same option twice is harmless. A page that moves or renders
 * again while the list is open can take a click elsewhere, so the check is part of each try. The pick fails after 20
 * seconds, naming the select and the option. A select that acts and then clears itself needs `pickAction`.
 */
export async function pickOption(combobox: Locator, option: string, filter = option) {
  const name = await selectName(combobox)
  try {
    await expect(async () => {
      const list = await openSelect(combobox)
      await chooseOption(combobox, list, option, filter)
      await expectChosen(combobox, option, { timeout: 2_000 })
    }).toPass({ timeout: 20_000 })
  } catch (cause) {
    throw new Error(`Picking '${option}' in the select ${name} left it unchosen.`, { cause })
  }
}

/**
 * Picks the option `option` of a select that acts and then clears itself, such as Add step or Move to, and expects
 * `result`, the widget, step or row the pick adds, to show. An editable select gets `filter`, which defaults to the
 * option, typed first. The select keeps no value, so it never picks again: a second pick would add a second item.
 */
export async function pickAction(combobox: Locator, option: string, result: Locator, filter = option) {
  const name = await selectName(combobox)
  await expect(async () => {
    const list = await openSelect(combobox)
    await chooseOption(combobox, list, option, filter)
  }).toPass({ timeout: 20_000 })
  await expect(result, `Picking ${option} in ${name} shows what it adds`).toBeVisible({ timeout: 5_000 })
}

/**
 * Picks every option in `options` of a select that allows several, and closes it. It leaves a chosen option as it is.
 * An editable select gets each option typed first, and each pick is made from the keyboard: Enter switches an option
 * on or off, so it is pressed only for an option that is not chosen. The picks are checked at once, and a pick that did
 * not land fails naming the select and the option. The check never picks again.
 */
export async function pickOptions(combobox: Locator, options: string[]) {
  const name = await selectName(combobox)
  await expect(async () => {
    const list = await openSelect(combobox)
    for (const option of options) {
      const item = list.getByRole('option', { name: option, exact: true })
      if ((await item.getAttribute('aria-selected', { timeout: 2_000 })) !== 'true') {
        await chooseOption(combobox, list, option, option)
      }
    }
  }).toPass({ timeout: 20_000 })
  const list = await openSelect(combobox)
  try {
    if (await combobox.isEditable()) {
      await combobox.fill('')
    }
    for (const option of options) {
      await expect(
        list.getByRole('option', { name: option, exact: true }),
        `Picking '${option}' in the select ${name} left it unchosen.`,
      ).toHaveAttribute('aria-selected', 'true', { timeout: 5_000 })
    }
  } finally {
    await combobox.press('Escape')
  }
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
