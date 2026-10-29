import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { loadUsers } from '../../scripts/lib/load-realm.mjs'
import { totp as e2eTotp } from '../e2e/totp.ts'
import { totp } from './lib/totp.js'

// The k6 scenario cannot import node:crypto, so tests/load/lib/totp.js does HMAC-SHA1 itself. These tests keep its
// codes equal to the ones of tests/e2e/totp.ts, which the Playwright journeys use against the same Keycloak.
describe('the k6 TOTP module gives the same codes as tests/e2e/totp.ts', () => {
  const rfcSecret = 'GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ'

  test('it gives the RFC 6238 vectors for the SHA-1 secret, as 6 digits', () => {
    assert.equal(totp(rfcSecret, 59_000), '287082')
    assert.equal(totp(rfcSecret, 1_111_111_109_000), '081804')
    assert.equal(totp(rfcSecret, 1_234_567_890_000), '005924')
    assert.equal(totp(rfcSecret, 2_000_000_000_000), '279037')
  })

  for (const at of [0, 29_999, 30_000, 59_000, 1_111_111_109_000, 1_234_567_890_000, 2_000_000_000_000, 4_102_444_800_000]) {
    test(`it equals the e2e code at ${at}`, () => {
      assert.equal(totp(rfcSecret, at), e2eTotp(rfcSecret, at))
    })
  }

  test('it equals the e2e code for the secrets of the load users', () => {
    const now = Date.now()
    for (const { secret } of loadUsers(1, 'p').slice(0, 5)) {
      assert.equal(totp(secret, now), e2eTotp(secret, now))
      assert.equal(totp(secret, now + 31_000), e2eTotp(secret, now + 31_000))
    }
  })

  test('it uses the current time without an argument', () => {
    assert.match(totp(rfcSecret), /^\d{6}$/)
  })

  test('a character outside Base32 is refused', () => {
    assert.throws(() => totp('not base32!'), /not a Base32 character/)
  })
})
