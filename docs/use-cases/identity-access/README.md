# Identity Access

> **Navigation**: [Use cases](../README.md) · [docs](../../README.md)

Identity Access currently owns one complete product slice: standalone email/password account registration, email verification, PKCE token exchange, and the account dashboard reached after verification.

## Current Use Cases

| Use case | Status |
|---|---|
| [Register a standalone user account](register-user/README.md) | Done |

## Current API Surface

| Endpoint | Purpose |
|---|---|
| `POST /api/users/register` | Create the standalone user, personal workspace membership, and verification email. |
| `GET /api/users/me` | Return the current verified user's dashboard profile. |
| `GET /api/auth/legal-versions` | Return the current user-level legal versions for registration. |
| `POST /api/auth/verify-email` | Verify the email token and establish the browser sign-in session. |
| `POST /api/auth/resend-verification` | Re-send a usable verification email when allowed. |
| `GET /connect/authorize` | Start Authorization Code + PKCE for the SPA. |
| `POST /connect/token` | Exchange an authorization code for tokens. |

`Axis.Api` emits `openapi.json`; the frontend consumes generated types in `frontend/src/lib/api-types.ts`. Regenerate with `python scripts/axis.py frontend gen-api-types` after REST contract changes.

## Out Of Repo Scope

The repository intentionally contains only the verified standalone-registration path. Add a new use-case spec before reintroducing any removed capability.

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | Done | User, workspace, membership, email verification, and legal acceptance behavior for standalone registration. |
| Application | Done | Register, verify email, resend verification, current user profile, and token-claim queries. |
| Infrastructure | Done | Identity EF Core persistence, repositories, BCrypt password hashing, email sender, OpenIddict client/token storage, and migrations. |
| API | Done | Registration, legal versions, email verification/resend, current user profile, PKCE authorize, and code token exchange. |
| Frontend | Done | Register, confirmation, verification, callback, authenticated shell, and account dashboard. |

## Reintroduction Rule

If a removed capability becomes real work again, create or restore the owning use-case document first, define acceptance criteria and verification, then implement the smallest end-to-end slice. Do not add placeholder folders or keep partial source behind "future" docs.
