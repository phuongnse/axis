import path from 'node:path'

const repoRoot = path.resolve(import.meta.dirname, '..', '..')

// The server compiles and activates all four applications in the tenant when it starts: the generic test
// application, the purchase request sample, the leave request sample and the service desk sample.
export const e2eApp = path.resolve(import.meta.dirname, 'fixtures', 'e2e-app')
export const purchaseRequests = path.resolve(repoRoot, 'samples', 'apps', 'purchase-requests')
export const leaveRequests = path.resolve(repoRoot, 'samples', 'apps', 'leave-requests')
export const serviceDesk = path.resolve(repoRoot, 'samples', 'apps', 'service-desk')
export const extensionsPath = path.resolve(repoRoot, 'artifacts', 'extensions')
export const modulesPath = path.resolve(repoRoot, 'artifacts', 'modules')
export const templatesPath = path.resolve(repoRoot, 'artifacts', 'templates')

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
const previewConnectionString = required(
  'AXIS_E2E_PREVIEW_DATABASE',
  'the PostgreSQL connection string of the preview tenant\'s runtime role',
)
const previewSchemaConnectionString = required(
  'AXIS_E2E_PREVIEW_SCHEMA_DATABASE',
  'the PostgreSQL connection string of the preview tenant\'s schema role',
)
const keycloakUrl = required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')
const purchasingUrl = required('AXIS_E2E_PURCHASING_URL', 'the address of the purchasing system stub')

/** The address of the preview tenant on the same server: the tenant's host is localhost, not 127.0.0.1. */
export function previewURL(baseURL: string): string {
  return baseURL.replace('127.0.0.1', 'localhost')
}

/**
 * The environment of a server that listens at `baseURL`. Only the applications to activate are left to the
 * caller: the E2E server activates four, and the activation helper one.
 */
export function serverEnv(baseURL: string): Record<string, string> {
  return {
    ASPNETCORE_URLS: baseURL,
    // Only Development lets a connector call the stub over http. Every setting the run needs is
    // set here, so it does not depend on which appsettings files the server finds.
    ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__Platform: connectionString,
    ConnectionStrings__PlatformSchema: schemaConnectionString,
    // The development key from appsettings.Development.json. A unit test keeps the two equal.
    Platform__SecretKey: 'x7lhedPPY+Gx/xkG67W8XRybLeK3gghTBhDhDv9TIVw=',
    // The browser reaches the server as 127.0.0.1, so that host is the tenant's.
    Tenants__default__Hosts__0: '127.0.0.1',
    Tenants__default__ConnectionString: connectionString,
    Tenants__default__SchemaConnectionString: schemaConnectionString,
    // The preview tenant default-preview. The server reaches it as localhost. It has no Oidc block, so it
    // takes the main tenant's, and no bindings. appsettings.Development.json would point it at localhost:5432.
    'Tenants__default-preview__PreviewOf': 'default',
    'Tenants__default-preview__Hosts__0': 'localhost',
    'Tenants__default-preview__ConnectionString': previewConnectionString,
    'Tenants__default-preview__SchemaConnectionString': previewSchemaConnectionString,
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
    // The sample templates, which scripts/build.sh installs. Only the server reads them.
    Templates__Path: templatesPath,
    // Studio edits the copy of the purchase request sample for the author role. appsettings.Development.json
    // lists the sample itself.
    Studio__Folders__0: studioFolder,
    Studio__AuthorRole: 'author',
    Studio__PublisherRole: 'publisher',
    // Makes GET /api/me answer studioTest: true for an author, which shows the test controls.
    Studio__Tests: 'true',
  }
}
