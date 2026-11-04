# Threat model

What can go wrong in Axis's own design, and what Axis does about each threat. The model follows
[D106](../decisions/d106-a-threat-model-kept-current-like-the-asvs-tracking.md). Its threat rows are
built. Each row comes from the issue that models one trust boundary, and the
[ASVS](asvs.md) page covers the controls that every web application needs. How the model is filled is in
[threat model method](threat-model-method.md).

The model has three scopes, each in a file of its own. The letter is the start of its ids. An id is unique across all three files.

- **P**, [the product](threat-model-product.md): the server, worker, SPA, Studio, packages, connectors, identity provider and personal data.
- **I**, [the installation](threat-model-installation.md): the operator's install, its images, hosts, databases, backups and secrets, and the cell console.
- **B**, [the way Axis is built](threat-model-build.md): NexKit, CI, the AI agents and the repository.
