import { readFileSync } from 'node:fs'
import path from 'node:path'

const repoRoot = path.resolve(import.meta.dirname, '..', '..')

// The server compiles and activates all seven applications in the tenant when it starts: the generic test
// application, the purchase request sample, the leave request sample, the service desk sample, the asset management sample,
// the sub-process fixture and the host site fixture.
export const e2eApp = path.resolve(import.meta.dirname, 'fixtures', 'e2e-app')
export const purchaseRequests = path.resolve(repoRoot, 'samples', 'apps', 'purchase-requests')
export const leaveRequests = path.resolve(repoRoot, 'samples', 'apps', 'leave-requests')
/** A manifest and nothing else. The journey server activates it at start, because only startup activation migrates a tenant database. */
export const journeyBase = path.resolve(import.meta.dirname, 'fixtures', 'journey-base')
export const serviceDesk = path.resolve(repoRoot, 'samples', 'apps', 'service-desk')
export const assetManagement = path.resolve(repoRoot, 'samples', 'apps', 'asset-management')
/** A parent process that starts a child sub-process that waits on a 24-hour timer. Neither has a subject. */
export const subprocessApp = path.resolve(import.meta.dirname, 'fixtures', 'subprocess-app')
/** An application that pins the fixture module `checklists`. It is never activated, so Studio compiles it only in memory. */
export const moduleApp = path.resolve(import.meta.dirname, 'fixtures', 'module-app')
/** The fixture module `checklists`, which a spec installs as `checklists/1.0.0` under its own `Modules:Path`. */
export const checklistsModule = path.resolve(import.meta.dirname, 'fixtures', 'modules', 'checklists')
/** An application with a site that has a host of its own, see `hostSiteHost`. */
export const hostSiteApp = path.resolve(import.meta.dirname, 'fixtures', 'host-site-app')
/** The host of the site `host-site`. It is one of the default tenant's hosts, and the browser resolves any `*.localhost` name to the loopback address. */
export const hostSiteHost = 'hostsite.localhost'
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

// The main server's Studio edits a copy of the purchase request sample that scripts/e2e.sh makes for each run, so a
// saved resource never changes a file under samples/. No spec writes to this copy.
const studioFolder = required(
  'AXIS_E2E_STUDIO_FOLDER',
  'the copy of the purchase request sample that Studio edits',
)
const developmentStudioFolders: string[] = JSON.parse(
  readFileSync(path.join(serverProject, 'appsettings.Development.json'), 'utf8'),
).Studio.Folders
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
const journeyConnectionString = required(
  'AXIS_E2E_JOURNEY_DATABASE',
  'the PostgreSQL connection string of the journey tenant\'s runtime role',
)
const journeySchemaConnectionString = required(
  'AXIS_E2E_JOURNEY_SCHEMA_DATABASE',
  'the PostgreSQL connection string of the journey tenant\'s schema role',
)
/** The address of the journey tenant's own server. Its host is 127.0.0.2, because 127.0.0.1 and localhost are taken. */
export const journeyURL = required('AXIS_E2E_JOURNEY_URL', 'the address of the journey tenant\'s server')
/** The folder where Studio creates the journey tenant's applications. The journey server creates it at start. */
export const journeyApplications = required(
  'AXIS_E2E_JOURNEY_APPLICATIONS',
  'the folder where Studio creates the journey tenant\'s applications',
)
const keycloakUrl = required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')
const purchasingUrl = required('AXIS_E2E_PURCHASING_URL', 'the address of the purchasing system stub')

/** The address of the preview tenant on the same server: the tenant's host is localhost, not 127.0.0.1. */
export function previewURL(baseURL: string): string {
  return baseURL.replace('127.0.0.1', 'localhost')
}

/**
 * The environment of a server that listens at `baseURL`. Only the applications to activate are left to the
 * caller: the E2E server activates seven, and the activation helper one.
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
    // The host of the site host-site: it serves only that site, at its root. No other spec opens that site.
    Tenants__default__Hosts__1: hostSiteHost,
    [`Tenants__default__SiteHosts__${hostSiteHost}`]: 'host-site',
    Tenants__default__ConnectionString: connectionString,
    Tenants__default__SchemaConnectionString: schemaConnectionString,
    // The preview tenant default-preview. The server reaches it as localhost. It has no Oidc block, so it
    // takes the main tenant's, and no bindings. appsettings.Development.json would point it at localhost:5432.
    'Tenants__default-preview__PreviewOf': 'default',
    'Tenants__default-preview__Hosts__0': 'localhost',
    'Tenants__default-preview__ConnectionString': previewConnectionString,
    'Tenants__default-preview__SchemaConnectionString': previewSchemaConnectionString,
    // The development tenant keys, as in appsettings.Development.json.
    Tenants__default__DataKeys__1: 'AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=',
    Tenants__default__LookupKeys__1: 'AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI=',
    'Tenants__default-preview__DataKeys__1': 'AwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwM=',
    'Tenants__default-preview__LookupKeys__1': 'BAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQ=',
    // The development realm and its client, as in appsettings.Development.json.
    Tenants__default__Oidc__Authority: `${keycloakUrl}/realms/axis`,
    Tenants__default__Oidc__ClientId: 'axis',
    Tenants__default__Oidc__ClientSecret: 'axis-development-secret',
    // Endpoint calls need a token for this audience, as in appsettings.Development.json.
    Tenants__default__Oidc__EndpointAudience: 'axis-endpoints',
    Tenants__default__Oidc__ServiceAudience: 'axis-services',
    // Startup activation brings the realm in step through the axis-activation client, as in appsettings.Development.json.
    Tenants__default__Realm__Name: 'axis',
    Tenants__default__Realm__ActivationClientSecret: 'axis-activation-development-secret',
    // The users list reads the realm's users through the axis-users client, as in appsettings.Development.json.
    Tenants__default__Oidc__UsersClientSecret: 'axis-users-development-secret',
    // The purchase request sample activates only with its purchasing system bindings. They hold the
    // stub's default client credentials. The worker gets the same bindings from scripts/e2e.sh.
    Tenants__default__Bindings__PurchasingSystemUrl__Kind: 'url',
    Tenants__default__Bindings__PurchasingSystemUrl__Value: purchasingUrl,
    Tenants__default__Bindings__PurchasingSystemTokenUrl__Kind: 'url',
    Tenants__default__Bindings__PurchasingSystemTokenUrl__Value: `${purchasingUrl}/oauth/token`,
    Tenants__default__Bindings__PurchasingSystemClientId__Kind: 'secret',
    Tenants__default__Bindings__PurchasingSystemClientId__Value: 'axis-purchasing',
    Tenants__default__Bindings__PurchasingSystemClientSecret__Kind: 'secret',
    Tenants__default__Bindings__PurchasingSystemClientSecret__Value: 'axis-development-purchasing-secret',
    // The company realm's broker client, as in appsettings.Development.json. The purchase request sample's company provider reads them.
    Tenants__default__Bindings__CompanyIssuer__Kind: 'url',
    Tenants__default__Bindings__CompanyIssuer__Value: `${keycloakUrl}/realms/company`,
    Tenants__default__Bindings__CompanyClientId__Kind: 'secret',
    Tenants__default__Bindings__CompanyClientId__Value: 'axis-broker',
    Tenants__default__Bindings__CompanyClientSecret__Kind: 'secret',
    Tenants__default__Bindings__CompanyClientSecret__Value: 'axis-broker-development-secret',
    // The sample extension package payment-references, which scripts/build.sh installs. The worker gets
    // the same folder from scripts/e2e.sh.
    Extensions__Path: extensionsPath,
    // The sample modules departments and department-budgets, which scripts/build.sh installs. The worker gets the same folder from
    // scripts/e2e.sh.
    Modules__Path: modulesPath,
    // The sample templates, which scripts/build.sh installs. Only the server reads them.
    Templates__Path: templatesPath,
    // Studio edits the copy of the purchase request sample for the author role. appsettings.Development.json
    // lists every sample itself, and these blank entries keep the others out of every E2E server.
    Studio__Folders__0: studioFolder,
    ...Object.fromEntries(developmentStudioFolders.slice(1).map((_, i) => [`Studio__Folders__${i + 1}`, ''])),
    Studio__AuthorRole: 'author',
    Studio__PublisherRole: 'publisher',
    // Makes GET /api/me answer studioTest: true for an author, which shows the test controls.
    Studio__Tests: 'true',
    // Makes GET /api/me answer users: true for the administrator user, who holds this role.
    Users__AdministratorRole: 'tenant-administrator',
  }
}

/**
 * The environment of the journey server, which serves only the journey tenant. It runs in Testing, because
 * appsettings.Development.json would add the default tenants, the startup activation and the Studio folders, and an
 * environment variable cannot remove a list entry. Testing still allows plain-http session cookies. It shares the
 * platform database with the main server. It activates only the empty application journey-base at start, because only
 * startup activation migrates a tenant database, so a journey builds its own application in Studio.
 */
export function journeyServerEnv(): Record<string, string> {
  return {
    ASPNETCORE_URLS: journeyURL,
    ASPNETCORE_ENVIRONMENT: 'Testing',
    ConnectionStrings__Platform: connectionString,
    ConnectionStrings__PlatformSchema: schemaConnectionString,
    Platform__SecretKey: 'x7lhedPPY+Gx/xkG67W8XRybLeK3gghTBhDhDv9TIVw=',
    Tenants__journey__Hosts__0: '127.0.0.2',
    Tenants__journey__ConnectionString: journeyConnectionString,
    Tenants__journey__SchemaConnectionString: journeySchemaConnectionString,
    // The journey tenant is not in appsettings.Development.json, so its keys are set here.
    Tenants__journey__DataKeys__1: 'BQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU=',
    Tenants__journey__LookupKeys__1: 'BgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgY=',
    // The same realm and client as the default tenant.
    Tenants__journey__Oidc__Authority: `${keycloakUrl}/realms/axis`,
    Tenants__journey__Oidc__ClientId: 'axis',
    Tenants__journey__Oidc__ClientSecret: 'axis-development-secret',
    Tenants__journey__Oidc__EndpointAudience: 'axis-endpoints',
    Tenants__journey__Oidc__ServiceAudience: 'axis-services',
    Extensions__Path: extensionsPath,
    Modules__Path: modulesPath,
    Templates__Path: templatesPath,
    ActivateOnStartup__0: journeyBase,
    Studio__ApplicationsPath: journeyApplications,
    Studio__AuthorRole: 'author',
    Studio__PublisherRole: 'publisher',
    Studio__Tests: 'true',
  }
}
