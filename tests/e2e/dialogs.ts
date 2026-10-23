import { expect, type Page } from '@playwright/test'

/** Clicks `button` in the open dialog until the dialog closes. A click that lands while the dialog is still opening is lost and the dialog stays open. */
export async function confirmDialog(page: Page, button: string) {
  const dialog = page.getByRole('dialog')
  await expect(async () => {
    await dialog.getByRole('button', { name: button, exact: true }).click({ timeout: 5_000 })
    await expect(dialog).toBeHidden({ timeout: 5_000 })
  }).toPass({ timeout: 30_000 })
}
