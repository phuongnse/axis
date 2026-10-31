import { createHmac } from "node:crypto";
import { readFileSync } from "node:fs";
import path from "node:path";

const base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
const period = 30;

function decodeBase32(value: string) {
  const bytes: number[] = [];
  let buffer = 0;
  let bits = 0;
  for (const character of value.replace(/=+$/, "")) {
    const index = base32Alphabet.indexOf(character);
    if (index < 0) throw new Error(`"${character}" is not a Base32 character.`);
    buffer = (buffer << 5) | index;
    bits += 5;
    if (bits >= 8) {
      bits -= 8;
      bytes.push((buffer >> bits) & 0xff);
      buffer &= (1 << bits) - 1;
    }
  }
  return Buffer.from(bytes);
}

/**
 * The 6-digit time-based one-time password (RFC 6238, HMAC-SHA1, 30 seconds) for `secret` at `at`, in milliseconds
 * since the epoch. `secret` is a Base32 string, or the raw key bytes.
 */
export function totp(secret: string | Uint8Array, at = Date.now()) {
  const key = typeof secret === "string" ? decodeBase32(secret) : secret;
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / period)));
  const hash = createHmac("sha1", key).update(counter).digest();
  const offset = hash[hash.length - 1] & 0x0f;
  const binary = hash.readUInt32BE(offset) & 0x7fffffff;
  return String(binary % 1_000_000).padStart(6, "0");
}

/**
 * A code for `secret` that differs from `used`, the code that the same person gave before. Keycloak refuses a code that
 * was used in its period, so this waits for the next period when the current code is `used`.
 */
export async function nextTotp(secret: string | Uint8Array, used: string) {
  while (totp(secret) === used) {
    const wait = period * 1000 - (Date.now() % (period * 1000)) + 500;
    await new Promise((resolve) => setTimeout(resolve, wait));
  }
  return totp(secret);
}

/** The Base32 secret of `user`'s `otp` credential in the development realm file, which is the only place that holds it. */
export function totpSecret(user: string) {
  const realm = JSON.parse(
    readFileSync(
      path.resolve(import.meta.dirname, "../../samples/keycloak/axis-realm.json"),
      "utf8",
    ),
  ) as {
    users: {
      username: string;
      credentials?: { type: string; secretData: string }[];
    }[];
  };
  const credential = realm.users
    .find((candidate) => candidate.username === user)
    ?.credentials?.find((candidate) => candidate.type === "otp");
  if (!credential) throw new Error(`The development realm has no authenticator for ${user}.`);
  return (JSON.parse(credential.secretData) as { value: string }).value;
}
