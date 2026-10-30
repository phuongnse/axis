import { expect, test } from '../test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { useSession } from '../sessions'

// The leave requests sample, which no journey of the auditor touches. The dev realm gives the user the id `auditor`.
const leaveRequestsId = '6613186d-8c6f-4e98-b911-6acc1104a5a2'
const auditorId = 'auditor'

test('an administrator opens Users from the app switcher and finds a user by email', async ({ page }) => {
  await useSession(page, 'administrator')
  await page.goto('/')

  await page.getByRole('banner').getByRole('button', { name: 'Switch to', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Users', exact: true }).click()

  await expect(page).toHaveURL(/\/users$/)
  const main = page.getByRole('main')
  await expect(main.getByRole('cell', { name: 'Administrator User', exact: true })).toBeVisible()
  await expect(main.getByRole('cell', { name: 'auditor@axis.local', exact: true })).toBeVisible()

  await main.getByRole('searchbox', { name: 'Search by name or email', exact: true }).fill('auditor@axis.local')
  await page.keyboard.press('Enter')

  await expect(main.getByRole('row').filter({ hasText: 'Auditor User' })).toHaveCount(1)
  await expect(main.getByRole('cell', { name: 'Administrator User', exact: true })).toHaveCount(0)
})

test('a person without the administrator role is told the page is for administrators', async ({ page }) => {
  await useSession(page, 'author')
  await page.goto('/users')

  await expect(page.getByText('This page is for administrators.', { exact: true })).toBeVisible()
})

test.describe('roles of a user', () => {
  // A failed run must not leave the role given.
  test.afterEach(async ({ page }) => {
    await useSession(page, 'administrator')
    await page.request.post(`/api/users/${auditorId}/remove-role`, {
      headers: csrfHeaders,
      data: { applicationId: leaveRequestsId, role: 'employee' },
    })
  })

  test('an administrator opens a user, gives an application role after a confirmation and removes it again', async ({
    page,
  }) => {
    await useSession(page, 'administrator')
    await page.goto('/users')
    const main = page.getByRole('main')
    await main.getByRole('searchbox', { name: 'Search by name or email', exact: true }).fill('auditor@axis.local')
    await page.keyboard.press('Enter')
    await main.getByRole('row').filter({ hasText: 'Auditor User' }).click()

    const panel = page.getByTestId('user-roles')
    await expect(panel.getByText('Auditor User', { exact: true })).toBeVisible()
    const section = panel.getByRole('region', {
      name: 'LeaveRequests',
      exact: true,
    })
    const role = section.getByTestId('user-role').filter({ hasText: 'employee' })
    await expect(role).toBeVisible()
    await expect(role.getByText('Given', { exact: true })).toHaveCount(0)

    await role.getByRole('button', { name: 'Give', exact: true }).click()
    const giveDialog = await waitForDialog(page, 'Give this role?')
    await expect(giveDialog).toContainText('Role: employee')
    await expect(giveDialog).toContainText('In: LeaveRequests')
    await giveDialog.getByRole('button', { name: 'Give', exact: true }).click()
    await expect(role.getByText('Given', { exact: true })).toBeVisible()

    await role.getByRole('button', { name: 'Remove', exact: true }).click()
    const removeDialog = await waitForDialog(page, 'Remove this role?')
    await removeDialog.getByRole('button', { name: 'Remove', exact: true }).click()
    await expect(role.getByText('Given', { exact: true })).toHaveCount(0)
    await expect(role.getByRole('button', { name: 'Give', exact: true })).toBeVisible()
  })
})
