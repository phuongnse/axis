import { afterEach, describe, expect, it, vi } from 'vitest'
import { newUuid } from './uuid'

const uuidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

afterEach(() => vi.unstubAllGlobals())

describe('newUuid', () => {
  it('gives a new version 4 UUID each time, also where randomUUID is missing', () => {
    expect(newUuid()).toMatch(uuidV4)
    expect(newUuid()).not.toBe(newUuid())

    // An insecure context has no randomUUID.
    const original = globalThis.crypto
    vi.stubGlobal('crypto', { getRandomValues: (bytes: Uint8Array<ArrayBuffer>) => original.getRandomValues(bytes) })
    expect(newUuid()).toMatch(uuidV4)
    expect(newUuid()).not.toBe(newUuid())
  })
})
