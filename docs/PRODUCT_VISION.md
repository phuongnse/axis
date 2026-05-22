# Product Vision

[← Back to Docs Home](./README.md)

---

## Overview

**Axis** is a multi-tenant SaaS low-code platform that enables organizations to build data-driven workflow applications without writing code. Users can model their own data, design automated workflows, create interactive forms, and build custom UI pages — all within a unified visual environment.

---

## Problem Statement

Organizations of all sizes need to digitize and automate their business processes. Custom software development is expensive, slow, and requires dedicated engineering teams. Existing low-code tools are either too rigid (can't model custom data), too complex (require DevOps expertise), or too generic (lack workflow depth).

**Core pain points:**

- Business teams can't build the tools they need without engineering resources.
- Custom data structures change frequently — rigid schemas don't keep up.
- Workflow logic lives in spreadsheets, emails, and manual handoffs.
- Form data is disconnected from business processes and approvals.

---

## Solution

Axis provides a unified platform where non-technical users can:

1. **Define their data** — Create custom models and data classes that match their business domain.
2. **Design workflows** — Visually build multi-step automated processes with branching, conditions, and integrations.
3. **Collect data via forms** — Embed interactive forms directly into workflow steps.
4. **Build UI** — Compose pages with pre-built widgets (lists, grids, charts) that are bound to live data and workflows.

---

## Target Users

| Role | Who They Are | Primary Goals |
|---|---|---|
| **Organization Admin** | Business owner or ops lead of a tenant org | Configure the platform, manage users, build workflows |
| **Organization Member** | Employees within the org | Execute workflows, manage records, fill forms |
| **End User** | Customers or external stakeholders | Submit forms, view published pages |
| **Platform Admin** | Axis internal team | Manage tenants, monitor platform health |

---

## Key Differentiators

- **Custom data modeling** — Users define their own schemas; no pre-built rigid data structures.
- **Deep workflow engine** — Branching, parallel steps, multiple trigger types (manual, schedule, webhook, event).
- **Form-workflow integration** — Forms are first-class workflow steps, not afterthoughts.
- **Multi-tenancy by design** — Each organization is fully isolated with its own database schema.
- **Developer-friendly import/export** — Workflows and models can be imported/exported as JSON.

---

## MVP Scope (Phase 1)

The MVP focuses on the core loop that makes the platform valuable:

```
Define Data → Build Workflow → Collect via Form → Execute & Monitor
```

| Capability | Included in MVP |
|---|---|
| Multi-tenant organization management | Yes |
| User authentication & RBAC | Yes |
| Custom data modeling | Yes |
| Workflow builder (visual canvas) | Yes |
| Step types: Form, HTTP, Condition, Script, Notification | Yes |
| Trigger types: Manual, Schedule, Webhook, Event | Yes |
| Branching & parallel execution | Yes |
| Form builder | Yes |
| Workflow execution engine | Yes |
| Execution history & error notifications | Yes |
| Page & UI builder | No (Phase 2) |

