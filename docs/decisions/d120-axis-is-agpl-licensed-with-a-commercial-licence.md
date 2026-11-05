# D120. Axis is AGPL licensed, with a commercial licence

Status: **Agreed**

## Context

Axis is meant to be open source, and nobody may use, change or share code that has no licence. Axis
is also a business that sells a SaaS, as
[D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) agrees, so its licence must keep
the code open without letting another company run a closed copy of it as a competing service. The
owner holds the copyright of all the code, so the licence can still be chosen freely.

## Decision

- **The platform is AGPL-3.0-only.** Everything in the repository, except the parts below, is under
  the GNU Affero General Public License, version 3 only, in `LICENSE` at the root. Whoever offers a
  changed Axis to users over a network must offer those users its source.
- **A commercial licence is sold beside it,** with support, to a customer that cannot accept the
  AGPL, such as a large organization, a bank or a government that installs Axis itself.
- **What others build on is under the MIT licence,** so that it binds nobody: `src/Axis.Extensions`,
  which extension packages reference, the widget SDK `@axis/widgets-sdk`, and `samples/`, the sample
  applications, packages, modules and templates. Each holds its own `LICENSE` file.
- **An application belongs to its author.** An application's own resources, and a business's
  customization layer, are the business's, whatever the platform's licence.
- **Contributors sign a contributor licence agreement** before their first contribution is merged,
  so that the owner can keep selling the commercial licence.
- **NexKit stays under the MIT licence.**

How Axis shows its licence and offers its source is in
[open source licences](../reference/legal.md#open-source-licences).

## Consequences

- The AGPL makes a closed hosted copy impossible, and the commercial licence is also income.
- Some companies avoid the AGPL until they buy the commercial licence.
- Outside contributions need an agreement first.

## Alternatives

- **A permissive licence.** A larger company could host Axis and compete with its own code.
- **A licence that is not open source.** It would cost the trust of the developers and the public
  bodies that Axis wants later.
