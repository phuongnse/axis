import { apiFetch } from './api'

/** The agent host's public key after the browser's check, with its key id, see docs/reference/keys-and-data-protection.md#the-agent-hosts-public-key. */
export interface AgentHostKey {
  keyId: string
  publicKey: CryptoKey
}

const coordinate = /^[A-Za-z0-9_-]{43}$/

function toBase64Url(bytes: Uint8Array): string {
  let binary = ''
  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function fromBase64Url(text: string): Uint8Array {
  const binary = atob(text.replace(/-/g, '+').replace(/_/g, '/'))
  return Uint8Array.from(binary, (char) => char.charCodeAt(0))
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isCoordinate(value: unknown): value is string {
  return typeof value === 'string' && coordinate.test(value) && fromBase64Url(value).length === 32
}

/**
 * Checks the answer of `GET /api/agent-host/key` and imports the key, or throws when the answer
 * cannot be trusted. It refuses a suite other than kem 16, kdf 1 and aead 2, a key that is not an
 * EC P-256 JWK, a point that Web Crypto will not import, and a key id that is not the key's RFC 7638
 * thumbprint, see
 * docs/reference/keys-and-data-protection.md#the-agent-hosts-public-key.
 */
export async function checkAgentHostKey(answer: unknown): Promise<AgentHostKey> {
  if (!isObject(answer) || answer.kem !== 16 || answer.kdf !== 1 || answer.aead !== 2) {
    throw new Error('The agent host key uses a suite other than kem 16, kdf 1 and aead 2.')
  }
  const jwk = answer.publicKey
  if (!isObject(jwk) || jwk.kty !== 'EC') {
    throw new Error('The agent host key is not an EC key: its key type is not EC.')
  }
  if (jwk.crv !== 'P-256') {
    throw new Error('The agent host key is not on the P-256 curve.')
  }
  const { x, y } = jwk
  if (!isCoordinate(x) || !isCoordinate(y)) {
    throw new Error('The agent host key has an x or y that is not 32 base64url bytes.')
  }
  const subtle = globalThis.crypto?.subtle
  if (!subtle) {
    throw new Error('Web Crypto is not available, so the agent host key cannot be checked.')
  }
  let publicKey: CryptoKey
  try {
    publicKey = await subtle.importKey(
      'jwk',
      { kty: 'EC', crv: 'P-256', x, y, ext: true },
      { name: 'ECDH', namedCurve: 'P-256' },
      true,
      [],
    )
  } catch {
    throw new Error('The agent host key point is not a P-256 public key.')
  }
  const thumbprint = `{"crv":"P-256","kty":"EC","x":"${x}","y":"${y}"}`
  const digest = await subtle.digest('SHA-256', new TextEncoder().encode(thumbprint))
  const keyId = toBase64Url(new Uint8Array(digest))
  if (answer.keyId !== keyId) {
    throw new Error('The agent host key id is not the thumbprint of the key.')
  }
  return { keyId, publicKey }
}

/**
 * Loads the agent host's public key and checks it. `null` when no key is published (`404`). Any
 * other failed status, and any key that fails the check, throws, see
 * docs/reference/keys-and-data-protection.md#the-agent-hosts-public-key.
 */
export async function loadAgentHostKey(signal?: AbortSignal): Promise<AgentHostKey | null> {
  const response = await apiFetch('/api/agent-host/key', { signal })
  if (response.status === 404) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Reading the agent host key failed with status ${response.status}.`)
  }
  return checkAgentHostKey(await response.json())
}
