import { expect, test } from '../test'
import { expectSessionUser, useSession } from '../sessions'

test('a switch made while the previous user loads a page signs in the new user', async ({ page }) => {
  await useSession(page, 'manager-2')
  // The switch interrupts this navigation, so its error is expected.
  page.goto('/purchasing/mytasks?pageSize=100', { waitUntil: 'commit' }).catch(() => null)

  await useSession(page, 'finance-reviewer')
  await page.goto('/purchasing/mytasks')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Finance Reviewer User', exact: true })).toBeVisible()
  await expectSessionUser(page, 'finance-reviewer')
})

test('a check for a user the server does not sign in names both users', async ({ page }) => {
  await useSession(page, 'requester')
  await expect(expectSessionUser(page, 'manager-2')).rejects.toThrow(/manager-2[\s\S]*requester|requester[\s\S]*manager-2/)
})
