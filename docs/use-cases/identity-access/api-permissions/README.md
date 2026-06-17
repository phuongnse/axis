# Use case — Permission enforcement on the API

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Every API endpoint to enforce the required permission so that unauthorized actions are rejected at the server regardless of what the frontend shows.

## Primary actor

- platform operator

## Trigger

- User initiates: every API endpoint to enforce the required permission

## Main flow

1. Authenticated request reaches an API endpoint with a declared permission policy.
2. API validates the JWT and evaluates required permissions from the request claims.
3. Authorized requests continue to the endpoint handler; unauthorized requests are rejected without revealing protected resource existence.

## Alternate / error flows

- Expired or invalid JWTs return HTTP 401 before permission checks run.
- Missing permissions return HTTP 403 with the required permission payload.
- Endpoints requiring multiple permissions must satisfy all required checks.
- Permission changes take effect after token refresh because permissions are claim-backed.

## Context

A resource-based permission system where each permission grants the ability to perform a specific action on a resource type. Permissions are assigned to roles, roles are assigned to users.

## Acceptance Criteria

*Happy path*
- [ ] Each endpoint is decorated with a policy attribute specifying the required permission(s).
- [ ] Requests from users who hold the required permission proceed normally.

*Validation & errors*
- [ ] A request without the required permission returns HTTP 403 with body: `{ "error": "forbidden", "required_permission": "workflow:definition:write" }`.
- [ ] A request with an expired or invalid JWT returns HTTP 401 before permission checks run.
- [ ] Missing permission returns 403, not 404 — resource existence is not revealed to unauthorized callers.

*Edge cases*
- [ ] An endpoint requiring multiple permissions (e.g., write + a feature flag) checks all conditions before returning 403.
- [ ] Permission checks are evaluated from the JWT claims at request time; role changes that happened after the JWT was issued take effect only after token refresh.
- [ ] All critical endpoints (write, delete, trigger) are covered by automated permission-enforcement tests in the test suite.

*Out of scope*
- Row-level security (e.g., "user can only edit their own records") — all permission checks are type-level.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** policy-based authorization middleware, `[RequirePermission]` attribute, and automated permission tests pending.
>
> **Decisions:**
> - permissions are included as a flat array in JWT claims at sign-in time (union of all role permissions)
> - checked via ASP.NET Core custom policy at API layer.
>
> **Deferred follow-ups:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
