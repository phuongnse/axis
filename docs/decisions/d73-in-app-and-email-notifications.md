# D73. In-app and email notifications

Status: **Agreed**

## Context

People must hear about work that waits for them and about their own requests, or processes stall, as
[human work](../domain/knowledge.md#human-work) says. Email needs an address and a language for each
user, while users live in the identity provider of
[D23](d23-sign-in-through-oidc-and-the-bff-session.md), not in Axis.

## Decision

- **A `notify` operation** sends text keys with placeholder values to users, roles or addresses, in the
  site, by email or both.
- **The compiler checks messages:** each placeholder has a value in every locale, and no value reads a
  `sensitive` field.
- **In-app notifications are stored in the step's transaction,** and a role recipient is checked when
  read.
- **Email goes through an outbox:** rendered in the step's transaction, sent after the commit at least
  once, under the limit of [D72](d72-a-delivery-limit-and-dead-letters.md).
- **Email to a role is a compile error,** because Axis has no directory to expand a role.
- **Axis keeps a profile of each user who signs in,** with the email and locale from the tenant's
  claims. It is not a directory.
- **MailKit sends email,** which adds to [D3](d3-technology-stack.md).

The detail is in [notifications](../reference/notifications.md).

## Consequences

- A rolled back step sends nothing, and a crash after the commit still sends.
- A message cannot break or leak a sensitive value at run time.
- A user who never signed in gets no email.

## Alternatives

- **A user directory in Axis.** A profile kept at sign-in gives an address and a locale without one.
