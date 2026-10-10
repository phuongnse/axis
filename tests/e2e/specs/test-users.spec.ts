import { expect, test } from '@playwright/test'

// The request fixture keeps its own cookies, so this sign-in does not reach other specs.
test('a test user signs in and the current user endpoint returns that user', async ({ request }) => {
  const signedIn = await request.post('/api/test-users/sign-in', { data: { id: 'anna' } })
  expect(signedIn.status()).toBe(200)

  const me = await request.get('/api/me')

  expect(me.status()).toBe(200)
  expect(await me.json()).toEqual({ id: 'anna', displayName: 'Anna Employee', roles: ['employee'] })
})
