import { webcrypto } from 'node:crypto'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { checkAgentHostKey, loadAgentHostKey } from './agentHostKey'

interface Jwk {
  kty: string
  crv: string
  x: string
  y: string
}

beforeEach(() => {
  vi.stubGlobal('crypto', webcrypto)
})

function base64Url(bytes: Uint8Array): string {
  return Buffer.from(bytes).toString('base64url')
}

async function thumbprint(jwk: Jwk): Promise<string> {
  const text = `{"crv":"${jwk.crv}","kty":"${jwk.kty}","x":"${jwk.x}","y":"${jwk.y}"}`
  return base64Url(new Uint8Array(await webcrypto.subtle.digest('SHA-256', Buffer.from(text))))
}

async function generate(namedCurve = 'P-256'): Promise<Jwk> {
  const pair = await webcrypto.subtle.generateKey({ name: 'ECDH', namedCurve }, true, ['deriveBits'])
  const jwk = await webcrypto.subtle.exportKey('jwk', pair.publicKey)
  return { kty: jwk.kty!, crv: jwk.crv!, x: jwk.x!, y: jwk.y! }
}

async function answerOf(jwk: Jwk, suite: { kem?: number; kdf?: number; aead?: number } = {}) {
  return { keyId: await thumbprint(jwk), kem: 16, kdf: 1, aead: 2, ...suite, publicKey: jwk }
}

describe('checkAgentHostKey', () => {
  it('accepts a P-256 key made with Web Crypto', async () => {
    const jwk = await generate()

    const key = await checkAgentHostKey(await answerOf(jwk))

    expect(key.keyId).toBe(await thumbprint(jwk))
    expect(key.publicKey.algorithm).toMatchObject({ name: 'ECDH', namedCurve: 'P-256' })
  })

  it.each([{ kem: 32 }, { kdf: 2 }, { aead: 1 }])('refuses the wrong suite %j', async (suite) => {
    const answer = await answerOf(await generate(), suite)

    await expect(checkAgentHostKey(answer)).rejects.toThrow(/kem 16, kdf 1 and aead 2/)
  })

  it('refuses a P-384 key', async () => {
    const answer = await answerOf(await generate('P-384'))

    await expect(checkAgentHostKey(answer)).rejects.toThrow(/P-256 curve/)
  })

  it('refuses a key that is not EC', async () => {
    const answer = await answerOf({ ...(await generate()), kty: 'RSA' })

    await expect(checkAgentHostKey(answer)).rejects.toThrow(/key type is not EC/)
  })

  it('refuses a coordinate that is not 32 bytes', async () => {
    const answer = await answerOf({ ...(await generate()), x: 'AAAA' })

    await expect(checkAgentHostKey(answer)).rejects.toThrow(/32 base64url bytes/)
  })

  it('refuses a point off the curve whose key id matches it', async () => {
    const jwk = await generate()
    const y = Buffer.from(jwk.y, 'base64url')
    y[y.length - 1] ^= 1
    const answer = await answerOf({ ...jwk, y: base64Url(y) })

    await expect(checkAgentHostKey(answer)).rejects.toThrow(/not a P-256 public key/)
  })

  it('refuses a valid key whose key id is another key thumbprint', async () => {
    const answer = { ...(await answerOf(await generate())), keyId: await thumbprint(await generate()) }

    await expect(checkAgentHostKey(answer)).rejects.toThrow(/key id/)
  })
})

describe('loadAgentHostKey', () => {
  function stubFetch(response: Response) {
    const fetchMock = vi.fn(async () => response)
    vi.stubGlobal('fetch', fetchMock)
    return fetchMock
  }

  function json(body: unknown) {
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }

  it('gives null when no key is published', async () => {
    const fetchMock = stubFetch(new Response(null, { status: 404 }))

    await expect(loadAgentHostKey()).resolves.toBeNull()
    expect(fetchMock).toHaveBeenCalledWith('/api/agent-host/key', { signal: undefined })
  })

  it('rejects when the answer fails the check', async () => {
    stubFetch(json(await answerOf(await generate(), { kem: 32 })))

    await expect(loadAgentHostKey()).rejects.toThrow(/kem 16, kdf 1 and aead 2/)
  })

  it('rejects on another failed status', async () => {
    stubFetch(new Response(null, { status: 500 }))

    await expect(loadAgentHostKey()).rejects.toThrow(/status 500/)
  })

  it('gives the checked key', async () => {
    const jwk = await generate()
    stubFetch(json(await answerOf(jwk)))

    const key = await loadAgentHostKey()

    expect(key?.keyId).toBe(await thumbprint(jwk))
  })
})
