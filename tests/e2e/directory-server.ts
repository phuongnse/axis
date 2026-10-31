import { runServer, sparePort } from './activate'
import {
  extensionsPath,
  modulesPath,
  platformSecretKey,
  required,
  templatesPath,
} from './server-settings'

/**
 * Starts a server that reads its tenants from the tenant directory, not from configuration, and serves them over
 * https on `AXIS_E2E_TENANT_HTTPS_PORT` with the certificate that scripts/e2e.sh makes for the run. It also listens
 * on plain http on a spare port, only for the readiness check, because Node's fetch refuses the self-signed
 * certificate. It runs in Testing, because appsettings.Development.json lists tenants and startup activation, which
 * the directory source refuses, and Testing still allows the plain-http address of Keycloak. It sets
 * `Users:AdministratorRole` to the value of appsettings.Development.json, because Testing has none and nobody could
 * use the users page without it. It reads the tenant
 * directory when it starts, so it serves every tenant that `tenant create` has made by then. It shares the platform
 * database with the other E2E servers. Returns once the server is ready. `stop` ends it, see `runServer`.
 */
export async function startDirectoryServer(): Promise<{ httpsPort: number; stop(): Promise<void> }> {
  const httpsPort = Number(
    required('AXIS_E2E_TENANT_HTTPS_PORT', 'the https port of the server of a created tenant'),
  )
  const httpUrl = `http://127.0.0.1:${await sparePort()}`
  const { stop } = await runServer(httpUrl, {
    ASPNETCORE_URLS: `${httpUrl};https://127.0.0.1:${httpsPort}`,
    ASPNETCORE_ENVIRONMENT: 'Testing',
    Tenancy__Source: 'directory',
    Users__AdministratorRole: 'tenant-administrator',
    ConnectionStrings__Platform: required('AXIS_E2E_DATABASE', 'the PostgreSQL connection string of the runtime role'),
    ConnectionStrings__PlatformSchema: required(
      'AXIS_E2E_SCHEMA_DATABASE',
      'the PostgreSQL connection string of the schema role',
    ),
    Platform__SecretKey: platformSecretKey,
    Kestrel__Certificates__Default__Path: required('AXIS_E2E_TLS_CERT', 'the path of the certificate of the run'),
    Kestrel__Certificates__Default__KeyPath: required('AXIS_E2E_TLS_KEY', 'the path of the private key of the run'),
    Extensions__Path: extensionsPath,
    Modules__Path: modulesPath,
    Templates__Path: templatesPath,
  })
  return { httpsPort, stop }
}
