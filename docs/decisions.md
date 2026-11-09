# Decisions

Each decision has a status. **Agreed** means the owner has accepted it and work may rely on it.
**Proposed** means it is a working default that can still change.

Each decision in force is one file in [decisions/](decisions/). Find the decisions a change touches
in this table, then read only those files. A new decision takes the next free number and a new row at
the end of this table. A decision that a newer one replaced has no file: its row names the decision
that superseded it and links its last text in git. How a decision is written and replaced is in
[writing the docs](writing-docs.md#decisions). `scripts/check-docs.mjs` checks that this table and
the files agree, title and status included.

| Decision | Title | Status |
| --- | --- | --- |
| [D1](decisions/d1-applications-are-configuration.md) | Applications are configuration | **Agreed** |
| [D2](decisions/d2-axis-owns-its-engines.md) | Axis owns its engines | **Agreed** |
| [D3](decisions/d3-technology-stack.md) | Technology stack | **Agreed** |
| [D4](decisions/d4-modular-monolith.md) | Modular monolith | **Agreed** |
| [D5](decisions/d5-configuration-is-authored-as-files-first.md) | Configuration is authored as files first | **Agreed** |
| [D6](decisions/d6-in-configuration-logic-uses-a-typed-expression-language.md) | In-configuration logic uses a typed expression language | **Agreed** |
| [D7](decisions/d7-custom-code-lives-in-extension-packages.md) | Custom code lives in extension packages | **Agreed** |
| [D8](decisions/d8-custom-ui-widgets-are-packaged-react-components.md) | Custom UI widgets are packaged React components | **Agreed, deferred** |
| [D9](decisions/d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md) | Authentication: OIDC through a BFF, ready for FAPI 2.0 | **Agreed** |
| [D10](decisions/d10-tenant-aware-from-day-one-full-tenancy-later.md) | Tenant-aware from day one, full tenancy later | **Agreed** |
| [D11](decisions/d11-durable-process-engine.md) | Durable process engine | **Agreed** |
| [D12](decisions/d12-authorization-is-server-side-and-default-deny.md) | Authorization is server-side and default-deny | **Agreed** |
| [D13](decisions/d13-delivery-workflow.md) | Delivery workflow | **Agreed** |
| [D14](decisions/d14-repository-language.md) | Repository language | **Agreed** |
| [D15](decisions/d15-presentation-model.md) | Presentation model | **Agreed** |
| [D16](decisions/d16-expression-language.md) | Expression language | **Agreed** |
| [D17](decisions/d17-entity-logic-and-child-collections.md) | Entity logic and child collections | **Agreed** |
| [D18](decisions/d18-data-sources.md) | Data sources | **Agreed** |
| [D19](decisions/d19-process-resource-and-worker-host.md) | Process resource and worker host | **Agreed** |
| [D20](decisions/d20-human-tasks-task-inbox-and-forms.md) | Human tasks, task inbox and forms | **Agreed** |
| [D21](decisions/d21-development-test-users-audit-records-and-sequences.md) | Development test users, audit records and sequences | **Agreed** |
| [D22](decisions/d22-current-time-and-time-zones.md) | Current time and time zones | **Agreed** |
| [D23](decisions/d23-sign-in-through-oidc-and-the-bff-session.md) | Sign-in through OIDC and the BFF session | **Agreed** |
| [D24](decisions/d24-roles-and-policies.md) | Roles and policies | **Agreed** |
| [D25](decisions/d25-connectors-deployment-bindings-and-secrets.md) | Connectors, deployment bindings and secrets | **Agreed** |
| [D26](decisions/d26-events-and-triggers.md) | Events and triggers | **Agreed** |
| [D27](decisions/d27-inbound-endpoints.md) | Inbound endpoints | **Agreed** |
| [D28](decisions/d28-extension-packages.md) | Extension packages | **Agreed** |
| [D29](decisions/d29-release-pinning-and-text-resources.md) | Release pinning and text resources | **Agreed** |
| [D30](decisions/d30-schema-migrations.md) | Schema migrations | **Agreed** |
| [D31](decisions/d31-separate-database-roles.md) | Separate database roles | **Agreed** |
| [D32](decisions/d32-test-scenarios.md) | Test scenarios | **Agreed** |
| [D33](decisions/d33-recomputing-stored-computed-fields.md) | Recomputing stored computed fields | **Agreed** |
| [D34](decisions/d34-process-inspection.md) | Process inspection | **Agreed** |
| [D35](decisions/d35-operator-retry-cancel-and-resolve.md) | Operator retry, cancel and resolve | **Agreed** |
| [D36](decisions/d36-correlated-logs-metrics-and-traces.md) | Correlated logs, metrics and traces | **Agreed** |
| [D37](decisions/d37-health-checks-for-the-server-and-the-worker.md) | Health checks for the server and the worker | **Agreed** |
| [D38](decisions/d38-studio-edits-application-folders-through-an-authoring-api.md) | Studio edits application folders through an authoring API | **Agreed** |
| D39 | Reference index and where a resource is used | Superseded by [D141](decisions/d141-the-reference-index-and-what-uses-a-resource.md), [last text](https://github.com/phuongnse/axis/blob/eeeb537b826a17c87c517b1f2b4060abfe0fde60/docs/decisions/d39-reference-index-and-where-a-resource-is-used.md) |
| [D40](decisions/d40-reusable-modules.md) | Reusable modules | **Agreed** |
| [D41](decisions/d41-leave-requests-as-the-second-sample-application.md) | Leave requests as the second sample application | **Agreed** |
| [D42](decisions/d42-tenant-directory-in-the-platform-database.md) | Tenant directory in the platform database | **Agreed** |
| [D43](decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md) | Tenant provisioning and migrations across tenant databases | **Agreed** |
| [D44](decisions/d44-packaging-for-on-premises-and-cloud-installation.md) | Packaging for on-premises and cloud installation | **Agreed** |
| [D45](decisions/d45-tenant-backup-and-restore.md) | Tenant backup and restore | **Agreed** |
| [D46](decisions/d46-an-enterprise-application-platform-for-any-domain-and-the-milestones-after-m9.md) | An enterprise application platform for any domain and the milestones after M9 | **Agreed** |
| [D47](decisions/d47-publishing-from-studio.md) | Publishing from Studio | **Agreed** |
| [D48](decisions/d48-preview-tenants.md) | Preview tenants | **Agreed** |
| [D49](decisions/d49-several-server-replicas-behind-a-tls-proxy.md) | Several server replicas behind a TLS proxy | **Agreed** |
| [D50](decisions/d50-sign-in-audit-records-and-security-scanning-in-ci.md) | Sign-in audit records and security scanning in CI | **Agreed** |
| [D51](decisions/d51-creating-applications-and-resources-from-studio.md) | Creating applications and resources from Studio | **Agreed** |
| [D52](decisions/d52-reference-pickers-label-texts-and-the-expression-editor.md) | Reference pickers, label texts and the expression editor | **Agreed** |
| [D53](decisions/d53-typed-editors-and-the-process-graph-in-studio.md) | Typed editors and the process graph in Studio | **Agreed** |
| [D54](decisions/d54-the-entity-designer-and-the-policy-matrix.md) | The entity designer and the policy matrix | **Agreed** |
| [D55](decisions/d55-running-test-scenarios-from-studio.md) | Running test scenarios from Studio | **Agreed** |
| [D56](decisions/d56-a-service-desk-as-the-third-sample-application.md) | A service desk as the third sample application | **Agreed** |
| [D57](decisions/d57-a-design-system-published-from-the-repository.md) | A design system published from the repository | **Agreed** |
| [D58](decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md) | Ant Design is a detail of the SPA, without ProComponents | **Agreed** |
| [D59](decisions/d59-table-row-and-toolbar-actions.md) | Table row and toolbar actions | **Agreed** |
| [D60](decisions/d60-the-service-desk-dashboard-ticket-detail-page-and-guided-form.md) | The service desk dashboard, ticket detail page and guided form | **Agreed** |
| [D61](decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md) | Widget trees in columns, tabs and sections, and pages at phone width | **Agreed** |
| [D62](decisions/d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md) | Conditional form states, validation as you type and wizard steps | **Agreed** |
| [D63](decisions/d63-navigate-actions-and-page-parameters.md) | Navigate actions and page parameters | **Agreed** |
| [D64](decisions/d64-detail-chart-and-kpi-widgets.md) | Detail, chart and KPI widgets | **Agreed** |
| [D65](decisions/d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md) | A visual page designer on a canvas drawn by the site's widgets | **Agreed** |
| [D66](decisions/d66-a-visual-form-designer-for-form-resources.md) | A visual form designer for form resources | **Agreed** |
| [D67](decisions/d67-live-widget-previews-in-the-design-system.md) | Live widget previews in the design system | **Agreed** |
| [D68](decisions/d68-issues-of-several-milestones-run-at-once.md) | Issues of several milestones run at once | **Agreed** |
| [D69](decisions/d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md) | Timer, wait for event and sub-process steps, loops and create record | **Agreed** |
| [D70](decisions/d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md) | Processes without a subject, scheduled jobs and delete triggers | **Agreed** |
| [D71](decisions/d71-task-deadlines-escalation-claim-and-reassign.md) | Task deadlines, escalation, claim and reassign | **Agreed** |
| [D72](decisions/d72-a-delivery-limit-and-dead-letters.md) | A delivery limit and dead letters | **Agreed** |
| [D73](decisions/d73-in-app-and-email-notifications.md) | In-app and email notifications | **Agreed** |
| [D74](decisions/d74-a-visual-process-designer.md) | A visual process designer | **Agreed** |
| [D75](decisions/d75-the-application-time-zone.md) | The application time zone | **Agreed** |
| [D76](decisions/d76-file-money-and-enum-label-field-types.md) | File, money and enum label field types | **Agreed** |
| [D77](decisions/d77-indexes-defaults-and-ranges.md) | Indexes, defaults and ranges | **Agreed** |
| [D78](decisions/d78-references-from-child-rows-and-many-to-many.md) | References from child rows and many-to-many | **Agreed** |
| [D79](decisions/d79-import-and-export-under-policies.md) | Import and export under policies | **Agreed** |
| [D80](decisions/d80-search-more-aggregates-and-query-timeouts.md) | Search, more aggregates and query timeouts | **Agreed** |
| [D81](decisions/d81-asset-management-as-the-fourth-sample-application.md) | Asset management as the fourth sample application | **Agreed** |
| [D82](decisions/d82-performance-slos-and-load-tests.md) | Performance SLOs and load tests | **Agreed** |
| [D83](decisions/d83-the-fapi-20-profile-per-tenant.md) | The FAPI 2.0 profile per tenant | **Agreed** |
| [D84](decisions/d84-asvs-level-2-and-a-server-side-session.md) | ASVS Level 2 and a server-side session | **Agreed** |
| [D85](decisions/d85-rate-limits-and-security-headers.md) | Rate limits and security headers | **Agreed** |
| [D86](decisions/d86-service-accounts-for-the-record-and-process-apis.md) | Service accounts for the record and process APIs | **Agreed** |
| [D87](decisions/d87-tenant-setting-commands-and-key-rotation.md) | Tenant setting commands and key rotation | **Agreed** |
| [D88](decisions/d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md) | Custom widgets in the designer palette and a published SDK | **Agreed** |
| [D89](decisions/d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md) | Modules that hold processes, pages and policies and depend on other modules | **Agreed** |
| [D90](decisions/d90-extension-packages-run-out-of-process.md) | Extension packages run out of process | **Agreed** |
| [D91](decisions/d91-connector-oauth2-and-structured-payloads.md) | Connector OAuth2 and structured payloads | **Agreed** |
| [D92](decisions/d92-outbound-webhooks.md) | Outbound webhooks | **Agreed** |
| [D93](decisions/d93-an-operator-web-console.md) | An operator web console | **Agreed** |
| [D94](decisions/d94-purchase-requests-prove-the-extensibility-milestone.md) | Purchase requests prove the extensibility milestone | **Agreed** |
| D95 | Widget previews show the states a design change touches | Superseded by [D142](decisions/d142-widget-previews-show-the-states-a-design-change-touches-one-at-a-time-where-they-share-fixtures.md), [last text](https://github.com/phuongnse/axis/blob/91b43d2dc63d9f317b1c2bcbf7f217395b30bde9/docs/decisions/d95-widget-previews-show-the-states-a-design-change-touches.md) |
| D96 | Rules from the design system review | Superseded by [D133](decisions/d133-docs-state-each-current-rule-once.md), [last text](https://github.com/phuongnse/axis/blob/c6fefbe9cb6c26665612eb2a94f57ce716438ad7/docs/decisions/d96-rules-from-the-design-system-review.md) |
| [D97](decisions/d97-reliable-test-runs.md) | Reliable test runs | **Agreed** |
| D98 | Rules from the shell review | Superseded by [D133](decisions/d133-docs-state-each-current-rule-once.md), [last text](https://github.com/phuongnse/axis/blob/c6fefbe9cb6c26665612eb2a94f57ce716438ad7/docs/decisions/d98-rules-from-the-shell-review.md) |
| [D99](decisions/d99-axis-ships-its-own-identity-provider.md) | Axis ships its own identity provider | **Agreed** |
| [D100](decisions/d100-identity-providers-per-application-and-sign-in-per-site.md) | Identity providers per application and sign-in per site | **Agreed** |
| [D101](decisions/d101-the-extensibility-milestone-is-split-into-six.md) | The extensibility milestone is split into six | **Agreed** |
| [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) | Built-in modules open to change and bound by a contract | **Agreed** |
| [D103](decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md) | Modules hold triggers, scenarios and connector slots | **Agreed** |
| D104 | CodeQL and the performance tests run every night | Superseded by [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md), [last text](https://github.com/phuongnse/axis/blob/7a4b98e91afc50fb844b235afd67f702f1ed0f91/docs/decisions/d104-codeql-and-the-performance-tests-run-every-night.md) |
| D105 | CI on main runs every two hours, and four issues run at once | Superseded by [D144](decisions/d144-branches-need-not-be-up-to-date.md) and [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md), [last text](https://github.com/phuongnse/axis/blob/7a4b98e91afc50fb844b235afd67f702f1ed0f91/docs/decisions/d105-ci-on-main-runs-every-two-hours-and-four-issues-run-at-once.md) |
| [D106](decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md) | A threat model kept current like the ASVS tracking | **Agreed** |
| [D107](decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) | The dependency scan runs now, and images are scanned and signed | **Agreed** |
| [D108](decisions/d108-reporting-a-vulnerability-and-security-fixes.md) | Reporting a vulnerability and security fixes | **Agreed** |
| [D109](decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) | Sensitive fields and backups are encrypted, and history keeps no names | **Agreed** |
| [D110](decisions/d110-security-alerts-and-a-hardening-checklist-for-the-operator.md) | Security alerts and a hardening checklist for the operator | **Agreed** |
| D111 | Authentication by default, and the owner guards the security checks | Superseded by [D137](decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md), [last text](https://github.com/phuongnse/axis/blob/dedd8e4394593f334d6df987d33efef009c5414c/docs/decisions/d111-authentication-by-default-and-the-owner-guards-the-security-checks.md) |
| D112 | CI, CodeQL and the performance tests run on main every few merges | Superseded by [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md), [last text](https://github.com/phuongnse/axis/blob/a6394ba03ec0b93fc1a1952b3854d62cbde9646b/docs/decisions/d112-ci-codeql-and-the-performance-tests-run-on-main-every-few-merges.md) |
| D113 | Branches need not be up to date, and three issues run at once | Superseded by [D144](decisions/d144-branches-need-not-be-up-to-date.md), [last text](https://github.com/phuongnse/axis/blob/f622b875d861beb4b51cd99f03dfb2bc4fea4061/docs/decisions/d113-branches-need-not-be-up-to-date-and-three-issues-run-at-once.md) |
| D114 | Rules from the sign-in and home page review | Superseded by [D133](decisions/d133-docs-state-each-current-rule-once.md), [last text](https://github.com/phuongnse/axis/blob/c6fefbe9cb6c26665612eb2a94f57ce716438ad7/docs/decisions/d114-rules-from-the-sign-in-and-home-page-review.md) |
| [D115](decisions/d115-an-inbound-endpoint-call-raises-an-event.md) | An inbound endpoint call raises an event | **Agreed** |
| [D116](decisions/d116-how-sensitive-fields-and-backups-are-encrypted.md) | How sensitive fields and backups are encrypted | **Agreed** |
| [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) | Small businesses first, and the milestones in three phases | **Agreed** |
| [D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) | Axis SaaS in cells, with sign-up, quotas and billing | **Agreed** |
| [D119](decisions/d119-ai-authoring-in-studio.md) | AI authoring in Studio | **Agreed** |
| [D120](decisions/d120-axis-is-agpl-licensed-with-a-commercial-licence.md) | Axis is AGPL licensed, with a commercial licence | **Agreed** |
| [D121](decisions/d121-how-a-module-contract-is-declared-and-checked.md) | How a module contract is declared and checked | **Agreed** |
| [D122](decisions/d122-how-a-customization-layer-is-written-and-applied.md) | How a customization layer is written and applied | **Agreed** |
| [D123](decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md) | How an Axis SaaS cell keeps tenants apart, measured and recoverable | **Agreed** |
| [D124](decisions/d124-the-organization-and-approvals-modules.md) | The organization and approvals modules | **Agreed** |
| [D125](decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) | How AI authoring connects, runs and keeps its changes | **Agreed** |
| [D126](decisions/d126-sign-up-trials-and-the-control-plane.md) | Sign-up, trials and the control plane | **Agreed** |
| [D127](decisions/d127-plans-payments-and-invoices.md) | Plans, payments and invoices | **Agreed** |
| D128 | Rules from the Studio review | Superseded by [D133](decisions/d133-docs-state-each-current-rule-once.md), [last text](https://github.com/phuongnse/axis/blob/c6fefbe9cb6c26665612eb2a94f57ce716438ad7/docs/decisions/d128-rules-from-the-studio-review.md) |
| D129 | Rules from the Studio editors review | Superseded by [D133](decisions/d133-docs-state-each-current-rule-once.md), [last text](https://github.com/phuongnse/axis/blob/c6fefbe9cb6c26665612eb2a94f57ce716438ad7/docs/decisions/d129-rules-from-the-studio-editors-review.md) |
| [D130](decisions/d130-activate-a-release-replaces-roll-back.md) | "Activate a release" replaces "roll back" | **Agreed** |
| [D131](decisions/d131-tenant-notices-live-in-axisnotifications.md) | Tenant notices live in axis.notifications | **Agreed** |
| D132 | Rules from the Studio settings review | Superseded by [D133](decisions/d133-docs-state-each-current-rule-once.md), [last text](https://github.com/phuongnse/axis/blob/c6fefbe9cb6c26665612eb2a94f57ce716438ad7/docs/decisions/d132-rules-from-the-studio-settings-review.md) |
| [D133](decisions/d133-docs-state-each-current-rule-once.md) | Docs state each current rule once | **Agreed** |
| D134 | Docs stay true while work merges | Superseded by [D145](decisions/d145-docs-stay-true-and-an-audit-reads-them-every-few-merges.md), [last text](https://github.com/phuongnse/axis/blob/ca77a8d1aefab251f800f77636d2d99f87e4e836/docs/decisions/d134-docs-stay-true-while-work-merges.md) |
| D135 | Docs stay true, and an audit reads them every hundred merges | Superseded by [D145](decisions/d145-docs-stay-true-and-an-audit-reads-them-every-few-merges.md), [last text](https://github.com/phuongnse/axis/blob/40f4289b57dec9f948818b122dedc2399a135d2b/docs/decisions/d135-docs-stay-true-and-an-audit-reads-them-every-hundred-merges.md) |
| D136 | CI, CodeQL and the performance tests run on main every few merges, started by NexKit alone | Superseded by [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md), [last text](https://github.com/phuongnse/axis/blob/65a18384a1c786cec5baaf8d452141403f1eaf40/docs/decisions/d136-ci-codeql-and-the-performance-tests-run-on-main-every-few-merges-started-by-nexkit-alone.md) |
| [D137](decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md) | Authentication by default, and the owner guards the security checks alone | **Agreed** |
| [D138](decisions/d138-the-docs-follow-arc42-c4-diagrams-in-mermaid-and-four-kinds-of-page.md) | The docs follow arc42, C4 diagrams in Mermaid and four kinds of page | **Agreed** |
| D139 | CI runs on main alone, and NexKit checks every branch | Superseded by [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md), [last text](https://github.com/phuongnse/axis/blob/f50f54c45cb465f5538a1d578ed52875c5b7450c/docs/decisions/d139-ci-runs-on-main-alone-and-nexkit-checks-every-branch.md) |
| [D140](decisions/d140-a-new-screen-is-designed-first-on-a-short-prototype-with-fixtures.md) | A new screen is designed first, on a short prototype with fixtures | **Agreed** |
| [D141](decisions/d141-the-reference-index-and-what-uses-a-resource.md) | The reference index, and what uses a resource | **Agreed** |
| [D142](decisions/d142-widget-previews-show-the-states-a-design-change-touches-one-at-a-time-where-they-share-fixtures.md) | Widget previews show the states a design change touches, one at a time where they share fixtures | **Agreed** |
| D143 | Branches need not be up to date, and one issue runs at a time | Superseded by [D144](decisions/d144-branches-need-not-be-up-to-date.md), [last text](https://github.com/phuongnse/axis/blob/6421ea9771a4baad0df94ea48efd50d60ee2a792/docs/decisions/d143-branches-need-not-be-up-to-date-and-one-issue-runs-at-a-time.md) |
| [D144](decisions/d144-branches-need-not-be-up-to-date.md) | Branches need not be up to date | **Agreed** |
| [D145](decisions/d145-docs-stay-true-and-an-audit-reads-them-every-few-merges.md) | Docs stay true, and an audit reads them every few merges | **Agreed** |
| [D146](decisions/d146-ci-runs-on-main-every-few-merges-and-nexkit-checks-every-branch.md) | CI runs on main every few merges, and NexKit checks every branch | **Agreed** |
