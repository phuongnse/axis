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
| [D76](decisions/d76-file-money-and-enum-label-field-types.md) | File, money and enum label field types | **Agreed** |
| [D77](decisions/d77-indexes-defaults-and-ranges.md) | Indexes, defaults and ranges | **Agreed** |
| [D78](decisions/d78-references-from-child-rows-and-many-to-many.md) | References from child rows and many-to-many | **Agreed** |
| [D79](decisions/d79-import-and-export-under-policies.md) | Import and export under policies | **Agreed** |
| [D80](decisions/d80-search-more-aggregates-and-query-timeouts.md) | Search, more aggregates and query timeouts | **Agreed** |
| [D81](decisions/d81-asset-management-as-the-fourth-sample-application.md) | Asset management as the fourth sample application | **Agreed** |
