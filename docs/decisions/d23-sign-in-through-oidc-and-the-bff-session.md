# D23. Sign-in through OIDC and the BFF session

Status: **Agreed**

M4 replaces the test users with real sign-in through each tenant's identity
provider. The flow, the identity provider per tenant, the endpoints, the
session, its tenant binding, the roles, the current user, sign-in needed,
CSRF, Keycloak and the audit actor are in place, and the test users are
removed. Everything in this decision is in place. See
[architecture](../reference/security.md#sign-in-and-the-session) for the details.

- **Flow:** OIDC authorization code flow with PKCE. The server is the
  confidential client (D9).
- **Identity provider per tenant:** each tenant in the `Tenants` section has
  an `Oidc` block with an authority, a client id and a client secret.
  - [D99](d99-axis-ships-its-own-identity-provider.md) changes where it points: at the tenant's realm in Axis's own
    identity provider.
  - It names the claims that give the user id (default `sub`), the display
    name (default `name`) and the roles (default `roles`).
  - Startup fails, naming each problem, when a tenant has no `Oidc` block or
    an incomplete one.
  - The client secret comes from the environment, never from a committed
    file outside development fixtures.
  - Under `Tenancy:Source=directory`, the `Oidc` settings and the encrypted
    client secret come from the tenant directory, see
    [D42](d42-tenant-directory-in-the-platform-database.md).
- **Endpoints:** `GET /api/auth/sign-in?returnUrl=` redirects to the
  identity provider. A `returnUrl` that is not a local path is a `400`. The
  callback creates the session and redirects to `returnUrl`.
  `POST /api/auth/sign-out` ends the session and signs out at the identity
  provider.
- **Session:** one HttpOnly, Secure, SameSite=Lax cookie.
  - It holds the user id, the display name, the roles and the tenant id.
    - [D84](d84-asvs-level-2-and-a-server-side-session.md) replaces this
      line: the session moves to the server and the cookie holds only its id.
  - It holds no tokens, and no token reaches the browser.
  - It ends after 8 hours, or after 1 hour without requests.
  - The cookie is protected with a key ring shared by every replica, and
    behind a named proxy the server uses the forwarded scheme and host. See
    [D49](d49-several-server-replicas-behind-a-tls-proxy.md).
- **Bound to its tenant:** a session used on another tenant's host is no
  session.
- **Roles:** read from the roles claim at sign-in. A role change applies at
  the next sign-in.
- **Current user:** `GET /api/me` keeps its shape and reads the session.
  [D38](d38-studio-edits-application-folders-through-an-authoring-api.md)
  adds a `studio` field.
- **Sign-in needed:** every endpoint under `/api/apps/`, and the site and
  page metadata, answer `401` without a session. The platform shell's texts
  and `/health` stay open.
- **CSRF:** every `POST`, `PUT`, `PATCH` and `DELETE` under `/api` must carry
  the header `X-Axis-Csrf: 1`, otherwise `403`. This adds to the JSON content
  type rule and the SameSite cookie. There is no antiforgery token.
- **Test users go:** `TestUsers`, their endpoints and the SPA picker are
  removed. Integration tests sign in through a test authentication scheme
  that only test hosts register. This replaces the test-user part of D21.
- **Keycloak:** Docker Compose, `scripts/dev.sh` and the E2E run start
  Keycloak with an imported development realm.
  - The realm holds the purchase request users, two employees, two department
    managers and a finance reviewer, with a development-only password.
  - It maps their roles to the `roles` claim.
  - It fixes each user's id, so seed data such as a department's manager can
    name them.
  - The realm and its client secret are committed as development fixtures.
    They are never valid outside Development and tests.
  - Integration tests that cover sign-in start the same realm through
    Testcontainers.
- **Audit actor:** the actor is the user id from the session. The display
  name is stored with the audit record, because Axis has no user directory
  to look it up later.
  - [D73](d73-in-app-and-email-notifications.md) changes this reason: from
    M14 each sign-in writes a user profile, with the email and locale that
    notifications need. It is still not a directory, and audit records keep
    the display name.

_Why:_ see [knowledge](../domain/knowledge.md#authorization). Policies and
processes need to know who acts, so test users must give way to real
identities. Keeping tokens on the server and the session bound to one tenant
means a stolen or reused cookie cannot act in another tenant. `sub` never
changes, so it is the default user id, and a tenant can name another claim.
