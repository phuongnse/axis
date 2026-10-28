import assert from "node:assert/strict";
import { test } from "node:test";
import { totp } from "./totp.ts";

// RFC 6238, appendix B: the SHA-1 key is the ASCII string 12345678901234567890. The RFC lists 8 digits, and the
// 6 digits are their last six.
const key = Buffer.from("12345678901234567890");

test("computes the RFC 6238 SHA-1 test vectors in 6 digits", () => {
  assert.equal(totp(key, 59_000), "287082");
  assert.equal(totp(key, 1_111_111_109_000), "081804");
  assert.equal(totp(key, 1_111_111_111_000), "050471");
  assert.equal(totp(key, 1_234_567_890_000), "005924");
  assert.equal(totp(key, 2_000_000_000_000), "279037");
});

test("reads a Base32 secret as the same key", () => {
  assert.equal(totp("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", 59_000), "287082");
});

test("gives the same code throughout one 30-second step", () => {
  assert.equal(totp(key, 30_000), totp(key, 59_999));
  assert.notEqual(totp(key, 59_999), totp(key, 60_000));
});
