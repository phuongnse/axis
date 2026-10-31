# Decisions

Each decision has a status. **Agreed** means the owner has accepted it and work may rely on it.
**Proposed** means it is a working default that can still change.

Each decision is one file in [decisions/](decisions/). Find the decisions a change touches in this
table, then read only those files. A new decision is a new file, `decisions/d<n>-<slug>.md`, with
the next free number, and a new row at the end of this table. Change an existing decision only by
recording a newer one that replaces it. `scripts/check-docs.mjs` checks that this table and the
files agree, title and status included.

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
| [D39](decisions/d39-reference-index-and-where-a-resource-is-used.md) | Reference index and where a resource is used | **Agreed** |
| [D40](decisions/d40-reusable-modules.md) | Reusable modules | **Agreed** |
| [D41](decisions/d41-leave-requests-as-the-second-sample-application.md) | Leave requests as the second sample application | **Agreed** |
| [D42](decisions/d42-tenant-directory-in-the-platform-database.md) | Tenant directory in the platform database | **Agreed** |
| [D43](decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md) | Tenant provisioning and migrations across tenant databases | **Agreed** |
| [D44](decisions/d44-packaging-for-on-premises-and-cloud-installation.md) | Packaging for on-premises and cloud installation | **Agreed** |
| [D45](decisions/d45-tenant-backup-and-restore.md) | Tenant backup and restore | **Agreed** |
| [D46](decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) | A general-purpose low-code platform and the milestones after M9 | **Agreed** |
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
| [D95](decisions/d95-widget-previews-show-the-states-a-design-change-touches.md) | Widget previews show the states a design change touches | **Agreed** |
| [D96](decisions/d96-rules-from-the-design-system-review.md) | Rules from the design system review | **Agreed** |
| [D97](decisions/d97-reliable-test-runs.md) | Reliable test runs | **Agreed** |
| [D98](decisions/d98-rules-from-the-shell-review.md) | Rules from the shell review | **Agreed** |
| [D99](decisions/d99-axis-ships-its-own-identity-provider.md) | Axis ships its own identity provider | **Agreed** |
| [D100](decisions/d100-identity-providers-per-application-and-sign-in-per-site.md) | Identity providers per application and sign-in per site | **Agreed** |
| [D101](decisions/d101-the-extensibility-milestone-is-split-into-six.md) | The extensibility milestone is split into six | **Agreed** |
| [D102](decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) | Built-in modules open to change and bound by a contract | **Agreed** |
| [D103](decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md) | Modules hold triggers, scenarios and connector slots | **Agreed** |
| [D104](decisions/d104-codeql-and-the-performance-tests-run-every-night.md) | CodeQL and the performance tests run every night | **Agreed** |
| [D105](decisions/d105-ci-on-main-runs-every-two-hours-and-four-issues-run-at-once.md) | CI on main runs every two hours, and four issues run at once | **Agreed** |
| [D106](decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md) | A threat model kept current like the ASVS tracking | **Agreed** |
| [D107](decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) | The dependency scan runs now, and images are scanned and signed | **Agreed** |
| [D108](decisions/d108-reporting-a-vulnerability-and-security-fixes.md) | Reporting a vulnerability and security fixes | **Agreed** |
| [D109](decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) | Sensitive fields and backups are encrypted, and history keeps no names | **Agreed** |
| [D110](decisions/d110-security-alerts-and-a-hardening-checklist-for-the-operator.md) | Security alerts and a hardening checklist for the operator | **Agreed** |
| [D111](decisions/d111-authentication-by-default-and-the-owner-guards-the-security-checks.md) | Authentication by default, and the owner guards the security checks | **Agreed** |
| [D112](decisions/d112-ci-codeql-and-the-performance-tests-run-on-main-every-few-merges.md) | CI, CodeQL and the performance tests run on main every few merges | **Agreed** |
| [D113](decisions/d113-branches-need-not-be-up-to-date-and-three-issues-run-at-once.md) | Branches need not be up to date, and three issues run at once | **Agreed** |
| [D114](decisions/d114-rules-from-the-sign-in-and-home-page-review.md) | Rules from the sign-in and home page review | **Agreed** |
| [D115](decisions/d115-an-inbound-endpoint-call-raises-an-event.md) | An inbound endpoint call raises an event | **Agreed** |
| [D116](decisions/d116-how-sensitive-fields-and-backups-are-encrypted.md) | How sensitive fields and backups are encrypted | **Agreed** |
| [D117](decisions/d117-small-businesses-first-and-the-milestones-in-three-phases.md) | Small businesses first, and the milestones in three phases | **Agreed** |
| [D118](decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) | Axis SaaS in cells, with sign-up, quotas and billing | **Agreed** |
| [D119](decisions/d119-ai-authoring-in-studio.md) | AI authoring in Studio | **Agreed** |
| [D120](decisions/d120-axis-is-agpl-licensed-with-a-commercial-licence.md) | Axis is AGPL licensed, with a commercial licence | **Agreed** |
