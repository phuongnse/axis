# Axis

Axis is a new enterprise application platform for organizations to define data
structures and run business processes according to their needs.

Architecture, technology choices and the first implementation scope remain open.
Read [the product brief](docs/product-brief.md) and [project instructions](AGENTS.md)
before product or architecture work.

## NexKit

The `discovery` pipeline supports requirement clarification and approved read-only
investigation. Application delivery and release are not configured.

Agents use **GPT-6.1 Sol** (`gpt-6.1-sol`) with **Max** reasoning (`max`), through
this project's dedicated ChatGPT subscription runner on the VPS.

- Create an issue and comment `/nexkit start discovery` to discuss a requirement.
- An authorized collaborator must approve the exact specification using the
  command posted by NexKit before read-only discovery runs.
- Find specifications and reports on the issue, and execution details in
  [GitHub Actions](https://github.com/phuongnse/axis/actions).

The accepted settings are in [.nexkit/project.json](.nexkit/project.json).
NexKit source and reusable workflows are pinned to an immutable release commit.
Runner administration is described in [runner operations](docs/runner-operations.md).
The repository owner maintains runner availability and its independent login.
