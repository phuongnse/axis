# Axis project instructions

Use English for source, comments, configuration and repository documentation.
Conversation with the owner may remain in Vietnamese.

Read `docs/product-brief.md` before product or architecture work. Axis is a new
enterprise application platform; its stack and first implementation scope
have not been selected. Distinguish observed behavior from proposals.

The accepted agent model is `gpt-6.1-sol` with reasoning effort `max`. Preserve
the owner's chosen model and effort when maintaining NexKit configuration.

Use the `discovery` pipeline for approved read-only investigations. Record
open decisions and evidence clearly. Human specification approval must come
from an authorized collaborator; never manufacture an approval.

Keep Axis issues self-contained. General knowledge from other systems must not
introduce unrelated project names, links, source paths or delivery dependencies.

NexKit workflow and control changes must go through an installation proposal
with exact file hashes. Keep the toolkit and reusable workflows pinned to the
accepted immutable release commit. Runner credentials belong only in the
dedicated runner's private login directory.

Before introducing application delivery, agree architecture and configure
real application tests and end-to-end checks. Infrastructure checks and
documentation validation do not establish application behavior. Report live
GitHub execution, live model access and simulated-model checks separately.

Application delivery requires successful current checks, an independent AI
review and one current authorized collaborator PR approval before merge.
Request changes feeds bounded repair; changed candidates invalidate approval.
Release requires its own issue and approval of an exact merged commit/version.
Installed delivery and release workflows remain blocked before reservation
until their accepted application readiness control is deliberately enabled.

Keep verification receipts, CLI logs and host snapshots outside the source
tree. Repository documentation describes expected behavior and the commands
needed to verify it.
