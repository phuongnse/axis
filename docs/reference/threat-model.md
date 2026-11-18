# Threat model

The threat model lists what can go wrong in Axis's own design and what Axis does about each threat,
one trust boundary at a time. It complements the [ASVS](asvs.md) page, which covers the controls
that every web application needs. [Threat model method](threat-model-method.md) says how each row is
found, rated and checked, and
[D106](../decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md) says why Axis keeps
the model.

The model has four scopes, and each scope has its own file or files. A scope's letter starts its ids, and
an id is unique across every file.

- **P**, [the product](threat-model-product.md): the server, worker, SPA, Studio, the MCP server and the agent host, packages and connectors. [History and recovery](threat-model-product-history.md) holds its threats to the history of records, the tenant's audit and the recycle bin, and [collaboration](threat-model-product-collaboration.md) holds its threats to tasks, absences, deciding several tasks, comments, live pages and notification preferences.
- **P**, [the identity provider and personal data](threat-model-identity-and-personal-data.md): the identity provider Axis ships and the personal data Axis holds.
- **I**, [the installation](threat-model-installation.md): the operator's install, its images, hosts, databases, backups and secrets, and the cell console.
- **B**, [the way Axis is built](threat-model-build.md): NexKit, CI, the AI agents and the repository.
- **C**, [the control plane](threat-model-control-plane.md): the sign-up page, the control plane's calls to the cells, the directory's DNS writes, the payment webhooks, the e-invoice provider, and the billing portal's sign-in and pages.
