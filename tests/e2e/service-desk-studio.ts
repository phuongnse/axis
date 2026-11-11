import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, type Locator, type Page } from '@playwright/test'
import { waitForDialog } from './dialogs'
import { pickOption } from './select'
import { serviceDesk } from './server-settings'

export async function pick(scope: Page | Locator, testId: string, option: string, filter?: string) {
  await pickOption(scope.getByTestId(testId).getByRole('combobox'), option, filter)
}

export async function save(page: Page, answers: [field: string, choice: string, expression?: string][] = []) {
  // A save with a migration posts one changes call, and a save without one patches the resource.
  const answer = page.waitForResponse(
    (response) => {
      const url = response.url()
      const method = response.request().method()
      return (
        (method === 'PATCH' && url.includes('/api/studio/apps/ServiceDesk/resources/')) ||
        (method === 'POST' && url.endsWith('/api/studio/apps/ServiceDesk/changes'))
      )
    },
    { timeout: 60_000 },
  )
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  for (const [field, choice, expression] of answers) {
    // A click while the dialog zooms in is lost, so wait until it has finished opening.
    await waitForDialog(page)
    await expect(page.getByTestId('studio-migration-field')).toHaveText(field)
    if (expression !== undefined) {
      await page.getByRole('textbox', { name: 'Convert expression', exact: true }).fill(expression)
    }
    await page.getByRole('button', { name: choice, exact: true }).click()
  }
  await expect(page.getByTestId('studio-saved')).toBeVisible()
  const { diagnostics } = (await (await answer).json()) as {
    diagnostics: { file: string; severity: string }[]
  }
  return diagnostics
}

export async function expectForm(page: Page) {
  await expect(page.getByRole('tab', { name: 'Form', exact: true })).toHaveAttribute('aria-selected', 'true')
}

export async function sample<T>(file: string): Promise<T> {
  return JSON.parse(await readFile(path.join(serviceDesk, file), 'utf8')) as T
}
