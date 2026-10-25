import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { expectChosen, pickOption, pickOptions } from '../select'
import { useSession } from '../sessions'

// The ids of a scenario and of a page in the purchase request sample, which Studio on the main E2E server edits as a copy.
const scenarioId = 'bd059896-4e74-43d6-bd45-f6e8abd08d41'
const pageId = '9b707fe9-a837-433e-8062-48a2a9b4d62a'

/**
 * Moves an option away from the pointer when a mouse button goes down on it. The button then goes up elsewhere,
 * so no click reaches the option. A list that is placed again between the two, as the list of a select is when
 * its filter narrows it or when it flips above its select, does the same.
 */
async function moveOptionOnMouseDown(page: Page) {
  await page.evaluate(() => {
    document.addEventListener(
      'pointerdown',
      (event) => {
        const option = (event.target as Element).closest('[role="option"]')
        if (option instanceof HTMLElement) {
          // Each click moves it further, so a click that is made again does not land either.
          const moves = Number(option.dataset.moves ?? 0) + 1
          option.dataset.moves = String(moves)
          option.style.transform = `translateY(${60 * moves}px)`
        }
      },
      { capture: true },
    )
  })
}

// Neither journey saves, so the copy of the sample that the other specs use stays as it is.
test.describe('the select helpers pick from the keyboard', () => {
  test('a role is chosen in the Roles select of a user row although a click on its option would miss', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`/studio?app=PurchaseRequests&resource=${scenarioId}`)
    const row = page.getByTestId('studio-field-/users/binh')
    const roles = row.getByRole('combobox', { name: 'Roles', exact: true })
    await expect(roles).toBeVisible()
    await moveOptionOnMouseDown(page)

    await pickOptions(roles, ['employee'])

    await expect(row).toContainText('employee')
    await expect(row).toContainText('department-manager')
  })

  test('a type is chosen in the Type select of a new parameter although a click on its option would miss', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`/studio?app=PurchaseRequests&resource=${pageId}`)
    await page.getByRole('button', { name: 'Add parameter', exact: true }).click()
    const type = page.getByTestId('studio-field-/parameters/0/type').getByRole('combobox')
    await expect(type).toBeVisible()
    await moveOptionOnMouseDown(page)

    await pickOption(type, 'reference')

    await expectChosen(type, 'reference')
  })
})
