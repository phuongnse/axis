import { expect, type Locator, type Page } from '@playwright/test'

/**
 * Returns the dialog, named `name` when a name is given, once it has finished opening. A dialog zooms in from opacity 0,
 * and a click during the zoom is lost. So the dialog counts as open only when it is fully opaque and no finite animation
 * runs or is paused in it, on two frames in a row. A repeating animation, such as a spinner, is not waited for. Under
 * reduced motion, which the Playwright config asks for, the SPA has no zoom and this returns at once.
 */
export async function waitForDialog(page: Page, name?: string): Promise<Locator> {
  const dialog = name === undefined ? page.getByRole('dialog') : page.getByRole('dialog', { name, exact: true })
  const label = name === undefined ? 'The dialog' : `The dialog "${name}"`
  await expect(dialog).toBeVisible({ timeout: 15_000 })
  await expect
    .poll(() => dialog.evaluate(hasOpened), { message: `${label} finished opening`, timeout: 15_000 })
    .toBe(true)
  return dialog
}

/**
 * Returns the dialog as soon as it is visible, without waiting for it to finish opening. Only the spec of reduced motion
 * uses it, to see the dialog in its first frame. Every other spec uses {@link waitForDialog}.
 */
export async function visibleDialog(page: Page): Promise<Locator> {
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible({ timeout: 15_000 })
  return dialog
}

async function hasOpened(element: Element): Promise<boolean> {
  for (let frame = 0; frame < 2; frame++) {
    await new Promise((resolve) => requestAnimationFrame(resolve))
    if (getComputedStyle(element).opacity !== '1') return false
    const finiteAnimation = element.getAnimations({ subtree: true }).some((animation) => {
      const pending = animation.playState === 'running' || animation.playState === 'paused'
      return pending && Number.isFinite(animation.effect?.getComputedTiming().iterations)
    })
    if (finiteAnimation) return false
  }
  return true
}

/** Clicks `button` in the open dialog until the dialog closes. A click that lands while the dialog is still opening is lost and the dialog stays open. */
export async function confirmDialog(page: Page, button: string) {
  await expect(async () => {
    const dialog = await waitForDialog(page)
    await dialog.getByRole('button', { name: button, exact: true }).click({ timeout: 5_000 })
    await expect(dialog).toBeHidden({ timeout: 5_000 })
  }).toPass({ timeout: 30_000 })
}
