// Runs a Docker build and runs it again when it fails, because a download in the build (npm, NuGet, Debian or
// PostgreSQL packages) or the registry lookup of a base image can fail at times. Docker reuses the layers that
// succeeded, so a retry reruns only the failed step. Same attempts and waits as scripts/lib/retry.sh.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../..', import.meta.url))

export const runDocker = async (args, { env = process.env, attempts = 3, label = `docker ${args.join(' ')}` } = {}) => {
  let status
  for (let attempt = 1; attempt <= attempts; attempt++) {
    status = spawnSync('docker', args, {
      cwd: root,
      env,
      stdio: 'inherit',
    }).status
    if (status === 0) return
    if (attempt === attempts) break
    console.error(`Attempt ${attempt} of ${attempts} failed with status ${status}: docker ${args.join(' ')}`)
    const seconds = Number(env.AXIS_RETRY_DELAY_SECONDS ?? 15) * 2 ** (attempt - 1)
    await new Promise((resolve) => setTimeout(resolve, seconds * 1000))
  }
  assert.fail(`${label} failed after ${attempts} attempts with status ${status}`)
}

export const buildImage = ({ target, version, tag, attempts = 3 }) =>
  runDocker(['build', '--target', target, '--build-arg', `AXIS_VERSION=${version}`, '-t', tag, '.'], {
    attempts,
    label: `docker build --target ${target}`,
  })
