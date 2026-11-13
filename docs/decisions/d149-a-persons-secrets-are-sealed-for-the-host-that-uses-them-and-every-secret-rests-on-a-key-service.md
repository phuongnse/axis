# D149. A person's secrets are sealed for the host that uses them, and every secret rests on a key service

Status: **Agreed**

## Context

[D119](d119-ai-authoring-in-studio.md) and
[D125](d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) let each author connect their own
AI account, whose key runs the requests they send. Every stored secret is encrypted with one
installation key, see [D42](d42-tenant-directory-in-the-platform-database.md) and
[D87](d87-tenant-setting-commands-and-key-rotation.md). The server, the worker, the admin tool and the
agent host all hold that key. So whoever runs the hosts can decrypt every secret, and so can the server
that faces the internet, which never uses an AI key.

## Decision

D42's installation key still encrypts the installation's own secrets, the tenant's own model key
included. This decision adds where that key rests, and how a person's own secret is kept.

- **Write-only.** A secret that a person gives, such as their AI key, is sealed in the browser with the
  public key of the host that uses it, by HPKE (RFC 9180), before it is sent. The server stores the
  sealed value and cannot open it. No route answers it, and a person sees its last four characters only.
- **Opened only where it is used.** Only that host holds the private key, the agent host for an AI key,
  and it opens the value in memory for the call that needs it. The server, the worker and the admin tool
  cannot.
- **A key service as the root of trust.** In SaaS the hosts' private keys and the installation key are
  wrapped by the cloud's key service (AWS KMS, Azure Key Vault, Google Cloud KMS or HashiCorp Vault).
  Its policy lets only each host's own identity unwrap its key, and it logs every unwrap, so no person
  decrypts a secret unseen. On premises the customer chooses a key file, as before, or their own key
  service. Every secret of the installation rests on it, not only a person's, so there is one mechanism.
- **Each secret its own data key.** This is envelope encryption: removing a secret destroys its data
  key, so no copy, backup included, opens again.
- **One purpose.** An AI key goes only to its provider's API host, and never into a prompt, a log, a
  trace, problem details or a backup in clear.
- **The person sees its use.** The account page shows when the key was last used and by which request,
  and adding, changing and removing it write audit records. Axis advises a key made for Axis alone, with
  a spending limit at the provider, which the person can revoke there at any time.
- **Said in public.** The docs say which hosts can open which secret and which cannot, and the threat
  model lists the threats with their tests. The limit is said too: code that uses a key touches it while
  it uses it, which signed images and the review of the agent host's changes guard.

The rules are in [key policy](../reference/keys-and-data-protection.md#key-policy),
[which host opens which secret](../reference/keys-and-data-protection.md#which-host-opens-which-secret),
[secrets in the platform database](../reference/storage.md#secrets-in-the-platform-database) and
[stored secrets and key ring](../reference/threat-model-installation.md#stored-secrets-and-key-ring).

## Consequences

- Whoever runs Axis, and the server, cannot read a person's AI key.
- A key service unwrap is logged, so a person who decrypts a secret leaves a trace.
- In SaaS a host cannot start without its key service.
- A lost host private key means that people set their keys again.
- On premises, a key file keeps the trust in whoever holds the file.
- Code that uses a key still touches it, so the agent host's own changes and its image need care.

## Alternatives

- **Keep the one installation key.** Every host and its operator could open every secret, the server
  that faces the internet among them.
- **Keep the key in the browser only.** A cross-site scripting flaw can read it, and a durable request
  cannot use it while the author is away.
- **A hardware security module on every host.** Too heavy for a small installation.
- **OAuth with the provider.** Not every provider offers it.
