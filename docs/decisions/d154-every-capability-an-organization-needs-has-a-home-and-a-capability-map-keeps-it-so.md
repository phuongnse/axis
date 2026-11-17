# D154. Every capability an organization needs has a home, and a capability map keeps it so

Status: **Agreed**

## Context

Axis was planned milestone by milestone, each one following from the one before. A design review of
the process instance page found that processes have one path: no two steps at once, no join, no task
for several people with a rule for when it is done, and no step for each item of a list. Approvals
by a board, a contract read by legal and finance side by side, or a task for each department named on
a request are everyday needs of an organization, and every comparable platform has them.

So the platform was checked against the references by which such platforms are judged: the
workflow patterns of control flow, resources and data, the executable part of BPMN, the criteria of
enterprise application platform evaluations, the features of comparable products, and the needs of
an organization that runs its work on Axis. The check found gaps of the same kind in every area:
processes, data and reporting, pages and forms, integration and documents, security, change and
operations. [Knowledge](../domain/knowledge.md#what-applications-use-most) already names some of them,
such as documents made from templates, as utilities the platform owes its applications.

## Decision

- **Axis covers what an organization needs to run its work.** A gap that the check found is closed,
  not left for each application to work round. The order follows what each gap blocks, not what is
  easiest.
- **Every capability has a home.** [The capability map](../capabilities.md) lists every capability an
  organization needs, by area. Each row links the doc section that holds its rule, or carries the
  planned marker of the milestone that builds it. A row with neither fails `scripts/check-docs.mjs`,
  so no capability is left without a plan, and the marker rule of
  [writing the docs](../writing-docs.md#planned-work) keeps a row true once its milestone is done.
- **The gaps become milestones in the existing phases**, as
  [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) orders them:
  - **Phase 2, growth,** gets the foundations every application needs, in this order: parallel paths
    and tasks for many people (M38), working time (M39), files and documents (M40), history and
    recovery (M41), everyday pages and forms (M42) and working together (M43). They come before M30,
    whose service desk needs working time, comments and inbound email. Then what a small business
    needs to run Axis on its own: the tenant runs itself (M44), data from other systems (M45), more
    channels (M46), personal data and retention (M47), AI inside applications (M48), public pages and
    portals (M49), and ready-made connectors and templates (M50).
  - **Phase 3, large organizations and government,** gets governed change (M51), directory and access
    (M52), organization-wide data (M53), analytics (M54), more protocols (M55), long-running processes
    (M56), continuity (M57) and everywhere (M58).
  - What each holds is in [the roadmap](../roadmap.md#milestones).
- **A foundation comes before what is built on it.** Several paths in one instance come before tasks
  for many people and for each item. The path of a file through calls and emails comes before
  documents, e-signature and extraction by AI. Values in history come before erasure and retention.
- **Each milestone gets its own decision before it is broken down.** The decision settles the design,
  with the owner, as [the life of a milestone](../delivery.md#the-life-of-a-milestone) describes. Where
  a gap was kept out on purpose by an agreed decision, that milestone's decision replaces the part it
  changes, whole, as [decisions](../writing-docs.md#decisions) requires. These are reopened:
  - one path per instance and the waiting step every loop needed, which
    [D155](d155-processes-run-paths-at-once-repeat-steps-give-tasks-to-many-people-and-take-error-paths.md) replaced for M38;
  - fixed durations with no working time ([D71](d71-task-deadlines-escalation-claim-and-reassign.md),
    [D75](d75-the-application-time-zone.md)), by M39;
  - history that keeps no values ([D21](d21-development-test-users-audit-records-and-sequences.md),
    [D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md)), by M41;
  - no email to a role ([D73](d73-in-app-and-email-notifications.md)), by M46;
  - only scalar outputs from a call ([D91](d91-connector-oauth2-and-structured-payloads.md)), by M45,
    and only JSON bodies and no grant that acts for a user, by M55;
  - authentication on every route ([D137](d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md)),
    for public pages only, by M49;
  - publishing with no second person's approval ([D47](d47-publishing-from-studio.md)), by M51;
  - no conversion between currencies ([D76](d76-file-money-and-enum-label-field-types.md)), by M53;
  - no compensation of earlier steps ([D35](d35-operator-retry-cancel-and-resolve.md)), by M56.

  Until such a decision is agreed, the old decision holds.
- **Phases and breakdown stay as D117 agrees.** Only the current phase is broken down, and phase 3
  starts when the owner names a first large or regulated customer.

## Consequences

- Nobody has to remember a gap: the map lists it, its row names its milestone, and the docs check
  fails when a row has no home.
- The map answers what Axis can do for an organization, with a link for each answer, for a buyer as
  well as for an author.
- Phase 2 grows by thirteen milestones and phase 3 by eight, so each phase runs longer. M30 waits for
  the foundations it needs instead of working round them.
- Several agreed decisions will be replaced in part. Each replacement is a decision of its own,
  written before its milestone is broken down.
- A change that adds a capability, or finds a new gap, adds a row to the map in the same change.

## Alternatives

- **Close gaps as they are found.** This is how the gap of parallel paths was found, late and in the
  middle of a design review. Others would be found the same way, by a customer.
- **One large milestone for every gap.** It could never be one vertical slice with one journey, and
  it would block everything after it.
- **Keep the gaps as issues only.** Issues of a later phase are not filed, because autopilot takes
  every open issue, so the gaps would live outside the system until their phase came.
