import { afterEach, describe, expect, it, vi } from 'vitest'
import { axisSourceUrl, fetchLicences, licenceUrl, sourceUrl } from './licences'

describe('sourceUrl', () => {
  it.each([
    ['0.1.0+749b198', `${axisSourceUrl}/tree/749b198`],
    ['0.1.0+749B198ABC', `${axisSourceUrl}/tree/749B198ABC`],
    ['0.1.0+' + 'a'.repeat(40), `${axisSourceUrl}/tree/${'a'.repeat(40)}`],
    ['0.1.0+abc123', axisSourceUrl],
    ['0.1.0+' + 'a'.repeat(41), axisSourceUrl],
    ['0.1.0+build.7', axisSourceUrl],
    ['0.1.0', axisSourceUrl],
    ['abcdef1234', axisSourceUrl],
    ['', axisSourceUrl],
    [null, axisSourceUrl],
  ])('gives the source for the version %j', (version, expected) => {
    expect(sourceUrl(version)).toBe(expected)
  })
})

describe('licenceUrl', () => {
  it.each([
    ['MIT', 'https://spdx.org/licenses/MIT.html'],
    ['Apache-2.0', 'https://spdx.org/licenses/Apache-2.0.html'],
    ['GPL-2.0+', 'https://spdx.org/licenses/GPL-2.0+.html'],
    ['https://example.com/licence', 'https://example.com/licence'],
    ['MIT OR Apache-2.0', null],
    ['(MIT AND Zlib)', null],
    ['LICENSE.txt', null],
    ['license.md', null],
    ['LICENSE', null],
    ['UNKNOWN', null],
    ['http://example.com/licence', null],
    ['', null],
  ])('gives the page of the licence %j', (licence, expected) => {
    expect(licenceUrl(licence)).toBe(expected)
  })
})

describe('fetchLicences', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists a package once in the public list, however many versions it ships at', async () => {
    const entry = { name: '@babel/runtime', licence: 'MIT' }
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input === '/licences.json'
          ? new Response(JSON.stringify({ npm: [entry, entry], nuget: [] }), { status: 200 })
          : new Response(null, { status: 401 }),
      ),
    )

    expect(await fetchLicences()).toEqual({ npm: [entry], nuget: [] })
  })
})
