import { expect, test } from '../test'
import { hostSiteHost } from '../server-settings'
import { expectSessionUser } from '../sessions'
import { logInAtKeycloak } from '../keycloak'

// The site host-site has a host of its own, which is one of the default tenant's hosts. That host serves the site
// at its root and every link of the SPA there has no path prefix. No other spec opens this site.
test('requester opens the site at / on its own host and its links have no site path', async ({ baseURL, browser }) => {
  const siteURL = baseURL!.replace('127.0.0.1', hostSiteHost)
  // A new context, because the saved sessions belong to the tenant's other host.
  const context = await browser.newContext({ baseURL: siteURL, storageState: { cookies: [], origins: [] } })
  try {
    const page = await context.newPage()
    await page.goto('/api/auth/sign-in?returnUrl=/')
    await logInAtKeycloak(page, 'requester')
    await page.waitForURL(`${siteURL}/topics`)
    await expectSessionUser(page, 'requester')

    const site = await page.request.get('/api/site')
    expect(((await site.json()) as { hostSite: string }).hostSite).toBe('host-site')

    const topics = page.getByRole('link', { name: 'Topics', exact: true })
    const inbox = page.getByRole('link', { name: 'Inbox', exact: true })
    await expect(topics).toHaveAttribute('href', '/topics')
    await expect(inbox).toHaveAttribute('href', '/inbox')

    await inbox.click()
    await expect(page).toHaveURL(`${siteURL}/inbox`)
    await expect(page.getByRole('heading', { name: 'My tasks', exact: true })).toBeVisible()

    // The platform pages are not served on a site's host.
    const studio = await page.request.get('/studio')
    expect(studio.status()).toBe(404)

    // The tenant's other host sends the site's old address to the site's host, without the site's path.
    const moved = await page.request.get(`${baseURL}/host-site/inbox`, { maxRedirects: 0 })
    expect(moved.status()).toBe(302)
    expect(moved.headers()['location']).toBe(`${siteURL}/inbox`)
  } finally {
    await context.close()
  }
})
