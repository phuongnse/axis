# D120. Axis is AGPL licensed, with a commercial licence

Status: **Agreed**

The owner agreed these points on 2026-10-31. Axis is meant to be open source, but the repository
had no licence, so nobody had the right to use, change or share it. Axis is a business that sells
a SaaS, as [D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) agrees, so its
licence must keep the code open without letting another company run a closed copy of it as a
competing service.

- **The platform is AGPL-3.0-only:** everything in the repository, except the parts below, is
  under the GNU Affero General Public License, version 3 only, in `LICENSE` at the root. Whoever
  offers a changed Axis to users over a network must offer those users its source.
- **A commercial licence is sold beside it:** the owner licenses Axis on other terms to a customer
  that cannot accept the AGPL, such as a large organization, a bank or a government installing it
  in phase 3 of [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md), with
  support.
- **What others build on stays free to license:**
  - `src/Axis.Extensions`, which extension packages reference, and the widget SDK
    `@axis/widgets-sdk` _(planned for M22)_, are under the MIT licence, so a package or a widget
    may have any licence.
  - `samples/`, the sample applications, packages, modules and templates, is under the MIT
    licence, so anyone may start from it.
  - Each of them holds its own `LICENSE` file.
- **An application belongs to its author:** an application's own resources, and a business's
  customization layer, are the business's, whatever licence the platform has.
- **Contributions:** the owner holds the copyright of all the code today. Before the first
  contribution from someone else is merged, contributors sign a contributor licence agreement that
  lets the owner keep selling the commercial licence.
- **NexKit** stays under the MIT licence.

_Why:_ a permissive licence would let a larger company host Axis and compete with its own code. A
licence that is not open source would cost the trust of the developers and the public bodies that
Axis wants later. The AGPL keeps Axis open and makes a closed hosted copy impossible, and a
commercial licence serves those who cannot use the AGPL, which is also income. The SDKs and the
samples are what others build on, so they take the MIT licence and bind nobody. Choosing now is
cheap, because the owner holds every copyright. The cost is that some companies avoid the AGPL
until they buy the commercial licence, and that outside contributions need an agreement first.
