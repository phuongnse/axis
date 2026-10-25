import { expect, test } from '../test'
import { createRequest, recordsPath } from '../purchase-requests'
import { previewURL } from '../server-settings'
import { expectSessionUser, sessionFile } from '../sessions'

// The record API needs a session with a grant, so the journey starts signed in as the employee anna.
test.use({ storageState: sessionFile('anna') })

// The preview tenant default-preview shares the server with the main tenant, but not its host, session or
// database. The request is created on default, and the preview, which anna signs in to on its own host,
// must not hold it. The title is unique, so the check does not depend on how many records either tenant has.
test('anna signs in on the preview host and sees the purchase request site without the records of default', async ({
  baseURL,
  browser,
  page,
}) => {
  const { title } = await createRequest(page, 'Preview isolation', 10)

  // The context would inherit anna's saved session from test.use, including her Keycloak cookies, so it starts empty.
  const context = await browser.newContext({
    baseURL: previewURL(baseURL!),
    storageState: { cookies: [], origins: [] },
  })
  try {
    const preview = await context.newPage()
    await preview.goto('/api/auth/sign-in?returnUrl=/')
    await preview.locator('#username').fill('anna')
    await preview.locator('#password').fill('axis')
    await preview.locator('#kc-login').click()
    await preview.waitForURL(`${previewURL(baseURL!)}/`)
    await expectSessionUser(preview, 'anna')

    await preview.goto('/purchasing/purchaserequests?pageSize=100')
    await expect(preview.getByRole('button', { name: 'New', exact: true })).toBeVisible()
    await expect(preview.getByRole('row', { name: new RegExp(title) })).toHaveCount(0)

    const records = await preview.request.get(`${recordsPath}?pageSize=100`)
    expect(records.status()).toBe(200)
    const body = (await records.json()) as { items: { values: { title: string } }[] }
    expect(body.items.map((item) => item.values.title)).not.toContain(title)
  } finally {
    await context.close()
  }
})
