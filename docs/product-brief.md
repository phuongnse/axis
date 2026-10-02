# Axis product brief

## Confirmed direction

Axis is a new business enterprise application platform. Organizations should
be able to define data structures and run business processes according to
their own needs. The owner wants a complete platform with a modern experience.
Axis is intended to support multiple business applications built on shared
platform capabilities.

The first requested step is NexKit setup with a dedicated runner on this VPS.
Agents use a separate ChatGPT/Codex subscription login, model `gpt-6.1-sol`,
and reasoning effort `max`.

## Requirement framing

Knowledge gained from other systems informs understanding of enterprise
applications. Axis issues must describe Axis's own user goals, expected
behavior, constraints and observable acceptance criteria. Keep unrelated
project names, source paths and references out of those issues. Knowledge
gathering does not establish a product requirement or an architecture decision.

## Decisions to establish through discovery

- Platform authors, application administrators, process participants and their
  representative scenarios in Axis.
- Which capabilities belong in the shared platform and which belong in
  application configuration or extensions.
- Data definition, relationships, validation and schema change requirements.
- How process definitions, forms and application views should be configured.
- Access control, audit, tenant isolation and operational requirements.
- Integration, import/export and reporting needs.
- Deployment expectations, architecture, technology choices and MVP boundaries.

These are discovery questions. No answer, feature commitment or technology
choice is implied by their inclusion.

## Current scope

The initial NexKit pipeline supports requirement discussion and approved
read-only analysis. Application implementation follows after the owner and
project establish an architecture and meaningful validation commands.
