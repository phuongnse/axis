import { describe, expect, it } from 'vitest'
import { quotaRefusalTitle } from './quota'

const quotaProblem = {
  title: 'This tenant has reached its storage quota.',
  status: 403,
  reason: 'quota-exceeded',
  quota: 'storage',
}

describe('quotaRefusalTitle', () => {
  it('gives the title of a quota refusal and leaves the body readable', async () => {
    const response = new Response(JSON.stringify(quotaProblem), { status: 403 })

    expect(await quotaRefusalTitle(response)).toBe('This tenant has reached its storage quota.')
    expect(await response.json()).toEqual(quotaProblem)
  })

  it.each([
    ['a 403 without a reason', 403, JSON.stringify({ title: 'You may not import records here.', status: 403 })],
    ['a 403 with an empty body', 403, ''],
    ['a 403 whose body is not JSON', 403, '<html>Forbidden</html>'],
    ['a quota refusal with an empty title', 403, JSON.stringify({ ...quotaProblem, title: '' })],
    ['a 409 with the reason', 409, JSON.stringify({ ...quotaProblem, status: 409 })],
  ])('gives null for %s', async (_name, status, body) => {
    expect(await quotaRefusalTitle(new Response(body, { status }))).toBeNull()
  })

  it('gives null for a response without a body clone', async () => {
    expect(await quotaRefusalTitle({ status: 403 } as Response)).toBeNull()
  })
})
