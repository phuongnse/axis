# Implementation Progress

> **Navigation**: [docs](./README.md) · [AGENTS.md](../AGENTS.md)

## Current Product Slice

| Slice | Status | Owner |
|---|---|---|
| Standalone user registration | Done | [register-user](./use-cases/identity-access/register-user/README.md) |

## Layer Status

| Layer | Status | Notes |
|---|---|---|
| Domain | Done | User registration, email verification, personal workspace membership, and legal acceptance behavior. |
| Application | Done | Register user, verify email, resend verification, current user profile, and token claims. |
| Infrastructure | Done | Identity EF Core persistence, migrations, repositories, password hashing, email sender, Redis-backed support services, and OpenIddict storage. |
| API | Done | Register, legal versions, verify/resend email, current user, PKCE authorize, and token exchange. |
| Frontend | Done | Register, confirmation, verification, callback, app shell, and account dashboard. |
| Local dev | Done | Compose stack for PostgreSQL, Redis, Maildev, API, web, optional collector, and E2E runner. |

## Removed Scope

All non-registration product surfaces were removed because they were not end-to-end usable slices in this repository.

## Reintroduction Rule

Reintroduce removed scope only through a fresh use-case spec with acceptance criteria, tests, and a Design Gate. Source, docs, and verification must land together for the smallest usable slice.
