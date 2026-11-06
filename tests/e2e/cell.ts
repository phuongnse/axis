import { randomBytes } from 'node:crypto'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser } from '@playwright/test'
import { startPublisher } from './admin-tool'
import { startDirectoryServer } from './directory-server'
import { required } from './server-settings'

const cellSettings = {
  Cell__Id: 'vn-1',
  Cell__Region: 'vn',
  Cell__TenantDomain: 'localhost',
  Cell__ConsoleHost: 'console.localhost',
}

// The export job stores its ZIP in the MinIO that scripts/e2e.sh starts. The server reads it back for the download.
const exportStorage = {
  Cell__ExportStorage__ServiceUrl: required('AXIS_E2E_EXPORT_STORAGE_URL', 'the address of the export storage'),
  Cell__ExportStorage__Region: required('AXIS_E2E_EXPORT_STORAGE_REGION', 'the region of the export storage'),
  Cell__ExportStorage__Bucket: required('AXIS_E2E_EXPORT_STORAGE_BUCKET', 'the bucket of the export storage'),
  Cell__ExportStorage__AccessKeyId: required('AXIS_E2E_EXPORT_STORAGE_ACCESS_KEY', 'the access key of the export storage'),
  Cell__ExportStorage__SecretAccessKey: required('AXIS_E2E_EXPORT_STORAGE_SECRET_KEY', 'the secret key of the export storage'),
}

/**
 * Starts a cell for a spec: a server on the console host and on the hosts of the tenants under `localhost`, the
 * publisher that runs the jobs the console queues, and a browser that maps every `*.localhost` host on port 443 to the
 * server. `portOffset` is the server's, see `startDirectoryServer`. `serverEnv` adds settings to the server only.
 * `stop` ends the browser, the publisher and the server, and removes the publisher's folder.
 */
export async function startCell(
  portOffset: number,
  serverEnv: Record<string, string> = {},
): Promise<{ server: Awaited<ReturnType<typeof startDirectoryServer>>; browser: Browser; stop(): Promise<void> }> {
  const identityProviderUrl = `${required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')}/`
  const server = await startDirectoryServer(portOffset, {
    ...cellSettings,
    ...exportStorage,
    Cell__ConsoleClientSecret: 'axis-cell-console-development-secret',
    IdentityProvider__Url: identityProviderUrl,
    ...serverEnv,
  })
  // The create job encrypts its first backup and creates the application's folder, so the publisher needs a backup key
  // and an applications folder.
  const folder = mkdtempSync(path.join(os.tmpdir(), 'axis-cell-'))
  const backupKeyFile = path.join(folder, 'backup.key')
  writeFileSync(backupKeyFile, randomBytes(32).toString('base64'))
  const applications = path.join(folder, 'applications')
  mkdirSync(applications)
  let publisher: Awaited<ReturnType<typeof startPublisher>> | undefined
  let browser: Browser | undefined
  const stop = async () => {
    await browser?.close()
    await publisher?.stop()
    await server.stop()
    rmSync(folder, { recursive: true, force: true })
  }
  try {
    // The run's shared publisher runs no provisioning jobs, so this one runs the jobs that the console queues.
    publisher = await startPublisher({
      ...cellSettings,
      ...exportStorage,
      Cell__BackupKeyFile: backupKeyFile,
      Studio__ApplicationsPath: applications,
      IdentityProvider__Url: identityProviderUrl,
    })
    browser = await chromium.launch({
      args: [`--host-resolver-rules=MAP *.localhost:443 127.0.0.1:${server.httpsPort}`],
    })
  } catch (error) {
    await stop()
    throw error
  }
  return { server, browser: browser!, stop }
}
