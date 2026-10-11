import { expect, test as setup } from '@playwright/test'
import { sampleUsers, sessionFile, type SampleUser } from '../sessions'

// Each user signs in once on the Keycloak login page. The journeys load the saved sessions.
for (const user of Object.keys(sampleUsers) as SampleUser[]) {
  setup(`${user} signs in on the Keycloak login page and the session is saved`, async ({ page, baseURL }) => {
    await page.goto('/api/auth/sign-in?returnUrl=/')
    await page.locator('#username').fill(user)
    await page.locator('#password').fill('axis')
    await page.locator('#kc-login').click()
    await page.waitForURL(`${baseURL}/`)

    const me = await page.request.get('/api/me')
    expect(me.status(), `GET /api/me as ${user}`).toBe(200)
    const body = await me.json()
    expect(body.id).toBe(user)
    expect(body.roles).toEqual(sampleUsers[user])

    await page.context().storageState({ path: sessionFile(user) })
  })
}
