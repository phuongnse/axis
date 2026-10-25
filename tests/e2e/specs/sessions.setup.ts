import { expect, test as setup } from '../test'
import { journeySessionFile, journeyUsers, sampleUsers, sessionFile, type SampleUser } from '../sessions'
import { journeyURL } from '../server-settings'

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
    // Keycloak does not keep the order of a user's roles.
    expect([...body.roles].sort()).toEqual([...sampleUsers[user]].sort())

    await page.context().storageState({ path: sessionFile(user) })
  })
}

// The journey host is another server, so its sessions are separate. The author, the requester, the service desk
// lead, the agent and the approver sign in there.
for (const user of journeyUsers) {
  setup(`${user} signs in on the journey host and the session is saved`, async ({ page }) => {
    await page.goto(`${journeyURL}/api/auth/sign-in?returnUrl=/`)
    await page.locator('#username').fill(user)
    await page.locator('#password').fill('axis')
    await page.locator('#kc-login').click()
    await page.waitForURL(`${journeyURL}/`)

    const me = await page.request.get(`${journeyURL}/api/me`)
    expect(me.status(), `GET ${journeyURL}/api/me as ${user}`).toBe(200)
    const body = await me.json()
    expect(body.id).toBe(user)
    expect([...body.roles].sort()).toEqual([...sampleUsers[user]].sort())

    await page.context().storageState({ path: journeySessionFile(user) })
  })
}
