# D165. Axis is under the AGPL and MIT licences only, with no commercial licence

Status: **Agreed**

## Context

Axis is an open-source project for its community, and nobody may use, change or share code that has
no licence. Its licence must keep the platform open, also when a changed copy is offered to users over
a network. It must also bind nobody who builds on it.

Axis also runs as a SaaS that an operator runs, as
[D167](d167-an-operator-runs-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) agrees. The licence
keeps such a hosted copy open too, so that another company cannot run a closed copy of it as a
competing service.

## Decision

- **The platform is AGPL-3.0-only.** Everything in the repository, except the parts below, is under
  the GNU Affero General Public License, version 3 only, in `LICENSE` at the root. Whoever offers a
  changed Axis to users over a network must offer those users its source.
- **What others build on is under the MIT licence,** so that it binds nobody: `src/Axis.Extensions`,
  which extension packages reference, and `samples/`, the sample applications, packages, modules and
  templates. Each holds its own `LICENSE` file.
- **The widget SDK is under the MIT licence too** _(planned for M22)_. `@axis/widgets-sdk`, the npm
  package a custom widget builds on, holds its own `LICENSE` file.
- **An application belongs to its author.** An application's own resources, and a business's
  customization layer, are the business's, whatever the platform's licence.
- **No commercial licence is offered.** Axis is offered under these open licences only.
- **A contribution comes under the licence of the files it changes.** A contributor signs nothing
  further before the contribution is merged.
- **NexKit stays under the MIT licence.**

How Axis shows its licence and offers its source is in
[open source licences](../reference/legal.md#open-source-licences).

## Consequences

- The AGPL keeps every changed copy that is hosted for users open. The MIT parts let anyone build on
  Axis under any licence.
- A company that cannot accept the AGPL cannot use the platform, because no other licence is offered.
- Anyone can contribute without signing anything first.
- Changing the platform's licence later needs the consent of every contributor whose code it holds.

## Alternatives

- **The AGPL with a commercial licence sold beside it,** as D120 had it. It brought income and
  support. But contributors would first have to give the owner the right to sell their code under
  another licence, and Axis is a project for its community.
- **A permissive licence.** A larger company could host Axis and compete with its own code.
- **A licence that is not open source.** It would cost the trust of the developers and the public
  bodies that Axis wants later.
