import path from 'node:path'

const repoRoot = path.resolve(import.meta.dirname, '..', '..')

// The server compiles and activates all three applications in the tenant when it starts: the generic test
// application, the purchase request sample and the leave request sample.
export const e2eApp = path.resolve(import.meta.dirname, 'fixtures', 'e2e-app')
export const purchaseRequests = path.resolve(repoRoot, 'samples', 'apps', 'purchase-requests')
export const leaveRequests = path.resolve(repoRoot, 'samples', 'apps', 'leave-requests')
export const extensionsPath = path.resolve(repoRoot, 'artifacts', 'extensions')
export const modulesPath = path.resolve(repoRoot, 'artifacts', 'modules')

/** The built server, and the folder it runs from. scripts/build.sh builds it in Release. */
export const serverProject = path.resolve(repoRoot, 'src', 'Axis.Server')
export const serverDll = path.resolve(serverProject, 'bin', 'Release', 'net10.0', 'Axis.Server.dll')

function required(name: string, meaning: string): string {
  const value = process.env[name]
  if (!value) {
    throw new Error(`${name} must hold ${meaning}. Run scripts/e2e.sh.`)
  }
  return value
}

// Studio edits a copy of the purchase request sample that scripts/e2e.sh makes for each run, so a saved
// resource never changes a file under samples/.
export const studioFolder = required(
  'AXIS_E2E_STUDIO_FOLDER',
  'the copy of the purchase request sample that Studio edits',
)
const connectionString = required('AXIS_E2E_DATABASE', 'the PostgreSQL connection string of the runtime role')
const schemaConnectionString = required(
  'AXIS_E2E_SCHEMA_DATABASE',
  'the PostgreSQL connection string of the schema role',
)
const keycloakUrl = required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')
const purchasingUrl = required('AXIS_E2E_PURCHASING_URL', 'the address of the purchasing system stub')

/**
 * The environment of a server that listens at `baseURL`. Only the applications to activate are left to the
 * caller: the E2E server activates three, and the activation helper one.
 */
export function serverEnv(baseURL: string): Record<string, string> {
  return {
    ASPNETCORE_URLS: baseURL,
    // Only Development lets a connector call the stub over http. Every setting the run needs is
    // set here, so it does not depend on which appsettings files the server finds.
    ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__Platform: connectionString,
    // The browser reaches the server as 127.0.0.1, so that host is the tenant's.
    Tenants__default__Hosts__0: '127.0.0.1',
    Tenants__default__ConnectionString: connectionString,
    Tenants__default__SchemaConnectionString: schemaConnectionString,
    // The development realm and its client, as in appsettings.Development.json.
    Tenants__default__Oidc__Authority: `${keycloakUrl}/realms/axis`,
    Tenants__default__Oidc__ClientId: 'axis',
    Tenants__default__Oidc__ClientSecret: 'axis-development-secret',
    // Endpoint calls need a token for this audience, as in appsettings.Development.json.
    Tenants__default__Oidc__EndpointAudience: 'axis-endpoints',
    // The purchase request sample activates only with its purchasing system bindings. The stub
    // ignores the token. The worker gets the same bindings from scripts/e2e.sh.
    Tenants__default__Bindings__PurchasingSystemUrl__Kind: 'url',
    Tenants__default__Bindings__PurchasingSystemUrl__Value: purchasingUrl,
    Tenants__default__Bindings__PurchasingSystemToken__Kind: 'secret',
    Tenants__default__Bindings__PurchasingSystemToken__Value: 'e2e-token',
    // The sample extension package payment-references, which scripts/build.sh installs. The worker gets
    // the same folder from scripts/e2e.sh.
    Extensions__Path: extensionsPath,
    // The sample module departments, which scripts/build.sh installs. The worker gets the same folder from
    // scripts/e2e.sh.
    Modules__Path: modulesPath,
    // Studio edits the copy of the purchase request sample for the author role. appsettings.Development.json
    // lists the sample itself.
    Studio__Folders__0: studioFolder,
    Studio__AuthorRole: 'author',
  }
}
