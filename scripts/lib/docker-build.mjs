// Runs a Docker build and runs it again when it fails, because a download in the build (npm, NuGet, Debian or
// PostgreSQL packages) or the registry lookup of a base image can fail at times. Docker reuses the layers that
// succeeded, so a retry reruns only the failed step. Same attempts and waits as scripts/lib/retry.sh. A call with
// `timeoutSeconds` stops a try that runs past it, so that a registry that stalls fails the try and the next one starts,
// see docs/development.md#setup.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../..', import.meta.url))

export const runDocker = async (
  args,
  { env = process.env, attempts = 3, label = `docker ${args.join(' ')}`, timeoutSeconds } = {},
) => {
  let status
  for (let attempt = 1; attempt <= attempts; attempt++) {
    const result = spawnSync('docker', args, {
      cwd: root,
      env,
      stdio: 'inherit',
      timeout: timeoutSeconds ? timeoutSeconds * 1000 : undefined,
    })
    status = result.error?.code === 'ETIMEDOUT' ? `no end in ${timeoutSeconds} seconds` : `status ${result.status}`
    if (result.status === 0) return
    if (attempt === attempts) break
    console.error(`Attempt ${attempt} of ${attempts} failed with ${status}: docker ${args.join(' ')}`)
    const seconds = Number(env.AXIS_RETRY_DELAY_SECONDS ?? 15) * 2 ** (attempt - 1)
    await new Promise((resolve) => setTimeout(resolve, seconds * 1000))
  }
  assert.fail(`${label} failed after ${attempts} attempts with ${status}`)
}

// `cacheFrom` names an image that Docker takes cached layers from, and `buildArgs` are more `NAME=value` build
// arguments. Each is left out of the command when unset, and so is the version of a stage that takes none.
export const buildImage = ({ target, version, tag, attempts = 3, cacheFrom, buildArgs = [] }) =>
  runDocker(
    [
      'build',
      '--target',
      target,
      ...(version ? ['--build-arg', `AXIS_VERSION=${version}`] : []),
      ...buildArgs.flatMap((arg) => ['--build-arg', arg]),
      ...(cacheFrom ? ['--cache-from', cacheFrom] : []),
      '-t',
      tag,
      '.',
    ],
    { attempts, label: `docker build --target ${target}` },
  )
