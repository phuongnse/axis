# Product Vision

> **Navigation**: [docs](./README.md) · [AGENTS.md](../AGENTS.md)

Axis is currently an account-registration foundation, not a low-code platform implementation.

## Current Product Promise

A user can create a standalone Axis account with email/password, verify their email, complete the browser PKCE flow, and reach a simple account dashboard.

## Current User

| User | Goal |
|---|---|
| Self-service user | Create and verify an Axis account without external setup context. |

## In Scope Now

- Standalone email/password registration.
- Current legal-version acceptance during registration.
- Email verification and resend states.
- Authorization Code + PKCE after verification.
- Authenticated dashboard profile.

## Out Of Scope Now

Everything beyond the current registration path is out of scope until it has a new use-case spec, implementation plan, tests, and updated docs.

Future product scope must return through use-case specs first, then source and tests.
