import { describe, expect, it } from 'vitest'
import { studioFileAddress, studioPackageAddress, studioRequestAddress, studioResourceAddress } from './studioAddress'

describe('studioResourceAddress', () => {
  it('builds the address of a resource', () => {
    expect(studioResourceAddress('PurchaseRequests', 'abc')).toBe('/studio?app=PurchaseRequests&resource=abc')
  })

  it('adds the path of a reference or a problem', () => {
    expect(studioResourceAddress('PurchaseRequests', 'abc', '/fields/0/name')).toBe(
      '/studio?app=PurchaseRequests&resource=abc&path=%2Ffields%2F0%2Fname',
    )
  })

  it('leaves out a missing or empty path', () => {
    expect(studioResourceAddress('PurchaseRequests', 'abc', null)).toBe('/studio?app=PurchaseRequests&resource=abc')
    expect(studioResourceAddress('PurchaseRequests', 'abc', '')).toBe('/studio?app=PurchaseRequests&resource=abc')
  })

  it('adds the field whose drawer is open, with or without a path', () => {
    expect(studioResourceAddress('PurchaseRequests', 'abc', null, 'code')).toBe(
      '/studio?app=PurchaseRequests&resource=abc&field=code',
    )
    expect(studioResourceAddress('PurchaseRequests', 'abc', '/fields/0/name', 'code')).toBe(
      '/studio?app=PurchaseRequests&resource=abc&path=%2Ffields%2F0%2Fname&field=code',
    )
  })

  it('leaves out a missing or empty field', () => {
    expect(studioResourceAddress('PurchaseRequests', 'abc', null, '')).toBe('/studio?app=PurchaseRequests&resource=abc')
  })

  it('encodes a name with a space', () => {
    expect(studioResourceAddress('Leave Requests', 'a&b')).toBe('/studio?app=Leave+Requests&resource=a%26b')
  })
})

describe('studioRequestAddress', () => {
  it('builds the address of an AI request', () => {
    expect(studioRequestAddress('PurchaseRequests', 'r1')).toBe('/studio?app=PurchaseRequests&view=requests&request=r1')
  })
})

describe('Studio addresses', () => {
  const sources = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}', '!../test/**'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  /** A record that sets `resource`, alone or after `app`, as the query of a Studio address does. */
  const buildsResourceAddress = /\{\s*(?:app: [^,}]+,\s*)?resource: /

  it('finds the sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('catches code that builds the address of a resource', () => {
    expect(buildsResourceAddress.test('new URLSearchParams({ app: application, resource: id })')).toBe(true)
    expect(buildsResourceAddress.test('setParams({ resource: id })')).toBe(true)
    expect(buildsResourceAddress.test("new URLSearchParams({ app: application, kind: 'entity' })")).toBe(false)
    expect(buildsResourceAddress.test('const resource: StudioResource | null')).toBe(false)
  })

  it('are built by studioResourceAddress, not by a record with resource in a query', () => {
    const builders = Object.entries(sources)
      .filter(([, text]) => buildsResourceAddress.test(text))
      .map(([file]) => file)
    expect(builders).toEqual([])
  })
})

describe('studioPackageAddress', () => {
  it('builds the address of a package workspace', () => {
    expect(studioPackageAddress('Shop', 'tools')).toBe('/studio?app=Shop&view=packages&package=tools')
  })

  it('adds the file in the package and its line', () => {
    expect(studioPackageAddress('Shop', 'tools', 'lib/x.py')).toBe(
      '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py',
    )
    expect(studioPackageAddress('Shop', 'tools', 'lib/x.py', 3)).toBe(
      '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py&line=3',
    )
  })
})

describe('studioFileAddress', () => {
  it("builds the address of a package's file at its line", () => {
    expect(studioFileAddress('Shop', 'packages/tools/lib/x.py', 3)).toBe(
      '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py&line=3',
    )
  })

  it("builds the address of a package's file without a line", () => {
    expect(studioFileAddress('Shop', 'packages/tools/lib/x.py')).toBe(
      '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py',
    )
  })

  it('returns no address for a file outside packages', () => {
    expect(studioFileAddress('Shop', 'entities/a.json', 3)).toBeUndefined()
    expect(studioFileAddress('Shop', 'packages/tools', 3)).toBeUndefined()
  })
})
