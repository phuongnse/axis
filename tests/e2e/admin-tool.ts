import { spawn } from 'node:child_process'
import path from 'node:path'
import { extensionsPath, modulesPath, platformSecretKey, required, templatesPath } from './server-settings'

/** The built admin tool. scripts/build.sh builds it in Release. */
const adminDll = path.resolve(import.meta.dirname, '..', '..', 'src', 'Axis.Admin', 'bin', 'Release', 'net10.0', 'Axis.Admin.dll')

/**
 * The settings of `Axis.Admin` for this run. scripts/e2e.sh gives Playwright only the `AXIS_E2E_*` names, so the
 * server, the worker and the publisher never inherit these. The tool runs in Development, which allows the plain
 * http address of Keycloak and plain SMTP, and reads tenants from the tenant directory, as the tenant setting commands such
 * as `tenant host add` need. Keycloak reaches Mailpit by its alias on the run's network.
 */
function adminEnv(): Record<string, string> {
  return {
    DOTNET_ENVIRONMENT: 'Development',
    Tenancy__Source: 'directory',
    Admin__ConnectionString: required('AXIS_E2E_ADMIN_DATABASE', 'the PostgreSQL connection string of the axis_admin role'),
    ConnectionStrings__PlatformSchema: required('AXIS_E2E_SCHEMA_DATABASE', 'the PostgreSQL connection string of the schema role'),
    Platform__SecretKey: platformSecretKey,
    IdentityProvider__Url: `${required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')}/`,
    IdentityProvider__AdminClientId: 'axis-admin',
    IdentityProvider__AdminClientSecret: required('AXIS_E2E_IDENTITY_ADMIN_SECRET', 'the secret of the axis-admin client'),
    Email__Smtp__Host: 'mailpit',
    Email__Smtp__Port: '1025',
    Email__Smtp__UseTls: 'false',
    Email__From: 'axis@localhost',
  }
}

export interface AdminResult {
  status: number | null
  stdout: string
  stderr: string
}

/**
 * Runs `Axis.Admin` with `args` against the E2E stack, for example `['tenant', 'create', 'acme', ...]`, and waits for
 * it to end. It kills the tool after `timeoutMs`, which leaves `status` null.
 */
export function runAdmin(args: string[], timeoutMs = 120_000): Promise<AdminResult> {
  return new Promise((resolve, reject) => {
    const child = spawn('dotnet', [adminDll, ...args], {
      env: { ...process.env, ...adminEnv() },
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    let stdout = ''
    let stderr = ''
    child.stdout.setEncoding('utf8').on('data', (chunk: string) => (stdout += chunk))
    child.stderr.setEncoding('utf8').on('data', (chunk: string) => (stderr += chunk))
    const timer = setTimeout(() => child.kill('SIGKILL'), timeoutMs)
    child.on('error', (error) => {
      clearTimeout(timer)
      reject(error)
    })
    child.on('close', (status) => {
      clearTimeout(timer)
      resolve({ status, stdout, stderr })
    })
  })
}

/**
 * Starts `Axis.Admin publisher` against the tenant directory, with `env` as the settings of a cell, and returns once it
 * has run for a moment. The run's shared publisher reads the `Tenants` section, so it runs no provisioning jobs. This one
 * reads the tenant directory, so it runs the jobs of `platform.jobs` that a spec queues, such as the console's suspend and
 * resume. It also claims the publish and host-check work of every tenant in the directory, so a spec stops it when it
 * is done. It prints no ready line, so a start that exits within 2 seconds throws with its output. `stop` ends it with
 * SIGTERM, and with SIGKILL after 15 seconds.
 */
export async function startPublisher(env: Record<string, string> = {}): Promise<{ stop(): Promise<void> }> {
  const child = spawn('dotnet', [adminDll, 'publisher'], {
    env: {
      ...process.env,
      ...adminEnv(),
      Extensions__Path: extensionsPath,
      Modules__Path: modulesPath,
      Templates__Path: templatesPath,
      ...env,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  let output = ''
  child.stdout.setEncoding('utf8').on('data', (chunk: string) => (output += chunk))
  child.stderr.setEncoding('utf8').on('data', (chunk: string) => (output += chunk))
  let exitCode: number | null | undefined
  const exited = new Promise<void>((resolve) =>
    child.once('close', (code) => {
      exitCode = code ?? -1
      resolve()
    }),
  )
  const stop = async () => {
    if (exitCode === undefined) {
      child.kill('SIGTERM')
      // A publisher that ignores SIGTERM must not hang the spec.
      const killer = setTimeout(() => child.kill('SIGKILL'), 15_000)
      await exited
      clearTimeout(killer)
    }
  }
  await new Promise((resolve) => setTimeout(resolve, 2_000))
  if (exitCode !== undefined) {
    throw new Error(`The publisher exited with code ${exitCode} at its start. Publisher output:\n${output}`)
  }
  return { stop }
}
