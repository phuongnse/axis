import { expect, test } from '@playwright/test'
import { expectSessionUser, useSession } from '../sessions'

test('a switch made while the previous user loads a page signs in the new user', async ({ page }) => {
  await useSession(page, 'chau')
  // The switch interrupts this navigation, so its error is expected.
  page.goto('/purchasing/mytasks?pageSize=100', { waitUntil: 'commit' }).catch(() => null)

  await useSession(page, 'dung')
  await page.goto('/purchasing/mytasks')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Dung Finance Reviewer', exact: true })).toBeVisible()
  await expectSessionUser(page, 'dung')
})

test('a check for a user the server does not sign in names both users', async ({ page }) => {
  await useSession(page, 'anna')
  await expect(expectSessionUser(page, 'chau')).rejects.toThrow(/chau[\s\S]*anna|anna[\s\S]*chau/)
})
