# Keys and data protection

This page states Axis's key policy, lists every key, algorithm and certificate it uses, and sorts its data into protection levels. Each topic keeps its details on its own page, and this page links to them. [Security](security.md) covers sign-in, the session and machine callers. [Platform database](storage.md#platform-database) covers how secrets are stored, and [tenant setting commands](tenant-setting-commands.md) covers the commands that change them. The session design is in [D84](../decisions/d84-asvs-level-2-and-a-server-side-session.md), and the key rotation is in [D87](../decisions/d87-tenant-setting-commands-and-key-rotation.md). [ASVS](asvs.md) lists the requirements this page meets.

## Key policy

The policy follows the lifecycle terms of NIST SP 800-57. It covers each key from its creation to its destruction.

- **Generation.** The operator makes the installation key from 32 random bytes, for example with `openssl rand -base64 32`. Axis makes database passwords the same way. The operator makes each client key as an EC P-256 key in PEM form.
- **Storage.** A secret comes from a file in `/run/secrets` or from the platform's secret store, never from a committed file. Axis keeps each tenant secret encrypted in the platform tables. It keeps no key in code, images or logs.
- **Holders.** The server, the worker and the admin tool of one installation share its installation key, and every replica holds the same value. A client key's private half stays with Axis. Only its public half goes to the identity provider. A webhook secret is shared by exactly two parties.
- **Use.** Each key has one purpose. The installation key only encrypts stored secrets and data protection keys. A client key only signs client assertions. The [cryptographic inventory](#cryptographic-inventory) lists where each key may and may not be used.
- **Cryptoperiod.** Axis rotates the installation key at least once a year, and at once when it may have leaked. The framework makes a new data protection key every 90 days. No other key has a set period yet.
- **Rotation.** Each key rotates on its own path.
  - The installation key rotates with [`platform rekey`](tenant-setting-commands.md#installation-key), without signing anyone out.
  - A client key rotates by adding the new key and then retiring the old one, see [sign-in settings](tenant-setting-commands.md#sign-in-settings).
  - Database passwords rotate with [`tenant rotate-passwords`](tenant-setting-commands.md#database-passwords).
- **Compromise.** Act at once when a key may have leaked. Rekey does not change the value of a secret, so the secrets change too.
  - Rotate the installation key with `platform rekey`.
  - Rotate the database passwords with `tenant rotate-passwords`.
  - Add a new client key, and retire the old one.
  - Set new values with `tenant email set --password` and `tenant binding set --secret`. No command changes the OIDC client secret of a tenant, which `tenant create` and `tenant restore` set.
  - End every session with `tenant session end <id> --all`.
- **Destruction.** Remove `Platform:PreviousSecretKey` from every host once rekey has run. Destroy the old installation key once no host holds it. Retire a client key once the identity provider no longer needs it. The server never deletes a data protection key.

## Cryptographic inventory

Each row is one key, algorithm or certificate. A _(planned for Mx)_ marker marks what is not built yet.

| Item | Algorithm and size | Used for | Never used for | Lifecycle |
| --- | --- | --- | --- | --- |
| **Installation key** `Platform:SecretKey` | AES-256-GCM, with a 32-byte key, a 12-byte random nonce and a 16-byte tag | Encrypting tenant secrets and data protection keys, see [platform database](storage.md#platform-database) | Records, files, backups, signatures and any value that is not a secret | Set once by the operator. Replaced with `platform rekey`, see [installation key](tenant-setting-commands.md#installation-key). |
| **Data protection keys**, in `platform.data_protection_keys` | AES-256-CBC with HMAC-SHA256, the ASP.NET Core default | Protecting the session cookie and the OIDC `state` of a sign-in, read back at the callback, see [sign-in and the session](security.md#sign-in-and-the-session) | Anything beyond the session cookie and the sign-in `state`. Records, files and secrets never use these keys | Each key is stored encrypted by the installation key. The framework makes a new key every 90 days. The server never deletes one. |
| **Session id** | 256 random bits, stored only as a SHA-256 hash | Naming a signed-in browser session | Storing the id in clear, or logging it | Ends at sign-out, at expiry or with `tenant session end`. The worker deletes expired rows. |
| **Client key** of a `fapi2` tenant | EC P-256 with ES256. The key id is the RFC 7638 thumbprint of the key, with SHA-256 | Signing the `private_key_jwt` client assertions at the identity provider | Anything else. RSA keys are refused | Given as a PEM by the operator and stored encrypted by the installation key. A tenant holds at most two. Changed with `tenant oidc set --client-key` and `--retire-client-key`. |
| **Sign-in DPoP key** | EC P-256 with ES256 | Proving the sign-in token request, see [FAPI 2.0 profile](security.md#fapi-20-profile) | Any other request | Made for each sign-in and dropped after it. It is never stored. |
| **Caller token keys** | The identity provider's published keys (JWKS). Signatures RS, PS and ES at 256 to 512 bits | Checking the signature of a caller's bearer token, see [machine callers](security.md#machine-callers) | Signing anything. Axis holds no private key here | The identity provider owns them. |
| **DPoP proof keys** of callers | ES256, or PS256 with an RSA key of at least 2048 bits. The `ath` hash is SHA-256 | Checking each caller's proof, which carries the public key | Holding a private key. A proof with a private key is refused | Made by the caller for each request. A proof must be made within 60 seconds of the server's clock. |
| **Identity provider keys** | Set by the identity provider | Signing the identity provider's own tokens | Anything Axis signs | Delegated to the identity provider. |
| **Tenant database passwords** | 32 random bytes, shown as 64 lowercase hex characters | Logging in to the tenant's runtime and schema database roles | Anything other than that login | Made at `tenant create` and by `tenant rotate-passwords`. Stored encrypted by the installation key. |
| **Realm client secrets** | 32 random bytes, shown as 64 lowercase hex characters | The secrets of the three clients of a tenant's realm: the platform client `axis`, which is the tenant's OIDC client secret, `axis-activation` and `axis-users`, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step) | Anything other than signing in as those clients | Made at `tenant create`. Stored encrypted by the installation key. A rerun keeps them. |
| **OIDC client secret, SMTP password and `secret` bindings** | Given by the operator and stored encrypted by the installation key | Calling the identity provider, the mail server and a connector | Anything else. Never printed | The OIDC client secret is set by `tenant create` and `tenant restore`, and no command replaces it. The SMTP password and `secret` bindings are replaced with `tenant email set --password` and `tenant binding set --secret`. The identity provider holds a copy of the SMTP password in the tenant's realm, so the realm can send a password reset link. The installation key does not encrypt that copy, because it lives in the identity provider's own database. The admin API shows it masked, and Axis never prints it. |
| **Webhook signing secret** | HMAC-SHA256, in the Standard Webhooks format | Signing outgoing webhook calls, see [webhook delivery](outbound-webhooks.md#webhook-delivery) | Anything else | Shared by two parties. The `secret` binding may hold two secrets during a change. A `whsec_` secret is the base64 of the key, any other secret is the key's UTF-8 bytes. |
| **Connector OAuth2 client secret** | Stored as a `secret` binding | Asking the authorization server for an access token, see [OAuth2 auth](integration-resources.md#oauth2-auth) | Anything else | Replaced with `tenant binding set --secret`. |
| **Proxy certificate and key** | A publicly trusted certificate, with TLS 1.2 and 1.3 and only the strong cipher suites | Serving HTTPS to clients, see [TLS](installation.md#tls) | Anything else. The Axis hosts hold no TLS key | The operator's proxy holds the key and renews the certificate. The Compose example reads it from `/certs`. |
| **Server certificates that Axis checks** | PostgreSQL under `VerifyFull`. The identity provider, SMTP and OTLP certificates | Verifying the servers that Axis connects to | Client certificates. Axis presents none | PostgreSQL uses `Root Certificate` for a private authority. The other servers use the system trust store. |
| **Hashes** | SHA-256 for resource versions, release content, fingerprints, file contents, names shortened to 16 hex characters and the Studio write lock key. SHA-1 only to name module resources as version 5 UUIDs | Identity, integrity and naming | Hiding a secret or a password. Axis hashes no password | No key. A hash change is a code change, see [crypto agility](#crypto-agility). |
| **Tenant data key** and **lookup key** _(planned for M27)_ | AES-256-GCM for the data key, HMAC-SHA256 for the lookup key, 32 bytes each | Encrypting the values of `sensitive` fields, bound to the tenant, entity, record and field; the lookup key makes the exact lookup of a field that declares one, see [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) | Secrets, files and any other value | Made with the tenant and stored encrypted by the installation key. The keys and their store are built, and the use of them is planned. `tenant data-key rotate` adds a numbered data key and lookup key, re-encrypts the tenant's values and recomputes their lookup hashes, and `tenant data-key retire` deletes the old keys. See [data encryption](data-encryption.md#tenant-keys). |
| **Backup key** | AES-256-GCM, in chunks of 1 MiB | Encrypting a tenant backup archive, see [encrypted backups](data-encryption.md#encrypted-backups) | Anything else. It is never the installation key | Given to the admin tool by the operator in a file, `--backup-key-file`, and kept outside the installation. A lost key loses its archives. |
| **Image signatures** _(planned for M32)_ | cosign keyless signing through the release workflow's GitHub identity | Signing Axis's images and their SBOMs, see [D107](../decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) | Anything else | One short-lived certificate per signing, recorded in the public transparency log. |
| **Random values** | The system's random generator, `RandomNumberGenerator`, for nonces, session ids, passwords and the backup name suffix | Values that must not be guessed | None beyond the uses listed | No key. |

### Crypto agility

- **One cipher for stored secrets.** Every stored secret goes through one class, `TenantSecretCipher`. A change of cipher starts in that one place.
- **Rekey moves every value.** `platform rekey` decrypts and re-encrypts every tenant secret and data protection key in one transaction. Hosts accept the previous installation key during the change, so no one is signed out.
- **New keys may use new algorithms.** The framework writes each data protection key's algorithm into the key. A new key can use a new algorithm while the old keys still decrypt.
- **Stored values name no algorithm.** An encrypted value holds a nonce, the ciphertext and a tag. Nothing in it names its form. So a change of form is a code change. A release reads the old form and writes the new one, and rekey moves every value in one transaction.
- **Token algorithms are lists.** The token checks take their algorithms from one list each in the server code, `EndpointCaller.Algorithms` and `DpopProof.Algorithms`. Changing a list is a code change.
- **Passwords and secrets change through commands.** `tenant rotate-passwords`, `tenant email set` and `tenant binding set` replace them. See [tenant setting commands](tenant-setting-commands.md).
- **No post-quantum algorithm.** Axis uses no post-quantum algorithm. Adopting one would be a code change to the cipher and to the token checks.

## Data classification

Axis sorts its data into five levels, from the most protected to the least. Each level sets the rules for its data. Each rule names the control that meets it and the test that checks the control.

Axis encrypts only the secrets, and only in the platform tables. For all other data, the operator's PostgreSQL or disk encryption protects data at rest. Axis also encrypts tenant backup archives, see [encrypted backups](data-encryption.md#encrypted-backups). _(planned for M27)_ It also encrypts the values of `sensitive` fields, see [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md).

| Level | What it holds |
| --- | --- |
| **Secret** | Credentials and key material. Also values that are only encoded, such as the base64 installation key and the JWT payloads of client assertions. |
| **Restricted** | Fields marked `sensitive`, file contents, personal data of users, and backup archives. |
| **Business** | Other record fields, process state, tasks, notifications and audit records. |
| **Internal** | Configuration, releases, names, logs and telemetry. |
| **Public** | The static files of the web app and the status answer of `/health`. |

### Secret

- **Encryption at rest.** Each stored secret is encrypted with AES-256-GCM under the installation key, see the [cryptographic inventory](#cryptographic-inventory). Test: `TenantSecretCipherTests`.
- **Encryption in transit.** Every connection uses TLS. PostgreSQL is checked under `VerifyFull`, see [TLS](installation.md#tls). Test: `DatabaseTlsRequirementTests`.
- **Integrity.** The GCM tag and the bound tenant and field catch any change. A value copied to another tenant or field does not decrypt. Test: `TenantSecretCipherTests`.
- **Retention.** A secret is kept until an operator replaces or removes it. A session id hash is kept until its session ends. Test: `WorkerSessionCleanupTests`.
- **Logging.** A secret never appears in a log, a span, an API answer, problem details or a backup. Test: `TraceCorrelationTests`, `SecretValueTests` and `AdminTenantBackupTests`.
- **Access to logs.** No log holds a secret, so reading a log reveals none. Test: `TraceCorrelationTests`.
- **Access control.** Only the server, the worker and the admin tool read secrets, each under its own database role. The worker has no schema connection. Test: `DatabaseRoleTests`.
- **Privacy.** A secret holds no personal data. Test: none.

### Restricted

- **Encryption at rest.** Axis encrypts none of this data. The operator's PostgreSQL or disk encryption protects it. A `sensitive` flag encrypts nothing. Test: none, because the control is the operator's. A backup archive is encrypted with AES-256-GCM under the operator's backup key, see [encrypted backups](data-encryption.md#encrypted-backups). _(planned for M27)_ A `sensitive` field's value is encrypted with AES-256-GCM under its tenant's data key, see [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md).
- **Encryption in transit.** Every connection uses TLS, see [TLS](installation.md#tls). Test: `DatabaseTlsRequirementTests`.
- **Integrity.** A file's SHA-256 is stored with it. Each change to a record writes an audit record in the same transaction, see [Business](#business). Test: `FileUploadTests` for the file hash and `AuditRecordTests` for the audit record.
- **Retention.** Axis sets no period for records or user profiles. A pending file is purged after 24 hours, and a deleted file is purged by the worker's sweep. An attached file lasts as long as its record. The operator keeps and removes backup archives. Test: `FileCleanUpTests`.
- **Logging.** These values never appear in a log, a span or telemetry. Test: `TraceCorrelationTests`.
- **Access to logs.** Logs and telemetry hold no field value, user id or address, so reading them reveals none of this data. Who reads the logs is the operator's choice, see [logs](../architecture.md#logs). Test: `TraceCorrelationTests`.
- **Access control.** Policies and field grants decide who reads and writes each field. A write to a field that no grant covers is refused, and a read leaves the field out. Test: `RecordFieldPolicyTests`.
- **Masking in process inspection.** A `sensitive` field's input is hidden in process inspection. The flag hides nothing else. The record API still shows the field to users whose grants cover it, and the database holds the value in clear. See [sensitive fields](roles-and-policies.md#sensitive-fields). Test: `ProcessInstanceDetailTests`.
- **Privacy.** Axis names no regulation. The application owner marks personal data `sensitive`, and sets retention and access in the application to match the rules that apply. Test: none. _(planned for M27)_ The owner marks sensitive personal data `sensitive`, such as health, finances, identity numbers and biometrics. Basic personal data, such as a name or an email, may stay in a Business field, so it can be searched. New history rows keep the user id and no display name, and session audit records keep a shortened client address, see [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md).

### Business

- **Encryption at rest.** The operator's PostgreSQL or disk encryption protects these records. Axis encrypts no column. Test: none, because the control is the operator's.
- **Encryption in transit.** Every connection uses TLS, see [TLS](installation.md#tls). Test: `DatabaseTlsRequirementTests`.
- **Integrity.** Each audit record is written in the same transaction as the change it records. The database rejects any update, delete or truncate of the audit table. Test: `AuditRecordTests`.
- **Retention.** The database rejects any delete of an audit record. Notifications are deleted after `Notifications:RetentionDays`, 90 days by default. Other records last as long as the application keeps them. Test: `NotificationRetentionTests`.
- **Logging.** Field values never appear in a log, a span or telemetry. Test: `TraceCorrelationTests`.
- **Access to logs.** As for Restricted. Test: `TraceCorrelationTests`.
- **Access control.** Policies and field grants, as for Restricted. Test: `RecordFieldPolicyTests`.
- **Privacy.** Personal data does not belong in a Business field. The owner marks such a field `sensitive`, and then treats it as Restricted. Test: none.

### Internal

- **Encryption at rest.** The operator's PostgreSQL or disk encryption protects the configuration. Test: none, because the control is the operator's.
- **Encryption in transit.** Every connection uses TLS. Outside Development, telemetry export must use `https`, see [telemetry export](host-settings.md#telemetry-export). Test: `ServerTelemetryTests`.
- **Integrity.** A release's content hash is SHA-256. The database rejects any change to the release history. Test: `ReleaseHistoryTests`.
- **Retention.** The database rejects any change to the release history, so no Axis command removes it. Logs and telemetry last as long as the operator's collector keeps them. Test: `ReleaseHistoryTests`.
- **Logging.** Logs and telemetry name things and hold no values, see [what telemetry never holds](../architecture.md#what-telemetry-never-holds). Test: `TraceCorrelationTests`.
- **Access to logs.** The operator protects the logs and telemetry. Their content needs no data grant. Test: none.
- **Access control.** Studio edits an application only for users with `Studio:AuthorRole`, and publishes only with `Studio:PublisherRole`, see [D47](../decisions/d47-publishing-from-studio.md). Test: `StudioTextWriteTests`.
- **Privacy.** Names reach logs and telemetry, so a name must not hold personal data. Test: none.

### Public

- **Encryption at rest.** None is needed, because the content is public. Test: none.
- **Encryption in transit.** Clients reach the files over TLS, like all client traffic, see [TLS](installation.md#tls). Test: none.
- **Integrity.** The static files ship inside the image. Test: none.
- **Retention.** The files last as long as their image. `/health` stores nothing. Test: none.
- **Logging.** No rule beyond the Internal rules. Test: none.
- **Access to logs.** No rule. Test: none.
- **Access control.** Anyone may read these. `/health` answers only a status, and no directory listing is served. Test: `HealthEndpointTests` and `StaticFolderAndTraceTests`.
- **Privacy.** None. Test: none.

A change that adds a key, an algorithm, a certificate or a kind of stored data updates this page in the same change.
