# Threat model

What can go wrong in Axis's own design, and what Axis does about each threat. The model follows
[D106](../decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md). Its threat rows are
built. Each row comes from the issue that models one trust boundary, and the
[ASVS](asvs.md) page covers the controls that every web application needs.

## How it is filled

- **The four questions.** The Threat Modeling Manifesto frames the model. What are we working on?
  What can go wrong? What are we going to do about it? Did we do a good job?
- **Methods.**
  - STRIDE finds the threats at each trust boundary.
  - LINDDUN finds the privacy threats to personal data.
  - OWASP's agentic AI threats find the threats to the AI agents that write and review code.
- **One row per threat.** The table has these columns: id, threat, boundary, category, rating,
  status and evidence. A scope letter and a number make an id, such as `P1`, `I1` or `B1`. An id is
  unique on the whole page, so a later row or issue can name a threat by its id alone.
- **Rating.** The rating is Low, Medium, High or Critical. It comes from likelihood and impact, a
  short form of the OWASP Risk Rating Methodology.
  - A **Gap** has time to close, as a vulnerable dependency does, see
    [dependency updates](../development.md#dependency-updates). Critical is 7 days, High 30 days,
    Medium 90 days (Moderate in that table) and Low the next routine update.
  - The time starts on the day the row is written.
- **Status.** Each row has one of four statuses.
  - **Mitigated:** Axis has a control. The evidence names a test class in backticks, ending in
    `Tests`, a doc section or a check.
  - **Gap:** Axis does not have the control yet. The evidence links the GitHub issue that closes it.
  - **Delegated:** the identity provider or the operator meets the threat. The evidence names which.
  - **Accepted:** the owner accepts the threat. The evidence gives the reason and the date, as
    YYYY-MM-DD. Only the owner accepts a threat.
- **Checked.** `scripts/check-docs.mjs` fails on these problems:
  - an id that another row already uses, on the whole page
  - a status that is not one of the four above
  - a rating that is missing or is not one of the four above
  - a row without evidence
  - a **Mitigated** row that names a test class no `.cs` file under `tests/` or `samples/` declares
  - a **Gap** row that links no GitHub issue
  - an **Accepted** row that gives no date
  - It does not judge that a test covers its threat, that a time frame was kept, or that a
    **Delegated** row names the right party. A reviewer does.
- **Kept current.** A change that adds a trust boundary updates this page in the same change, see
  [the threat model rule](../delivery.md#keeping-the-docs-current). Each docs sweep reviews the page.
  Once a year the owner rates Axis against OWASP SAMM _(planned for M32)_, see
  [ASVS other evidence](asvs.md#other-evidence).

## The product

The server, the worker, the SPA, tenants, Studio and authoring, extension packages, connectors,
endpoints and webhooks, and the identity provider Axis ships.

```mermaid
flowchart LR
    subgraph browser [Browser]
        Spa[SPA and Studio]
    end
    subgraph server [Server]
        Api[Server API]
    end
    subgraph tenantdb [Tenant database]
        TenantDb[(Tenant databases)]
    end
    subgraph worker [Worker]
        Steps[Process steps]
    end
    subgraph package [Extension package]
        Code[Custom C# code]
    end
    subgraph connector [Connector]
        External[External system]
    end
    subgraph idp [Identity provider]
        Idp[Axis identity provider]
    end
    Spa -->|session cookie| Api
    Api --> TenantDb
    Api -->|OIDC| Idp
    Steps --> TenantDb
    Steps --> Code
    Steps --> External
```

### Browser and server

The browser holds a session cookie that the server issues. The server checks each page and each write against that session.

```mermaid
flowchart LR
    subgraph browser [Browser]
        Page[SPA and Studio]
    end
    subgraph server [Server]
        Session[Session and CSRF check]
    end
    Page -->|session cookie and CSRF header| Session
    Session -->|secure cookie| Page
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P1 | A stolen or fixed session cookie signs someone in as another user. | Browser and server | Spoofing | High | **Mitigated** | `SignInTests` (a copied cookie stops working after sign out, and a new sign-in ends the old session), `SessionCookieTests` (the host cookie is Secure, with path root and no domain). |
| P2 | A page on another site makes the signed-in user write data. | Browser and server | Tampering | High | **Mitigated** | `CsrfHeaderTests` (writes without the header get a 403 and change nothing). |
| P3 | A change is made and no record shows who made it. | Browser and server | Repudiation | Medium | **Mitigated** | `AuditRecordTests` (each create, update and delete adds one record with the signed-in user as actor). |
| P4 | Injected script or a framing page reads or changes the Axis pages. | Browser and server | Information disclosure | High | **Mitigated** | `SecurityHeadersTests` (every answer sends a Content Security Policy that forbids framing, and the nosniff header). |
| P5 | A sign-in sends the user on to an outside address. | Browser and server | Spoofing | Medium | **Mitigated** | `SignInEndpointTests` (a return address that is not a local path is a 400 problem). |
| P6 | A flood of sign-ins or writes slows the service for other users. | Browser and server | Denial of service | Medium | **Mitigated** | `RateLimitTests` (a user past the session limit is refused while other users still pass). |
| P7 | A user reads or changes a page or record without a grant. | Browser and server | Elevation of privilege | High | **Mitigated** | `RecordPolicyTests` (every route is a 401 without a session and a 403 without a grant), `AuthorizationRouteTests` (every route off the open list answers 401 or 403 with no session and no token), `PageAccessTests` (a department manager can read purchase requests but cannot submit them). |
| P8 | A session stays open past its time limit or after an admin ends it. | Browser and server | Elevation of privilege | Medium | **Mitigated** | `SessionLifetimeTests` (a session ends after an hour idle or eight hours after sign in), `AdminTenantSessionEndTests` (ending one person's sessions leaves other people signed in). |
| P100 | Crafted input to a parser, in a record body, an import file or an expression, crashes the parser or reaches SQL. | Browser and server | Tampering | High | **Mitigated** | `RecordBodyFuzzTests` (a body is accepted or refused with a 400 problem), `RecordImportFuzzTests` (any bytes give rows or refusals), `ExpressionFuzzTests` (no SQL the translator emits holds input text outside a parameter). |

### Server and databases

The server finds each tenant from the host. It then reads and writes that tenant's database as the runtime role.

```mermaid
flowchart LR
    subgraph server [Server and worker]
        Api[Server and worker]
    end
    subgraph platform [Platform database]
        Catalog[Tenant catalog]
    end
    subgraph tenants [Tenant databases]
        TenantDb[One database per tenant]
    end
    Api -->|find tenant by host| Catalog
    Api -->|runtime role over verify-full TLS| TenantDb
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P9 | A request for one tenant's host reaches another tenant's data. | Server and databases | Spoofing | Critical | **Mitigated** | `TenantResolutionTests` (an unknown host gets a 404 that names no tenant), `TenantIsolationTests` (a request on tenant a asks the catalog only for tenant a). |
| P10 | A tenant reads or changes another tenant's rows. | Server and databases | Information disclosure | Critical | **Mitigated** | `TenantIsolationTests` (a row written for tenant a is read only through tenant a). |
| P11 | A database connection without verified TLS lets someone alter the traffic. | Server and databases | Tampering | High | **Mitigated** | `DatabaseTlsRequirementTests` (a mode below verify-full is refused outside Development), `TenantDatabaseTlsTests` (each SSL mode maps to its libpq value, and a named root certificate is used). |
| P12 | A database change leaves no audit record, or an audit record is edited. | Server and databases | Repudiation | Medium | **Mitigated** | `AuditRecordTests` (a failed write adds no record, and the database rejects any change to an audit record). |
| P13 | The server or worker uses a database role with more rights than it needs. | Server and databases | Elevation of privilege | High | **Mitigated** | `DatabaseRoleTests` (every server and worker session on a tenant database uses the runtime role). |
| P14 | A tenant's secret is read from the database in clear text. | Server and databases | Information disclosure | High | **Mitigated** | `TenantSecretCipherTests` (an encrypted value hides its plain text and does not decrypt for another tenant or field). |
| P15 | A slow read holds a database connection for too long. | Server and databases | Denial of service | Medium | **Mitigated** | `QueryTimeoutTests` (a slow read answers 503, and is logged and counted). |

### Caller and server

A machine caller, such as the purchasing system, sends a bearer token and a DPoP proof to an inbound endpoint.

```mermaid
flowchart LR
    subgraph caller [Caller]
        Machine[Purchasing system with a service account]
    end
    subgraph server [Server]
        Endpoint[Inbound endpoint]
    end
    subgraph db [Tenant database]
        Records[Records and audit records]
    end
    Machine -->|bearer token and DPoP proof| Endpoint
    Endpoint -->|invoke grant checked| Records
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P16 | A token that is unusable, forged or meant for another use is accepted. | Caller and server | Spoofing | High | **Mitigated** | `EndpointCallerTests` (an ID token is a 401 even with the expected audience), `ServiceAccountBearerOptionsTests` (both bearer schemes accept only asymmetric signature algorithms). |
| P17 | A bearer token of one tenant opens another tenant's endpoints. | Caller and server | Spoofing | Critical | **Mitigated** | `EndpointCallerTests` (a valid token of tenant a is a 401 on another tenant's host), `ServiceAccountBearerOptionsTests` (a request of another tenant gets no result from the service scheme). |
| P18 | A stolen bearer token is used with a DPoP proof made for another request. | Caller and server | Spoofing | High | **Mitigated** | `DpopProofTests` (a proof for another address, method or access token fails, and so does a proof outside its time window). |
| P19 | A repeated or changed request writes a second record. | Caller and server | Tampering | Medium | **Mitigated** | `EndpointCallTests` (a repeat with the same key returns the first answer, and the same key with other input is a 422 that writes nothing). |
| P20 | A record is created or started with no record of which caller did it. | Caller and server | Repudiation | Medium | **Mitigated** | `EndpointCallTests` (the create and the start are audited with the caller as actor). |
| P21 | A machine caller reads data beyond its own routes or grants. | Caller and server | Information disclosure | High | **Mitigated** | `ServiceAccountRouteTests` (a service token without a session is a 401 on every route outside the service account and endpoint routes), `ServiceAccountRecordTests` (a service account has no grant on another entity, and its record filter hides rows, and the operator commands are 403 without a grant, 404 for a hidden subject and 401 for a token of the wrong audience), `DpopCallerTests` (a plain bearer token is a 401 on every bearer route under `fapi2`). |
| P22 | A machine caller floods the server with requests. | Caller and server | Denial of service | Medium | **Mitigated** | `RateLimitRouteTests` (every route has a partition, and an endpoint route also has the bearer partition). |
| P23 | A caller without the invoke grant starts a process. | Caller and server | Elevation of privilege | High | **Mitigated** | `EndpointCallTests` (a caller without the invoke grant gets a 403 before the body and writes nothing), `ServiceAccountRecordTests` (a service account without the grant gets a 403 on a data source and a process). |

### Worker and outside system

The worker calls an outside system through a connector. It uses the tenant's bound URL and secret.

```mermaid
flowchart LR
    subgraph worker [Worker]
        Step[Connector call step]
    end
    subgraph secrets [Tenant secrets]
        Token[Bearer token binding]
    end
    subgraph outside [Outside system]
        Ext[Purchasing system over https]
    end
    Step -->|reads binding| Token
    Step -->|https request with bearer token| Ext
    Ext -->|status and body| Step
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P24 | A connector call goes over plain http, where someone else can answer for the outside system. | Worker and outside system | Spoofing | High | **Mitigated** | `ConnectorCallTests` (outside Development the worker sends nothing to an http URL), and the [deployment bindings](integration-resources.md#deployment-bindings) rule that a URL binding is https outside Development. |
| P25 | A reply that does not fit the expected output is written as a result. | Worker and outside system | Tampering | Medium | **Mitigated** | `ConnectorCallTests` (a 200 whose body does not fit leaves the call unknown, even with an idempotency key). |
| P26 | The connector token appears in a log, history row or audit record. | Worker and outside system | Information disclosure | High | **Mitigated** | `SecretValueTests` (a secret shows no part of its value in text, JSON or logs), `ConnectorCallTests` (no audit record, history row, call row or log entry holds the secret token). |
| P27 | A call is redirected to a host the connector does not name. | Worker and outside system | Elevation of privilege | High | **Mitigated** | [Connector resource](integration-resources.md#connector-resource) (the path is added to the bound base URL, and the worker never follows a redirect), `ConnectorCallTests` (each tenant call uses its own URL and token). |
| P28 | A slow or failing outside system ties up worker capacity. | Worker and outside system | Denial of service | Medium | **Mitigated** | `ConnectorCallBackoffTests` (the wait doubles after each attempt, up to an hour). |
| P29 | A connector call happens with no record in the step history. | Worker and outside system | Repudiation | Low | **Mitigated** | `ProcessConnectorStepTests` (a connector step writes its history and a staged call audit record without input values). |

### Worker and extension package

Extension package code runs inside the worker process until the package runners of M21 exist.

```mermaid
flowchart LR
    subgraph worker [Worker process]
        Steps[Process steps]
        Pkg[Extension package code]
    end
    subgraph tenants [Every tenant]
        Data[Secrets and data of each tenant]
    end
    subgraph outside [Outside systems]
        Ext[Any host the package can reach]
    end
    Steps -->|operation call| Pkg
    Pkg -->|same process, no isolation| Data
    Pkg -->|can reach| Ext
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P30 | Package code reads the secrets or data of other tenants inside the worker. | Worker and extension package | Information disclosure | High | **Delegated** | The operator. Packages are trusted code that the operator installs and reviews, see [Not a sandbox](extension-packages.md#not-a-sandbox). |
| P31 | Package code goes around its capability limits inside the worker. | Worker and extension package | Elevation of privilege | High | **Delegated** | The operator, see [Not a sandbox](extension-packages.md#not-a-sandbox). The package runners of M21 are not built yet. |
| P32 | A package by mistake reads an undeclared secret or calls an undeclared host. | Worker and extension package | Elevation of privilege | Medium | **Mitigated** | `PackageCapabilityTests` (an undeclared secret fails the instance, and a call to another host fails the call). It stops mistakes, not attacks, see [Not a sandbox](extension-packages.md#not-a-sandbox). |
| P33 | A package version is swapped after a release pins another one. | Worker and extension package | Tampering | Medium | **Mitigated** | `PackagePinningTests` (an instance keeps the version its release pins), `PackageCatalogTests` (an assembly that differs from its manifest version is a diagnostic). |
| P34 | A package call runs with no record of its operation. | Worker and extension package | Repudiation | Low | **Mitigated** | `PackageCallTests` (a settled call writes exactly one called audit record and records the operation identity). |
| P35 | A package operation never returns and holds a worker thread. | Worker and extension package | Denial of service | Medium | **Delegated** | The operator, see [Not a sandbox](extension-packages.md#not-a-sandbox). The timeout stops the wait, but the code keeps running in the worker. |

Spoofing does not apply. The operator installs each package, and a caller does not send a package an identity.

Webhooks come with M19.

### Authoring

Authoring covers what an author changes in Studio, what a release does once it is live, and what a
borrowed module or template brings in. It has five boundaries, each with its own diagram below.

**Author and release**

```mermaid
flowchart LR
    subgraph author [Author]
        Writer[Author with the author role]
    end
    subgraph files [Application files]
        Definitions[Policies, roles, rules and data sources]
    end
    subgraph release [Release]
        Compiled[Compiled release]
    end
    subgraph runtime [Running server]
        Enforced[Axis.Policy and data source SQL]
    end
    Writer -->|edits| Definitions
    Definitions -->|compiled into| Compiled
    Compiled -->|enforced by| Enforced
```

**Studio and the authoring API**

```mermaid
flowchart LR
    subgraph browser [Browser]
        Studio[Studio in the SPA]
    end
    subgraph server [Server]
        Routes[Authoring API with session and CSRF header]
    end
    subgraph folder [Application folder]
        Json[JSON files on disk]
    end
    Studio -->|session cookie and CSRF header| Routes
    Routes -->|writes after the version check| Json
```

**Publishing with activation and rollback**

```mermaid
flowchart LR
    subgraph server [Server]
        Request[Stores publish request, no schema role]
    end
    subgraph tenantdb [Tenant database]
        Requests[(Publish requests and releases)]
    end
    subgraph publisher [Publisher]
        Activate[Validates and activates or rolls back with schema role]
    end
    subgraph running [Server and worker]
        Instances[Running instances stay on their release]
    end
    Request -->|stores request| Requests
    Requests -->|claimed by| Activate
    Activate -->|sets the active release| Requests
    Requests -->|new work uses the active release| Instances
```

**Preview tenants**

```mermaid
flowchart LR
    subgraph maintenant [Main tenant]
        MainStudio[Studio on the main host]
        MainDb[(Main tenant database)]
    end
    subgraph publisher [Publisher]
        Run[Runs the preview request]
    end
    subgraph previewtenant [Preview tenant]
        PreviewDb[(Preview database and bindings)]
        PreviewHost[Preview host and its own sessions]
    end
    MainStudio -->|preview request| MainDb
    MainDb -->|claimed by| Run
    Run -->|activates on the preview only| PreviewDb
    PreviewHost -->|its own tenant only| PreviewDb
```

**Modules and templates**

```mermaid
flowchart LR
    subgraph operator [Operator]
        Folders[Modules:Path and Templates:Path folders]
    end
    subgraph compiler [Compiler]
        Copy[Copies pinned module files into the release]
    end
    subgraph release [Release]
        Hashed[Module files under the content hash]
    end
    subgraph studio [Studio]
        Create[POST /api/studio/apps copies a template]
    end
    subgraph appfolder [Application folder]
        Application[New application files]
    end
    Folders -->|installed modules| Copy
    Copy -->|copied into| Hashed
    Folders -->|installed templates| Create
    Create -->|writes| Application
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P36 | An author widens access by changing a policy or a role. | Author and release | Elevation of privilege | High | **Mitigated** | `StudioPublishTests` and `StudioRollbackTests` show that publishing and rolling back need the publisher role. `PolicyMatrixTests` shows what each policy grants. See [publishing](authoring.md#publishing). |
| P37 | One person holds the author role and the publisher role, and widens access alone. | Author and release | Elevation of privilege | Medium | **Delegated** | The operator decides who holds each role at the identity provider. See [Studio settings](host-settings.md#studio-settings). |
| P38 | An author denies having published a release. | Author and release | Repudiation | Low | **Mitigated** | `AdminPublisherTests` writes one audit record for each publish. `StudioReleaseHistoryTests` lists each activation, newest first. See [audit](authoring.md#audit). |
| P39 | A data source shows a field that the user may not read. | Author and release | Information disclosure | Medium | **Mitigated** | `DataSourcePolicyTests` answers 403 for the whole data source. See [data source authorization](data-sources.md#authorization). |
| P40 | An author expression injects SQL. | Author and release | Tampering | High | **Mitigated** | `SqlTranslatorTests` checks that text and names never reach the SQL unquoted, and that literals are typed parameters. |
| P41 | A user without the author role calls the Studio API. | Studio API | Elevation of privilege | High | **Mitigated** | `StudioEndpointTests` answers 403 without the author role and 401 without a session. `StudioTextWriteTests` does the same for text writes. |
| P42 | A site in another origin sends a write as a signed-in author. | Studio API | Tampering | Medium | **Mitigated** | `StudioEndpointTests` refuses a write without the CSRF header. `CsrfHeaderTests` does the same for record writes. |
| P43 | A write escapes the application folder through `..`, a link or a folder link. | Studio API | Tampering | High | **Mitigated** | `StudioEndpointTests` refuses a name with `..` and does not write through a linked file or folder. `StudioCreateApplicationTests` refuses a name with `..`. |
| P44 | Two authors overwrite each other's changes. | Studio API | Tampering | Medium | **Mitigated** | `StudioWriteLockTests` gives one of two writes that share a version a 409. See [versions](authoring.md#versions). |
| P45 | The live release differs from the folder the author sent to publish. | Publishing | Tampering | Medium | **Mitigated** | `StudioPublishTests` shows that the release keeps the folder as it was at the call. A later edit is not in it. |
| P46 | A publish from one tenant's host changes another tenant. | Publishing | Elevation of privilege | High | **Mitigated** | `StudioPublishTests` stores the request in that tenant's database only. `AdminPublisherTests` activates the release for that tenant only. |
| P47 | A new release changes work that is already running. | Publishing | Tampering | High | **Mitigated** | Running instances stay on the release they started on. `StudioRollbackTests`, `TaskReleaseTests` and `PinnedReleaseStartTests` cover this. |
| P48 | Activation or rollback breaks data that is already stored. | Publishing | Tampering | High | **Mitigated** | `MigrationActivationTests` and `ModuleUpgradeTests` refuse a change that would break stored data, and change nothing. `PublisherRollbackTests` fails a rollback across a rename. |
| P49 | A request keeps crashing the publisher and blocks the queue. | Publishing | Denial of service | Medium | **Mitigated** | `AdminPublisherTests` fails a request with `AXC0215` after three expired claims. |
| P50 | The server activates a release or holds a schema connection. | Publishing | Elevation of privilege | High | **Mitigated** | The server stores a publish request and never activates. Only the publisher holds the tenant schema role. See [publishing](authoring.md#publishing) and [D47](../decisions/d47-publishing-from-studio.md). |
| P51 | A test request reaches the tenant's data. | Publishing | Information disclosure | Medium | **Mitigated** | `PublisherTestRequestTests` runs scenarios in their own database and leaves no record in the tenant database. |
| P52 | A preview reaches its main tenant's data. | Preview tenant | Information disclosure | High | **Mitigated** | `PublisherPreviewTests` activates the release on the preview tenant only. `StudioPreviewTests` leaves the main tenant unchanged. See [preview tenants](../architecture.md#preview-tenants). |
| P53 | A preview uses the main tenant's bindings. | Preview tenant | Information disclosure | Medium | **Mitigated** | `PreviewTenantActivationTests` gives the preview its own bindings, empty by default. `ReleaseActivatorTests` warns for each missing binding on a preview. |
| P54 | A session made on the preview host is used on the main host, or the other way round. | Preview tenant | Spoofing | Medium | **Mitigated** | `PreviewTenantSignInTests` answers 401 when a session made on the preview host is used on the main host. |
| P55 | The preview accepts a service account or machine token. | Preview tenant | Elevation of privilege | Medium | **Mitigated** | The preview has no service or endpoint audience, so it accepts no such token. See [preview tenants](../architecture.md#preview-tenants). |
| P56 | An administrator of the main tenant manages users on the preview. | Preview tenant | Elevation of privilege | Medium | **Mitigated** | `StudioPreviewTests` says that the users route is off on the preview for an administrator. |
| P57 | A module changes under a live release. | Modules and templates | Tampering | Medium | **Mitigated** | `ModulePinningTests` shows that a release holds its own module files. The compiler copies them into the release under its content hash. See [modules](modules-and-templates.md#modules). |
| P58 | A module brings in roles or policies. | Modules and templates | Elevation of privilege | Medium | **Mitigated** | `ModuleCompilerTests` reports a role file in a module as an error. A module policy names only role slots of its module, which the application maps, and grants only on its own module. See [modules](modules-and-templates.md#processes-pages-policies-and-dependencies). |
| P59 | An untrusted module or template is installed. | Modules and templates | Tampering | High | **Delegated** | The operator chooses what sits in `Modules:Path` and `Templates:Path`. See [modules](modules-and-templates.md#modules). |
| P60 | A template brings a wide policy into a new application. | Modules and templates | Elevation of privilege | Medium | **Mitigated** | `StudioCreateApplicationTests` copies a template as a new application. `StudioPublishTests` shows that an author without the publisher role cannot publish it. |

### Identity provider

Axis's identity provider is Keycloak, with one realm for each tenant. See [identity provider](identity-provider.md)
and [user administration](user-administration.md), and the decisions [D99](../decisions/d99-axis-ships-its-own-identity-provider.md)
and [D100](../decisions/d100-identity-providers-per-application-and-sign-in-per-site.md). It has six boundaries, each
with its own diagram below.

**Browser and sign-in page**

```mermaid
flowchart LR
    subgraph browser [Browser]
        Person[Person with the SPA]
    end
    subgraph realmpage [Realm sign-in page]
        Form[Account form, code and Continue with a provider]
    end
    Person -->|password, code or provider choice| Form
```

**Realm and brokered provider**

```mermaid
flowchart LR
    subgraph realm [Realm]
        Broker[Realm brokers the sign-in]
    end
    subgraph provider [Brokered OIDC or SAML provider]
        Upstream[Provider sign-in with its own MFA]
    end
    Broker -->|authorization request| Upstream
    Upstream -->|token or SAML response with acr and claims| Broker
```

**Server and realm**

```mermaid
flowchart LR
    subgraph server [Server]
        Callback[Sign-in callback]
        Activation[Activation step]
        UsersPage[Users page]
    end
    subgraph realmclients [Realm of the tenant]
        SiteClient[Site client and platform client, ID token with provider and acr]
        ActivationClient[Activation client, providers and clients]
        UsersClient[Users client, users only]
    end
    Callback -->|code exchange| SiteClient
    Activation -->|writes providers and clients| ActivationClient
    UsersPage -->|manages users| UsersClient
```

**Site and its providers**

```mermaid
flowchart LR
    subgraph server [Server]
        Check[Callback check]
    end
    subgraph release [Active release]
        Allowed[Providers the site allows]
    end
    subgraph session [Session]
        Alias[Session holds the provider alias]
    end
    Check -->|checks the provider and acr against| Allowed
    Check -->|writes only for an allowed provider| Alias
```

**Preview tenant and main realm**

```mermaid
flowchart LR
    subgraph previewtenant [Preview tenant]
        PreviewHost[Preview host]
        PreviewSession[Preview session]
    end
    subgraph maintenant [Main tenant]
        MainRealm[Main tenant realm and platform client]
        MainRoles[Roles given on the main tenant]
        MainHost[Main host]
    end
    PreviewHost -->|signs in through| MainRealm
    MainRealm -->|state returns to the preview host| PreviewSession
    MainRoles -->|apply to| PreviewSession
    PreviewSession -->|refused on| MainHost
```

**Administrator and users page**

```mermaid
flowchart LR
    subgraph browser [Browser]
        Admin[Administrator with session, CSRF header and recent sign-in]
    end
    subgraph server [Server]
        Routes[Users routes]
    end
    subgraph realmusers [Realm]
        UsersRealm[Users client, users only]
    end
    subgraph tenantdb [Tenant database]
        Audit[(Audit records)]
    end
    Admin -->|invites, disables and gives roles| Routes
    Routes -->|writes users| UsersRealm
    Routes -->|one audit record per change| Audit
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P74 | A password is guessed or reused. | Browser and sign-in page | Spoofing | High | **Delegated** | The identity provider: at least 12 and at most 128 characters, a list of breached passwords and a wait after repeated failures, see [accounts](user-administration.md#accounts). `TenantRealmAccountRulesTests` refuses a short or breached password in the realm. |
| P75 | A stolen password signs in without a second step. | Browser and sign-in page | Spoofing | High | **Delegated** | The identity provider: a code from an authenticator app at every account sign-in, see [accounts](user-administration.md#accounts). |
| P76 | An account is made without an invitation. | Browser and sign-in page | Elevation of privilege | Medium | **Delegated** | The identity provider: the realm allows no self-registration, see [accounts](user-administration.md#accounts). |
| P77 | The sign-in page shows whether an account exists or is locked. | Browser and sign-in page | Information disclosure | Low | **Delegated** | The identity provider: the sign-in page shows the same message in each case, see [accounts](user-administration.md#accounts). |
| P78 | A provider signs a person in at an `acr` below its list. | Realm and brokered provider | Spoofing | High | **Mitigated** | `SiteSignInTests` (a site callback whose upstream `acr` the provider does not accept is refused), `SignInRefusalsTests` (an `acr` the list does not accept is refused as `acrNotAccepted`). The realm does not forward `acr_values`, so the callback check is the control. |
| P79 | One person is split into two users, because a provider appears twice with other bindings or claims. | Realm and brokered provider | Spoofing | Medium | **Mitigated** | `RealmPlannerTests` (a provider that shares an issuer binding with another client is a conflict, and so is one with the same bindings and other claims), `AdminActivateRealmTests` (a conflicting second application fails activate with `AXC0291` and leaves the realm unchanged). |
| P80 | Two people are merged into one user by a shared email. | Realm and brokered provider | Spoofing | High | **Mitigated** | `RealmBrokeredUsersTests` (one person signed in through two providers with the same email becomes two users), `TenantRealmAccountRulesTests` (a second run keeps a first-login flow that only creates a user, and the realm allows duplicate emails). |
| P81 | A person who signs in through a brokered provider skips its second step. | Realm and brokered provider | Spoofing | Medium | **Delegated** | The brokered provider's MFA policy. [D99](../decisions/d99-axis-ships-its-own-identity-provider.md) says a person who signs in through their organization follows that organization's MFA policy. |
| P82 | A brokered OIDC token is forged or changed. | Realm and brokered provider | Tampering | High | **Delegated** | The identity provider checks the token signature with the provider's published keys, because the realm sync turns on signature validation for OIDC providers. See [identity provider](identity-provider.md). |
| P83 | An ID token for one site's client is used on another site. | Server and realm | Spoofing | High | **Mitigated** | `SiteSignInTests` (an ID token for another site's client is refused and writes no session). |
| P84 | A users client changes providers or clients, or the activation client changes users. | Server and realm | Elevation of privilege | High | **Mitigated** | `TenantRealmTests` (the users client manages users only, and the activation client manages clients and not users). |
| P85 | A provider that no release names still signs people in. | Server and realm | Tampering | Medium | **Mitigated** | `AdminActivateRealmTests` (activating again without the provider removes it from the realm), `RealmSyncTests` (a second sync removes a site that is gone and its provider, and keeps the other clients). |
| P86 | Realm sync output shows the activation secret. | Server and realm | Information disclosure | Medium | **Mitigated** | `AdminActivateRealmTests` (a wrong activation secret fails with a line that names no secret). |
| P87 | A roles claim gives a role in an application that does not map it. | Server and realm | Elevation of privilege | High | **Mitigated** | `ApplicationRolesSignInTests` (a brokered person holds the mapped role in the application that maps it only), `SiteSignInTests` (a roles value the mapping does not list gives no role). |
| P88 | A sign-in goes through a provider the site does not allow, even when `kc_idp_hint` forces it. | Site and its providers | Spoofing | High | **Mitigated** | `SiteSignInTests` (a callback through a provider the site does not allow is refused, for OIDC and for SAML, even when `kc_idp_hint` forces it), `SignInRefusalsTests` (a provider the site does not allow is refused as `providerNotAllowed`). |
| P89 | A session through one provider opens a site that does not allow that provider. | Site and its providers | Elevation of privilege | Medium | **Mitigated** | `SiteSignInTests` (a session opens a second site that allows its provider, and a site that does not allow it asks to sign in again). |
| P90 | A provider that the platform sign-in does not accept opens Studio, the console or the users page. | Site and its providers | Elevation of privilege | High | **Mitigated** | `PlatformSignInTests` (a sign-in through a provider the platform does not accept is refused and writes no session), `PlatformSignInSitesTests` (a person signed in through a site's provider sees no tool). |
| P91 | A refused sign-in leaves no record. | Site and its providers | Repudiation | Low | **Mitigated** | `SiteSignInTests` (a refusal is recorded with the reason `providerNotAllowed`), `SignInTests` (a callback with another tenant's state is refused and recorded). |
| P92 | A preview tenant's sign-in, made through its main tenant's realm and client, is used on the main tenant. | Preview tenant and main realm | Spoofing | Medium | **Mitigated** | `SignInTests` (a callback with another tenant's state is refused and recorded), `PreviewTenantSignInTests` (a preview session gets 401 on the main host, the cookie case in P54). |
| P93 | A role removed on the main tenant keeps working on the preview. | Preview tenant and main realm | Elevation of privilege | Medium | **Mitigated** | `PreviewTenantPlatformSignInTests` (a role the main tenant's users page gives applies on the preview until it is removed, and a person without it is denied). |
| P94 | A stolen or unattended administrator session gives its own user a platform role, such as publisher. | Administrator and users page | Elevation of privilege | High | **Mitigated** | `UserCommandTests` (a platform role change with an old login answers 403 and changes and audits nothing), `UserInvitationTests` (a platform role at invite time needs a recent login). |
| P95 | An administrator gives themselves a role and denies it. | Administrator and users page | Repudiation | Medium | **Mitigated** | `UserCommandTests` (giving and removing a role each write one audit record), `UserInvitationTests` (an invitation with a role writes one record for the invitation and one for the role). |
| P96 | A non-administrator lists, invites or changes users. | Administrator and users page | Elevation of privilege | High | **Mitigated** | `UserListTests` (a person without the administrator role gets 403 and nobody signed in gets 401), `UserCommandTests` (a person without the administrator role gets 403 on the user routes), `UserInvitationTests` (an invitation without an administrator session is refused). |
| P97 | An invitation takes over an account, by a used email or by an old link after a resend. | Administrator and users page | Spoofing | Medium | **Mitigated** | `UserInvitationTests` (an invitation for a used email is refused, and a resent invitation ends the old link). |
| P98 | A disabled person keeps a session or signs in. | Administrator and users page | Elevation of privilege | High | **Mitigated** | `UserCommandTests` (disabling a person ends their sessions and refuses their sign-in). |
| P99 | An administrator locks the tenant out by disabling themselves or removing their own administrator role. | Administrator and users page | Denial of service | Medium | **Mitigated** | `UserCommandTests` (disabling oneself, and removing one's own administrator role, answer 409 and change nothing). |

An identity provider session's lifetime and CSRF are covered by P1, P2 and P8.

Open, waiting for an issue:

- (Spoofing, High) no test shows that the realm checks the signature of a SAML provider's response. The sync keeps
  what Keycloak's metadata import sets, which may leave the check off.
- (Elevation of privilege, Medium) the `/api/apps/{app}` data endpoints do not check the session's provider, see
  [sites and sign-in](identity-provider.md#sites-and-sign-in).

Each becomes a **Gap** row, linked to its issue, once that issue exists.

### Personal data

Axis holds personal data about the people who use it: user profiles with email addresses, records and
their history, audit records with client addresses, notifications, logs and traces, uploaded files,
exports and backups. LINDDUN finds the privacy threats to that data, see
[D106](../decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md).

```mermaid
flowchart LR
    subgraph idp [Identity provider]
        Claims[Identity provider claims]
    end
    subgraph server [Server]
        Api[Server API]
    end
    subgraph worker [Worker]
        Steps[Process steps and notify]
    end
    subgraph tenantdb [Tenant database]
        Profiles[(User profiles)]
        Records[(Records, record history and audit records with client addresses)]
        Files[(Uploaded files)]
        Outbox[(Notifications and email outbox)]
    end
    subgraph outside [Outside systems]
        Smtp[SMTP server]
        Collector[Collector run by the operator]
    end
    subgraph browser [Browser]
        Download[Export file]
    end
    subgraph operator [Operator]
        Admin[Admin tool tenant backup]
        Archive[(Archive kept by the operator)]
    end
    Claims -->|name, email and locale at sign-in| Profiles
    Api -->|writes| Records
    Api -->|stores uploads| Files
    Api -->|builds| Download
    Steps -->|writes| Outbox
    Outbox -->|sent over SMTP| Smtp
    Api -->|logs and traces| Collector
    Steps -->|logs and traces| Collector
    Admin -->|tenant backup| Archive
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| P61 | A trace holds a person's user id, a field value or a bound address. | Personal data | Linking | Medium | **Mitigated** | `TraceCorrelationTests` (on the purchase request journey, no span holds a field value, a secret, a user id or a bound address). |
| P62 | A record's history shows a person's actions in another tenant. | Personal data | Linking | Low | **Mitigated** | `RecordHistoryTests` (a record's history holds only its own tenant's audit records). |
| P63 | The reassign picker shows other users' names to someone without the grant. | Personal data | Identifying | Medium | **Mitigated** | `TaskReassignTests` (the picker is 403 for anyone without the grant, and lists at most 20 profiles). |
| P64 | A user learns that a record or file exists from the answer code. | Personal data | Detecting | Medium | **Mitigated** | `FileDownloadTests` (no read grant, or a read filter that excludes the record, gets 404), `RecordHistoryTests` (a deleted record's history is 404). |
| P65 | Other users see whether a person has read a notification. | Personal data | Detecting | Low | **Mitigated** | `NotificationEndpointTests` (marking notifications read lowers the unread count only for the caller). |
| P66 | A user reads personal fields without a grant. | Personal data | Data disclosure | High | **Mitigated** | `RecordFieldPolicyTests` (reads and writes hold only the fields the user may read, and a write to any other field is a 403). |
| P67 | An export holds rows or columns that the user may not read. | Personal data | Data disclosure | High | **Mitigated** | `ExportEndpointTests` (hidden rows and columns are absent, a user without the export grant gets 403 and nothing is audited, and each export is audited). |
| P68 | A profile's email reaches a log. | Personal data | Data disclosure | Medium | **Mitigated** | `SignInTests` (a profile that cannot be written logs a warning without the email). |
| P69 | A sensitive input shows in process inspection. | Personal data | Data disclosure | Medium | **Mitigated** | `ProcessInstanceDetailTests` (the input that maps a sensitive field is hidden from a user who may read the field). |
| P70 | People do not know what Axis keeps about them. | Personal data | Unawareness | Medium | **Delegated** | The operator. Axis names no regulation, so the operator tells people what the application keeps, see [Restricted](keys-and-data-protection.md#restricted). |
| P71 | A person cannot correct their profile. | Personal data | Unawareness | Low | **Delegated** | The identity provider. Each sign-in rewrites the profile from its claims, see [user profiles](notifications.md#user-profiles). |
| P72 | Notifications are kept longer than needed. | Personal data | Non-compliance | Medium | **Mitigated** | `NotificationRetentionTests` (notifications over the retention days are deleted with their reads, and newer ones stay). |
| P73 | Logs, telemetry and backup archives are kept longer than needed. | Personal data | Non-compliance | Medium | **Delegated** | The operator keeps and removes them, see [log inventory](log-inventory.md#logs) and [Restricted](keys-and-data-protection.md#restricted). |
| P101 | Session audit records keep the full client address forever. | Personal data | Identifying | Medium | **Mitigated** | `ClientAddressTests` (IPv4 loses its last octet and IPv6 its last 80 bits) and `SignInTests` (sign-in, sign-out and refused sign-in records hold the shortened address). |

Non-repudiation does not apply as a privacy threat. The audit records exist so that a person cannot deny
a business action, see P3, P12 and P20. What they keep about a person is in the list below.

Open, waiting for an issue:

- (Identifying, Medium) audit records written before M27 keep the actor's display name, `actor_name`, forever.
  New rows store no name, [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md).
- (Data disclosure, High) a database login, a dump or a replica reads personal data and `sensitive` fields
  in clear, _(planned for M27)_, [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md).
  Until then the operator's control is I15.
- (Non-compliance, Medium) `axis.email_outbox` rows keep the address and the body forever.
- (Non-compliance, Medium) `axis.user_profiles` rows and old audit records cannot be removed for a person's
  removal request.

Each becomes a **Gap** row, linked to its issue, once that issue exists.

## The installation

The operator installs Axis from its images, with Docker Compose, the Helm chart or on premises. The
overview shows the parts of an install. Each part has its own table below.

```mermaid
flowchart LR
    subgraph operator [Operator]
        Person[Operator]
    end
    subgraph registry [Registry]
        Images[Images axis-server, axis-worker and axis-admin]
    end
    subgraph hosts [Hosts]
        Proxy[TLS proxy]
        Servers[Server replicas]
        Worker[Worker and publisher]
    end
    subgraph secrets [Secrets]
        Files[Secret files or Kubernetes Secrets]
    end
    subgraph data [Databases and backups]
        Db[(Platform and tenant databases)]
        Backups[(Backups)]
    end
    Person -->|builds or pulls| Images
    Person -->|writes| Files
    Person -->|stores| Backups
    Images -->|starts| Servers
    Images -->|starts| Worker
    Files -->|read at start| Servers
    Files -->|read at start| Worker
    Proxy -->|HTTPS| Servers
    Servers -->|runtime role| Db
    Worker -->|runtime role| Db
    Db -->|pg_dump and tenant backup| Backups
```

### Operator and installation

The operator writes the secrets and the settings, then starts the hosts. Each row is a threat to that work.

```mermaid
flowchart LR
    subgraph operator [Operator]
        Person[Operator with host or cluster access]
    end
    subgraph install [Installation]
        Files[Secret files or Kubernetes Secrets]
        Settings[Compose file or Helm values]
    end
    Person -->|writes| Files
    Person -->|edits| Settings
    Settings -->|names the secrets only| Files
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| I1 | A secret ends up in a Compose or values file, or in an environment variable. | Operator and installation | Information disclosure | High | **Mitigated** | `scripts/onprem.test.mjs` (no file under deploy/compose holds a password, a client secret or a key), `scripts/helm.test.mjs` (no env entry holds a secret setting, and no Secret object is rendered). |
| I2 | Someone with host or cluster access reads the secret files or the TLS private key. | Operator and installation | Information disclosure | High | **Delegated** | The operator controls who can read the secret files and the TLS key, see [On premises](installation.md#on-premises) and [In the cloud](installation.md#in-the-cloud). |
| I3 | An admin command changes a tenant or the platform and leaves no record. | Operator and installation | Repudiation | Medium | **Mitigated** | `AdminPlatformAuditTests` (each of the seven admin commands writes one record with its tenant, user and machine and no secret), `PlatformRekeyTests` (a successful rekey writes one record with no tenant). |
| I4 | A host gets more database rights than its job needs, such as a worker with the schema role. | Operator and installation | Elevation of privilege | High | **Mitigated** | `DatabaseRoleTests` (every server and worker session on a tenant database uses the runtime role), `scripts/compose.test.mjs` (the worker gets only the runtime role), `scripts/helm.test.mjs` (the worker gets no platform schema connection). |

Spoofing, tampering and denial of service do not apply to the operator's own steps. The operator already has host access, see I2.

### Images and registry

The images travel from a registry to each host, where Compose or the Helm chart starts them.

```mermaid
flowchart LR
    subgraph registry [Registry]
        Images[axis-server, axis-worker and axis-admin images]
    end
    subgraph host [Host or cluster]
        Pull[Compose or Helm pulls the image]
        Running[Running container with a read-only root file system]
    end
    Images -->|pulled by tag| Pull
    Pull -->|starts| Running
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| I5 | An image is swapped or changed between the registry and the host. | Images and registry | Tampering | High | **Delegated** | The operator builds the images or pulls them from a registry the operator controls, see [Images](installation.md#images). Signed images are planned for M32, see [D107](../decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md). |
| I6 | An image ships samples, development settings or source control metadata. | Images and registry | Information disclosure | High | **Mitigated** | `scripts/images.test.mjs` (no image holds a sample, a Development settings file or `.git`). |
| I7 | A process in a container writes to its image or runs as root. | Images and registry | Elevation of privilege | High | **Mitigated** | `scripts/images.test.mjs` (each image runs as a user other than root, and the server and worker start read-only with their settings in `/run/secrets`). |
| I8 | An install keeps running a version with a known vulnerability. | Images and registry | Elevation of privilege | Medium | **Delegated** | The operator upgrades to the latest version, the only one that gets security fixes, see [Upgrade](installation.md#upgrade). |

Spoofing and repudiation do not apply to an image, which is a file that signs no one in and writes no record. Denial of service is covered by the server rows, such as P6.

### Proxy and server replicas

Clients reach the server replicas through a TLS proxy. The proxy checks each replica's certificate before it forwards a request.

```mermaid
flowchart LR
    subgraph clients [Clients]
        Browser[Browser or caller]
    end
    subgraph edge [TLS proxy]
        Proxy[TLS proxy in front of the replicas]
    end
    subgraph replicas [Server replicas]
        Servers[Server replicas on HTTPS only]
    end
    Browser -->|HTTPS| Proxy
    Proxy -->|HTTPS with the server certificate checked| Servers
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| I9 | A client fakes its address, scheme or host through forwarded headers. | Proxy and server replicas | Spoofing | High | **Mitigated** | `ForwardedHeadersTests` (an address that is not listed cannot fake its host or its scheme), `scripts/helm.test.mjs` (forwarded header settings render on the server only). |
| I10 | Traffic between a client, the proxy and a server is read or changed. | Proxy and server replicas | Information disclosure | High | **Mitigated** | `scripts/onprem.test.mjs` (the servers answer HTTPS only, the proxy takes only TLS 1.2 and 1.3, refuses CBC ciphers on TLS 1.2, and trusts only the server certificate authority). |
| I11 | A TLS endpoint that the operator runs in front of the server accepts weak TLS or skips the server certificate check. | Proxy and server replicas | Information disclosure | High | **Delegated** | The operator uses only the TLS versions and ciphers that [TLS](installation.md#tls) lists, and checks the server certificate as the Compose proxy does. |
| I12 | A replica restarts and drops sessions, or a replica with another key joins the install. | Proxy and server replicas | Denial of service | Medium | **Mitigated** | `SharedKeyRingTests` (a session survives a restart and another instance accepts it, and a server with another key fails to start), `scripts/onprem.test.mjs` (a restarted replica serves a signed-in user again). |
| I13 | A save through one replica overwrites a newer save made through another. | Proxy and server replicas | Tampering | Medium | **Mitigated** | `scripts/onprem.test.mjs` (a stale save through one replica answers 409 after a save through another). |

Repudiation does not apply here, because the server writes the audit records, see P3. Elevation of privilege does not apply to the proxy, which only forwards requests. The access checks are in the server, see P7.

### Database and backups

The server and the worker reach tenant databases with the runtime role. Backups hold copies of the databases.

```mermaid
flowchart LR
    subgraph app [Server and worker]
        Runtime[Server and worker]
    end
    subgraph database [Database host]
        Db[(Platform and tenant databases)]
    end
    subgraph backupstore [Backup storage]
        Archive[(Dumps and tenant archives)]
    end
    Runtime -->|runtime role over verify-full TLS| Db
    Db -->|pg_dump and tenant backup| Archive
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| I14 | A backup archive or its output holds a tenant secret in clear. | Database and backups | Information disclosure | High | **Mitigated** | `AdminTenantBackupTests` (the archive and the output of a backup hold no password, client secret or binding secret of the tenant) and `AdminEncryptedBackupTests` (the archive bytes hold no tenant key, in base64 or raw, and no backup key). |
| I15 | A stolen tenant archive exposes business data. | Database and backups | Information disclosure | High | **Mitigated** | `AdminEncryptedBackupTests` (the archive is encrypted with the backup key, holds neither the dump's bytes nor the tenant id, a restore refuses an archive that is not encrypted, a wrong key or a cut archive, and a changed chunk fails the restore). The operator keeps the backup key outside the installation, see [What else to back up](administration.md#what-else-to-back-up), and stores a dump that the operator made with its own tools under access control, see [Restricted](keys-and-data-protection.md#restricted). |
| I16 | A backup is stored with the installation key, so a stolen backup yields the stored secrets. | Database and backups | Information disclosure | High | **Delegated** | The operator keeps the installation key apart from the backups, see [What else to back up](administration.md#what-else-to-back-up). |
| I17 | A failed upgrade or a lost database cannot be restored. | Database and backups | Denial of service | Medium | **Delegated** | The operator backs up the platform and tenant databases before each upgrade, see [Upgrade](installation.md#upgrade). |
| I18 | A host connects to PostgreSQL without a full certificate check. | Database and backups | Tampering | High | **Mitigated** | `DatabaseTlsStartTests` (a production server stops at start on a connection string without full certificate checks), `AdminDatabaseTlsTests` (an admin command exits with status 2 for such a string). |

Spoofing, repudiation and elevation of privilege on the database are covered by the server rows, P9, P12 and P13.

### Stored secrets and key ring

The installation key encrypts every stored secret and each data protection key. The data protection keys sit in the platform database.

```mermaid
flowchart LR
    subgraph hosts [Server and worker hosts]
        Files[Installation key in secret files or Kubernetes Secrets]
    end
    subgraph platform [Platform database]
        Ring[(Data protection key ring)]
        Secrets[(Encrypted tenant secrets)]
    end
    Files -->|encrypts| Ring
    Files -->|encrypts| Secrets
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| I19 | Data protection keys are read in clear from the platform database. | Stored secrets and key ring | Information disclosure | High | **Mitigated** | `SharedKeyRingTests` (the stored keys hold no plain key material and decrypt only with the installation key). |
| I20 | A rekey leaves the old installation key able to decrypt stored values. | Stored secrets and key ring | Information disclosure | High | **Mitigated** | `PlatformRekeyTests` (after a rekey, the new key alone decrypts every encrypted column). |
| I21 | The previous installation key stays on the hosts after a rekey. | Stored secrets and key ring | Information disclosure | Medium | **Delegated** | The operator removes `Platform:PreviousSecretKey` from every host once the rekey has run, see [Installation key](tenant-setting-commands.md#installation-key). |
| I22 | A leaked installation key stays in use until the operator rotates it. | Stored secrets and key ring | Information disclosure | High | **Delegated** | The operator runs `platform rekey` and changes the secrets at once when a key may have leaked, see [Key policy](keys-and-data-protection.md#key-policy). |

Other categories do not apply to stored keys. A changed key ring fails to start, see I12, and a rekey writes a record, see I3.

## The way Axis is built

NexKit, CI, the AI agents that write to the repository, and the repository itself.

```mermaid
flowchart LR
    subgraph nexkit [NexKit]
        Runner[NexKit runs the work]
    end
    subgraph agents [AI agents]
        Writer[Agents that write changes]
    end
    subgraph repository [Repository]
        Repo[Git repository on GitHub]
    end
    subgraph ci [CI]
        Checks[Checks and workflows]
    end
    Runner -->|runs| Writer
    Writer -->|commits| Repo
    Runner -->|pull requests| Repo
    Repo -->|triggers| Checks
```

### NexKit and repository

NexKit turns an issue into a pull request, and the rules on `main` decide when it merges.

```mermaid
flowchart LR
    subgraph issues [Issues and comments]
        Text[Issue text, comments and repository files]
    end
    subgraph pipeline [NexKit pipeline]
        Run[NexKit workflow pinned to tag v5.9.0]
    end
    subgraph review [Pull request]
        Branch[Branch with a nexkit prefix and its pull request]
    end
    subgraph trunk [Main branch]
        Rules[Rules on main require NexKit checks]
        Main[main]
    end
    Text -->|read by| Run
    Run -->|pushes| Branch
    Branch -->|merges once checks pass| Main
    Rules -->|guard| Main
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| B4 | A dependency, or a new version of one, brings in malicious code. | NexKit and repository | Tampering | High | **Mitigated** | [Dependency updates](../development.md#dependency-updates) pin the NuGet versions and the npm lock files, and take packages only from nuget.org and the npm registry. The CI, CodeQL and Performance workflows pin their GitHub Actions to commit hashes. NexKit's workflow pins its pipeline to the tag `v5.9.0`, not to a hash. `scripts/scan.sh` does not run in CI until the hand-made change of [D107](../decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md). |
| B8 | A change merges with no record of its issue, plan or review. | NexKit and repository | Repudiation | Low | **Mitigated** | Each change follows [the life of an issue](../delivery.md#the-life-of-an-issue), from plan through implementation and review to merge. |
| B9 | Runaway agent runs use up the runners or the usage limit. | NexKit and repository | Denial of service | Low | **Mitigated** | At most three issues run at once, with `max_parallel` at 3, and a second Claude subscription takes over at the usage limit, see [delivery decisions](../delivery.md#decisions). |

Spoofing does not apply to the pipeline's own steps, which act through the Actions token. The secrets the pipeline reads are rated in B6.

### CI and secrets

The three check workflows run with read access and no secret. NexKit's own jobs hold the write access and the secrets.

```mermaid
flowchart LR
    subgraph pushes [Pushes]
        Push[Push to main or a hand-made branch]
    end
    subgraph checks [Check workflows]
        Ci[CI, CodeQL and Performance with read access and no secret]
    end
    subgraph nexkitjobs [NexKit jobs]
        Jobs[NexKit jobs with write access]
        Secrets[Secrets that the config names for each job]
    end
    Push -->|runs| Ci
    Ci -->|a finished run starts a sweep| Jobs
    Jobs -->|reads only| Secrets
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| B3 | A workflow token pushes to `main` without the checks. | CI and secrets | Elevation of privilege | High | **Mitigated** | CI, CodeQL and Performance run with `contents: read`, and the [rules on main](../delivery.md#decisions) require NexKit's checks. NexKit's Actions token cannot push a change to `.github/workflows/`, because there is no push token. |
| B6 | A secret, such as an agent's subscription token, leaks into a log or reaches a job that does not need it. | CI and secrets | Information disclosure | High | **Mitigated** | The CI, CodeQL and Performance workflows use no secret. `.github/workflows/nexkit.yml` says the NexKit pipeline passes each job only the secrets that `.nexkit/config.json` names for it. |

Spoofing does not apply to a workflow run, which signs in no one and acts through its own token. Denial of service on the runners is rated in B9.

### Agents

The agents read the issue and the repository and write commits. They cannot change the protected paths.

```mermaid
flowchart LR
    subgraph sources [Issues, comments and files]
        Text[Issue text, comments and repository files]
    end
    subgraph agents [Agents]
        Agent[Plan, implement, fix and review agents]
    end
    subgraph limits [Limits]
        Checks[NexKit checks and the AI review]
        Protected[Protected paths for workflows and NexKit settings]
    end
    Text -->|read by| Agent
    Agent -->|commits and runs checks| Checks
    Agent -.->|cannot change| Protected
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| B1 | An agent adds an exception to a security check, or loosens one, to turn a red check green, such as in `scripts/scan-accepted.json` or `scripts/check-docs.mjs`. | Agents | Tampering | High | **Mitigated** | The owner guards the security checks with `.github/CODEOWNERS`, as [D111](../decisions/d111-authentication-by-default-and-the-owner-guards-the-security-checks.md) agrees: a pull request that changes one of those files waits for the owner's review, see [delivery decisions](../delivery.md#decisions). |
| B2 | Text in an issue, a comment or a repository file steers an agent. This is prompt injection. | Agents | Tampering | High | **Mitigated** | The [delivery decisions](../delivery.md#decisions) limit what an agent can change: protected paths, no push token, and NexKit's checks and review on every pull request. These limit the damage. They do not stop the steering. |
| B5 | The AI review shares the writer's blind spots, because the same kind of model writes and reviews the code. | Agents | Tampering | Medium | **Accepted** | Accepted on 2026-10-29. The owner keeps NexKit's AI review as the only gate before a merge, with the checks and code owners of [D111](../decisions/d111-authentication-by-default-and-the-owner-guards-the-security-checks.md) around it. |
| B7 | An agent turns off or narrows a CI job or a CodeQL check. | Agents | Tampering | High | **Mitigated** | `.github/` and `.nexkit/` are protected paths, see [delivery decisions](../delivery.md#decisions). A change to them is a hand-made pull request, opened by the hourly driver or the local agent session. The owner reviews and merges each one. |

Spoofing does not apply to an agent, which acts through NexKit's own token. Denial of service on the runners is rated in B9.
