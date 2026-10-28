import { expect, test } from '../test'
import { createRequest, recordsPath } from '../purchase-requests'
import { previewURL } from '../server-settings'
import { expectSessionUser, sessionFile } from '../sessions'
import { logInAtKeycloak } from '../keycloak'

// The record API needs a session with a grant, so the journey starts signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

// The preview tenant default-preview shares the server with the main tenant, but not its host, session or
// database. The request is created on default, and the preview, which requester signs in to on its own host,
// must not hold it. The title is unique, so the check does not depend on how many records either tenant has.
test('requester signs in on the preview host and sees the purchase request site without the records of default', async ({
  baseURL,
  browser,
  page,
}) => {
  const { title } = await createRequest(page, 'Preview isolation', 10)

  // The context would inherit the requester's saved session from test.use, including the requester's Keycloak cookies, so it starts empty.
  const context = await browser.newContext({
    baseURL: previewURL(baseURL!),
    storageState: { cookies: [], origins: [] },
  })
  try {
    const preview = await context.newPage()
    await preview.goto('/api/auth/sign-in?returnUrl=/')
    await logInAtKeycloak(preview, 'requester')
    await preview.waitForURL(`${previewURL(baseURL!)}/`)
    await expectSessionUser(preview, 'requester')

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
