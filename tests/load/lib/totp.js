// The 6-digit authenticator code (RFC 6238: HMAC-SHA1, 30 seconds) for the k6 scenario. It is plain JavaScript with no
// k6 import, so that tests/load/totp.test.mjs runs exactly the code that k6 runs and compares it with tests/e2e/totp.ts.
const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
const period = 30

function decodeBase32(value) {
  const bytes = []
  let buffer = 0
  let bits = 0
  for (const character of value.replace(/=+$/, '')) {
    const index = alphabet.indexOf(character)
    if (index < 0) throw new Error(`"${character}" is not a Base32 character.`)
    buffer = (buffer << 5) | index
    bits += 5
    if (bits >= 8) {
      bits -= 8
      bytes.push((buffer >> bits) & 0xff)
      buffer &= (1 << bits) - 1
    }
  }
  return Uint8Array.from(bytes)
}

const rotateLeft = (value, count) => (value << count) | (value >>> (32 - count))

// SHA-1 (RFC 3174) of the bytes: 20 bytes.
function sha1(message) {
  const length = message.length
  const padded = new Uint8Array((((length + 8) >> 6) + 1) << 6)
  padded.set(message)
  padded[length] = 0x80
  const view = new DataView(padded.buffer)
  view.setUint32(padded.length - 8, Math.floor((length * 8) / 0x100000000))
  view.setUint32(padded.length - 4, (length * 8) >>> 0)

  let h0 = 0x67452301
  let h1 = 0xefcdab89
  let h2 = 0x98badcfe
  let h3 = 0x10325476
  let h4 = 0xc3d2e1f0
  const words = new Array(80)
  for (let block = 0; block < padded.length; block += 64) {
    for (let i = 0; i < 16; i++) words[i] = view.getUint32(block + i * 4)
    for (let i = 16; i < 80; i++) words[i] = rotateLeft(words[i - 3] ^ words[i - 8] ^ words[i - 14] ^ words[i - 16], 1)
    let a = h0
    let b = h1
    let c = h2
    let d = h3
    let e = h4
    for (let i = 0; i < 80; i++) {
      let f
      let k
      if (i < 20) {
        f = (b & c) | (~b & d)
        k = 0x5a827999
      } else if (i < 40) {
        f = b ^ c ^ d
        k = 0x6ed9eba1
      } else if (i < 60) {
        f = (b & c) | (b & d) | (c & d)
        k = 0x8f1bbcdc
      } else {
        f = b ^ c ^ d
        k = 0xca62c1d6
      }
      const next = (rotateLeft(a, 5) + f + e + k + words[i]) | 0
      e = d
      d = c
      c = rotateLeft(b, 30)
      b = a
      a = next
    }
    h0 = (h0 + a) | 0
    h1 = (h1 + b) | 0
    h2 = (h2 + c) | 0
    h3 = (h3 + d) | 0
    h4 = (h4 + e) | 0
  }
  const digest = new Uint8Array(20)
  const out = new DataView(digest.buffer)
  ;[h0, h1, h2, h3, h4].forEach((word, i) => out.setUint32(i * 4, word >>> 0))
  return digest
}

// HMAC-SHA1 (RFC 2104) with the 64-byte block of SHA-1.
function hmacSha1(key, message) {
  const block = new Uint8Array(64)
  block.set(key.length > 64 ? sha1(key) : key)
  const inner = new Uint8Array(64 + message.length)
  const outer = new Uint8Array(64 + 20)
  for (let i = 0; i < 64; i++) {
    inner[i] = block[i] ^ 0x36
    outer[i] = block[i] ^ 0x5c
  }
  inner.set(message, 64)
  outer.set(sha1(inner), 64)
  return sha1(outer)
}

// The code for `secret`, a Base32 string, at `at`, in milliseconds since the epoch.
export function totp(secret, at = Date.now()) {
  const counter = Math.floor(at / 1000 / period)
  const message = new Uint8Array(8)
  const view = new DataView(message.buffer)
  view.setUint32(0, Math.floor(counter / 0x100000000))
  view.setUint32(4, counter >>> 0)
  const hash = hmacSha1(decodeBase32(secret), message)
  const offset = hash[hash.length - 1] & 0x0f
  const binary = (((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3]) >>> 0
  return String(binary % 1_000_000).padStart(6, '0')
}
