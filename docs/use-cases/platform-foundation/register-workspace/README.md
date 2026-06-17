# Use case — Register a new workspace

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Register a workspace on the Axis platform with an official workspace contact email, verify that contact channel, and provision the workspace foundation before any user identity is attached.

## Primary actor

- Workspace representative

## Trigger

- A prospective customer starts onboarding a new workspace.

## Main flow

1. Actor opens the workspace registration page.
2. Actor enters workspace name, workspace contact email, and accepts Terms of Service / Privacy Policy.
3. System validates the workspace details, reserves a unique workspace slug, and sends a verification email to the workspace contact email.
4. Actor verifies the workspace email link.
5. System marks the workspace verified, starts workspace provisioning, and issues a first-user setup link for the `/register` handoff.
6. Actor continues to user registration to create the first owner/admin identity.

## Alternate / error flows

- Workspace email already exists for another workspace: show the same confirmation screen where possible; never disclose ownership details to anonymous callers.
- Verification link expired: show a resend option for the workspace contact email.
- Verification link already used: show a clear completed-state message and link to user registration or sign-in.
- Provisioning fails after automatic retries: show the workspace provisioning failure state with manual retry/support action.

## Context

This use case is about workspace onboarding, not user identity onboarding. The email collected here is an official workspace contact email, such as `admin@company.com` or `it@company.com`; it is not a personal user login. Microsoft / Google / GitHub identity providers belong to user identity sign-in and provider-linking flows, not to workspace registration.

Axis supports standalone user accounts. Registering a workspace is required only when a user wants to create or join a workspace workspace; it is not a prerequisite for normal user registration.

## Acceptance Criteria

*Happy path*
- [ ] Registration form collects workspace name and workspace contact email.
- [ ] The user must accept the Terms of Service and Privacy Policy before the workspace registration can be submitted; the accepted versions are recorded with the workspace registration record.
- [ ] A workspace slug is auto-generated from the workspace name, uniqueness-checked, and shown to the actor before submission.
- [ ] On successful submission, a verification email is sent to the workspace contact email and the actor sees a confirmation screen.
- [ ] Clicking the verification link verifies the workspace contact email and starts workspace provisioning.
- [ ] After workspace verification, the system creates a short-lived first-user setup token/link for the `/register` handoff; the workspace is not usable until a user account is created and attached.
- [ ] Once workspace provisioning completes, the first registered owner/admin can access the workspace.

*Validation & errors*
- [ ] Workspace name: required, 2–100 characters.
- [ ] Workspace contact email: required, valid email format, unique across active workspaces.
- [ ] Workspace contact email must not be collected from Microsoft / Google / GitHub OAuth claims in this use case.
- [ ] All field-level errors are shown inline, not as a global toast.
- [ ] Submitting with an already-registered workspace email shows the same confirmation screen when possible; anonymous callers must not learn whether a workspace already exists.
- [ ] If the API returns a server error (5xx), the form shows a generic "Something went wrong, please try again" message and the submit button re-enables.

*Edge cases*
- [ ] Multiple rapid submissions of the same workspace registration are deduplicated with an idempotency key.
- [ ] workspace name with special characters (e.g., `O'Brien & Co.`) is accepted and slugified consistently.
- [ ] A generic mailbox (e.g., `admin@company.com`) is allowed; the first-user setup link decides who becomes the initial owner/admin.
- [ ] Verification and first-user setup links are short-lived and single-use.

*Workspace provisioning*
- [ ] A dedicated PostgreSQL schema is created per module after workspace email verification.
- [ ] All base tables are migrated into each module's workspace schema automatically.
- [ ] Provisioning is idempotent: running it twice for the same workspace does not create duplicate schemas or tables.
- [ ] If a workspace schema already exists from a partial previous run, the migration runner continues from where it left off.
- [ ] The UI shows `workspace-provisioning` while workspace setup is running and redirects when the workspace becomes active.
- [ ] If provisioning fails after all retries, the UI shows a failed state with **Try again** and support contact.

*Out of scope*
- Standalone user registration without workspace context; see [register-user](../../identity-access/register-user/).
- User account provider linking and Microsoft / Google / GitHub login; see [sign-in](../../identity-access/sign-in/) and ADR-027.
- Enterprise SAML/SCIM federation and per-workspace IdP configuration.
- CAPTCHA / bot protection on the workspace registration form.
- Automatic re-send of verification email after X minutes.
- Custom schema naming chosen by the user; schema names are auto-generated.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⚠️ |
>
> **Gaps vs spec:** Backend/API now split workspace onboarding from standalone user registration.
> `POST /api/workspaces` records workspace facts + legal versions and sends workspace-contact verification; it only creates the pending workspace record.
> The workspace-email verification step starts provisioning and issues a short-lived setup token for the `/register` first-owner handoff. Remaining work is the dedicated register-workspace frontend copy/validation and polished setup-token handoff UI.
>
> **Deferred follow-ups:**
> - Dedicated register-workspace frontend copy/validation.
> - Polished first-owner setup-token handoff UI.
>
> **Decisions:**
> - `register-workspace` owns workspace facts only: workspace name, workspace contact email, legal acceptance, slug, verification, and workspace provisioning.
> - Microsoft / Google / GitHub authenticate users, not workspaces. Generic OAuth provider claims must not be used as proof that someone controls a workspace.
> - First owner/admin identity creation is a follow-up setup-token step after workspace verification.
> - Workspace onboarding is optional from the product perspective: a user can register and use Axis without creating or joining a workspace.
>
## Screen flow

Canonical order for this use case. Screens owned by another use case are linked inline.

| Step | Screen | When |
|------|--------|------|
| 1 | `register-workspace` | Enter workspace name, workspace contact email, slug preview, and legal acceptance |
| 2 | `email-confirmation` | After workspace registration submit; tells actor to verify the workspace email |
| 3 | First-user setup handoff | First owner/admin uses the setup token and creates a user identity after workspace verification |
| 4 | `workspace-provisioning` | After user email verification when workspace setup is still running |

**Error / reference screens** (not sequential steps):

| Screen | When |
|--------|------|
| `register-workspace-states` | Validation or 5xx on the workspace registration form |
| `email-confirmation-states` | Resend workspace verification email: success and rate limit |
| `verify-email-states` | workspace email verification link expired, already used, invalid, or rate-limited |

```mermaid
%%{init: {'theme':'dark','themeVariables':{'background':'#0d1117','mainBkg':'#0d1117','primaryColor':'#161b22','primaryBorderColor':'#388bfd','primaryTextColor':'#e6edf3','secondaryColor':'#21262d','secondaryBorderColor':'#388bfd','secondaryTextColor':'#e6edf3','tertiaryColor':'#161b22','tertiaryTextColor':'#e6edf3','lineColor':'#58a6ff','textColor':'#e6edf3','nodeBorder':'#388bfd','clusterBkg':'#161b22','clusterBorder':'#388bfd','titleColor':'#e6edf3','edgeLabelBackground':'#161b22','actorBkg':'#161b22','actorBorder':'#388bfd','actorTextColor':'#e6edf3','signalColor':'#58a6ff','labelBoxBkgColor':'#161b22','labelBoxBorderColor':'#388bfd','noteBkgColor':'#161b22','noteBorderColor':'#388bfd','noteTextColor':'#c9d1d9','activationBkgColor':'#30363d'}}}%%
flowchart TD
  entry["1 → register-workspace"]
  confirm["2 → email-confirmation"]
  user["3 → first-user setup"]
  provision["4 → workspace-provisioning"]
  errEntry["register-workspace-states"]
  errEmail["email-confirmation-states"]
  errVerify["verify-email-states"]

  entry --> confirm
  confirm -->|"verify workspace email; issue setup token"| user
  user -->|"verify user email; workspace still provisioning"| provision
  entry -.-> errEntry
  confirm -.-> errEmail
  confirm -.-> errVerify
```

## Wireframes

UI assets in this folder cover workspace-owned screens. Standalone user registration is owned by [register-user](../../identity-access/register-user/); external-provider/user-identity screens are not owned by this folder.

| # | Screen | Role | Excalidraw | Preview |
|---|--------|------|------------|---------|
| 1 | register-workspace | Happy path — workspace registration form | [source](./register-workspace.excalidraw) | [preview](./register-workspace.svg) |
| 2 | email-confirmation | Happy path — workspace email confirmation | [source](./email-confirmation.excalidraw) | [preview](./email-confirmation.svg) |
| 4 | workspace-provisioning | Workspace setup progress / failure reference | [source](./workspace-provisioning.excalidraw) | [preview](./workspace-provisioning.svg) |
| — | register-workspace-states | Validation / 5xx reference | [source](./register-workspace-states.excalidraw) | [preview](./register-workspace-states.svg) |
| — | email-confirmation-states | Resend workspace verification email states | [source](./email-confirmation-states.excalidraw) | [preview](./email-confirmation-states.svg) |
| — | verify-email-states | workspace verification link error states | [source](./verify-email-states.excalidraw) | [preview](./verify-email-states.svg) |

## Diagrams

### register-workspace-journey

Workspace onboarding only. First-owner identity setup continues through the setup-token `/register` handoff for users who are creating a workspace.

```mermaid
%%{init: {'theme':'dark','themeVariables':{'background':'#0d1117','mainBkg':'#0d1117','primaryColor':'#161b22','primaryBorderColor':'#388bfd','primaryTextColor':'#e6edf3','secondaryColor':'#21262d','secondaryBorderColor':'#388bfd','secondaryTextColor':'#e6edf3','tertiaryColor':'#161b22','tertiaryTextColor':'#e6edf3','lineColor':'#58a6ff','textColor':'#e6edf3','nodeBorder':'#388bfd','clusterBkg':'#161b22','clusterBorder':'#388bfd','titleColor':'#e6edf3','edgeLabelBackground':'#161b22','actorBkg':'#161b22','actorBorder':'#388bfd','actorTextColor':'#e6edf3','signalColor':'#58a6ff','labelBoxBkgColor':'#161b22','labelBoxBorderColor':'#388bfd','noteBkgColor':'#161b22','noteBorderColor':'#388bfd','noteTextColor':'#c9d1d9','activationBkgColor':'#30363d'}}}%%
sequenceDiagram
  actor Rep as Workspace representative
  participant Web as Web App
  participant API as Axis API
  participant Email as Email service
  participant Provisioning as Workspace provisioners

  Rep->>Web: Submit workspace name + workspace contact email + legal acceptance
  Web->>API: POST /api/workspaces
  API->>API: Validate, reserve slug, create pending workspace
  API->>Email: Send workspace verification email
  API-->>Web: Confirmation screen
  Rep->>Email: Open verification link
  Email-->>Web: Verification token
  Web->>API: POST /api/workspaces/verify-email
  API->>Provisioning: Start workspace provisioning
  Provisioning-->>API: Module provisioning reports
  API-->>Web: Workspace provisioning status
  Web-->>Rep: Continue to `/register` first-owner setup link when ready
```

### workspace-provisioning

Async multi-module workspace setup after workspace email verification.

```mermaid
%%{init: {'theme':'dark','themeVariables':{'background':'#0d1117','mainBkg':'#0d1117','primaryColor':'#161b22','primaryBorderColor':'#388bfd','primaryTextColor':'#e6edf3','secondaryColor':'#21262d','secondaryBorderColor':'#388bfd','secondaryTextColor':'#e6edf3','tertiaryColor':'#161b22','tertiaryTextColor':'#e6edf3','lineColor':'#58a6ff','textColor':'#e6edf3','nodeBorder':'#388bfd','clusterBkg':'#161b22','clusterBorder':'#388bfd','titleColor':'#e6edf3','edgeLabelBackground':'#161b22','actorBkg':'#161b22','actorBorder':'#388bfd','actorTextColor':'#e6edf3','signalColor':'#58a6ff','labelBoxBkgColor':'#161b22','labelBoxBorderColor':'#388bfd','noteBkgColor':'#161b22','noteBorderColor':'#388bfd','noteTextColor':'#c9d1d9','activationBkgColor':'#30363d'}}}%%
sequenceDiagram
  participant API as Platform API
  participant Kafka
  participant Mod as Module provisioners
  participant Coord as Platform coordinator

  API->>API: Workspace -> Provisioning
  API->>Kafka: WorkspaceVerifiedEvent
  Kafka->>Mod: Provision workspace schema per module
  Mod->>Mod: CREATE SCHEMA + EF migrations
  Mod->>Kafka: WorkspaceModuleProvisionReportEvent
  Kafka->>Coord: Aggregate module reports
  alt All modules succeeded
    Coord->>API: Mark workspace ACTIVE
  else Retries exhausted
    Coord->>Coord: Critical log and failed status
  end
```

**Related:** [register-user](../../identity-access/register-user/) owns standalone user registration. First owner/admin setup-token polish remains part of this workspace onboarding journey; third-party identity providers belong to user identity sign-in/provider-linking flows.
