import { expect } from './test'
import type { Page } from '@playwright/test'
import { waitForDialog } from './dialogs'
import { fieldLabel } from './fields'

/** Opens the lookup of the Department field and picks the row named `department`. */
export async function chooseDepartment(page: Page, department: string) {
  await page.getByTestId('reference-department').getByRole('button', { name: 'Choose…', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('row', { name: new RegExp(department) }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByLabel(fieldLabel('Department'))).toHaveValue(department)
}
