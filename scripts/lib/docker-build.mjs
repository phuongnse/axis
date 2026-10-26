// Builds one production image and builds it again when `docker build` fails, because a download in the build
// (npm, NuGet, Debian or PostgreSQL packages) can fail at times. Docker reuses the layers that succeeded, so a retry
// reruns only the failed step. Same attempts and waits as scripts/lib/retry.sh.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../..', import.meta.url))

export const buildImage = async ({ target, version, tag, attempts = 3 }) => {
  const args = ['build', '--target', target, '--build-arg', `AXIS_VERSION=${version}`, '-t', tag, '.']
  let status
  for (let attempt = 1; attempt <= attempts; attempt++) {
    status = spawnSync('docker', args, { cwd: root, stdio: 'inherit' }).status
    if (status === 0) return
    if (attempt === attempts) break
    console.error(`Attempt ${attempt} of ${attempts} failed with status ${status}: docker ${args.join(' ')}`)
    const seconds = Number(process.env.AXIS_RETRY_DELAY_SECONDS ?? 15) * 2 ** (attempt - 1)
    await new Promise((resolve) => setTimeout(resolve, seconds * 1000))
  }
  assert.fail(`docker build --target ${target} failed after ${attempts} attempts with status ${status}`)
}
