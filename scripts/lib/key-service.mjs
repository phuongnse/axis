// The key service of the scripts that start a production image. A host outside Development and Testing does not start without one, so
// these scripts make a key file and wrap the installation key with `platform key wrap` in the admin image, as an operator does, see
// docs/reference/installation-on-premises.md#on-premises and docs/reference/keys-and-data-protection.md#key-policy.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { chmodSync, writeFileSync } from 'node:fs'

// Where the hosts and the wrap command find the key file in a container. It is outside /run/secrets, which hosts read as settings.
export const keyServicePath = '/run/key-service/key'

// writeKeyFile: writes a new key file, 32 random bytes in base64, readable by the non-root user in the image.
export const writeKeyFile = (path) => {
  writeFileSync(path, randomBytes(32).toString('base64'))
  chmodSync(path, 0o644)
}

// wrapKey: runs `platform key wrap --purpose <purpose>` in the admin image `image` with the key file `keyFile`, reading `key` on standard
// input, and returns the wrapped key.
export const wrapKey = ({ image, keyFile, purpose = 'installation-key', key }) => {
  const result = spawnSync(
    'docker',
    [
      'run', '--rm', '-i', '--read-only',
      '-e', 'KeyService__Kind=file',
      '-e', `KeyService__KeyFile=${keyServicePath}`,
      '-v', `${keyFile}:${keyServicePath}:ro`,
      image,
      'platform', 'key', 'wrap', '--purpose', purpose,
    ],
    { encoding: 'utf8', input: key },
  )
  assert.equal(result.status, 0, `platform key wrap failed\n${result.stderr}`)
  return result.stdout.trim()
}
