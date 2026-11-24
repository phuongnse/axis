# Keys and data protection

This page states Axis's key policy, lists every key, algorithm and certificate it uses, and sorts its data into protection levels. Each topic keeps its details on its own page, and this page links to them. [Security](security.md) covers sign-in, the session and machine callers. [Platform database](storage.md#platform-database) covers how secrets are stored, [tenant setting commands](tenant-setting-commands.md) covers the commands that change them, and [data encryption](data-encryption.md) covers sensitive fields and backups. [ASVS](asvs.md) lists the requirements this page meets.

## Key policy

The policy follows the lifecycle terms of NIST SP 800-57. It covers each key from its creation to its destruction.

- **Generation.** The operator makes the installation key from 32 random bytes, for example with `openssl rand -base64 32`, and wraps it with `platform key wrap` before any host reads it. On premises the operator also makes the key file of the key service the same way. Axis makes database passwords the same way. The operator makes each client key as an EC P-256 key in PEM form. The operator makes the key pair of the agent host the same way, as an EC P-256 private key in PEM form for `Ai:HostKeyFile`.
- **Storage.** A secret comes from a file in `/run/secrets` or from the platform's secret store, never from a committed file. Axis keeps each tenant secret encrypted in the platform tables. It keeps no key in code, images or logs.
- **Holders.** The server, the worker, the admin tool and the agent host of one installation share its installation key, and every replica holds the same value. A client key's private half stays with Axis. Only its public half goes to the identity provider. Only the agent host holds the private half of its key pair. The server reads the public half that the agent host publishes and answers it at `GET /api/agent-host/key`, and it holds nothing that opens a value sealed with it. A webhook secret is shared by exactly two parties.
- **Key service.** In SaaS AWS KMS (`aws-kms`) wraps the private key of each host that opens a sealed secret and the installation key. Its key policy lets only each host's own identity unwrap its key, and CloudTrail logs every unwrap. The operator writes that policy, and Axis cannot check it. On premises a key file (`file`) wraps them. Outside Development and Testing a host that reads the installation key or the agent host's private key does not start without a key service, and refuses a key that is not wrapped. Development and Testing accept plain keys.
- **Other key services.** _(planned for M35)_ Azure Key Vault, Google Cloud KMS and HashiCorp Vault wrap the same keys, with the same policy and the same log.
- **Sealed secrets.** _(planned for M35)_ A secret that a person gives is sealed in the browser for the host that uses it, with a data key of its own, and only that host opens it, in memory, for the call that needs it, see [D149](../decisions/d149-a-persons-secrets-are-sealed-for-the-host-that-uses-them-and-every-secret-rests-on-a-key-service.md). A person's AI key is the only AI key that Axis stores.
- **Use.** Each key has one purpose. The installation key only encrypts stored secrets and data protection keys. A client key only signs client assertions. The [cryptographic inventory](#cryptographic-inventory) lists where each key may and may not be used.
- **Cryptoperiod.** Axis rotates the installation key at least once a year, and at once when it may have leaked. The framework makes a new data protection key every 90 days. No other key has a set period.
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

The inventory lists every key, algorithm and certificate that Axis uses, one to a row, with what it may and may not be used for.

| Item | Algorithm and size | Used for | Never used for | Lifecycle |
| --- | --- | --- | --- | --- |
| **Installation key** `Platform:SecretKey` | AES-256-GCM, with a 32-byte key, a 12-byte random nonce and a 16-byte tag | Encrypting tenant secrets and data protection keys, see [platform database](storage.md#platform-database) | Records, files, backups, signatures and any value that is not a secret | Set once by the operator. Replaced with `platform rekey`, see [installation key](tenant-setting-commands.md#installation-key). |
| **Data protection keys**, in `platform.data_protection_keys` | AES-256-CBC with HMAC-SHA256, the ASP.NET Core default | Protecting the session cookie and the OIDC `state` of a sign-in, read back at the callback, see [sign-in and the session](security.md#sign-in-and-the-session) | Anything beyond the session cookie and the sign-in `state`. Records, files and secrets never use these keys | Each key is stored encrypted by the installation key. The framework makes a new key every 90 days. The server never deletes one. |
| **Session id** | 256 random bits, stored only as a SHA-256 hash | Naming a signed-in browser session | Storing the id in clear, or logging it | Ends at sign-out, at expiry or with `tenant session end`. The worker deletes expired rows. |
| **Client key** of a `fapi2` tenant | EC P-256 with ES256. The key id is the RFC 7638 thumbprint of the key, with SHA-256 | Signing the `private_key_jwt` client assertions at the identity provider | Anything else. RSA keys are refused | Given as a PEM by the operator and stored encrypted by the installation key. A tenant holds at most two. Changed with `tenant oidc set --client-key` and `--retire-client-key`. |
| **Sign-in DPoP key** | EC P-256 with ES256 | Proving the sign-in token request, see [FAPI 2.0 profile](fapi2-profile.md) | Any other request | Made for each sign-in and dropped after it. It is never stored. |
| **Caller token keys** | The identity provider's published keys (JWKS). Signatures RS, PS and ES at 256 to 512 bits | Checking the signature of a caller's bearer token, see [machine callers](security.md#machine-callers) | Signing anything. Axis holds no private key here | The identity provider owns them. |
| **DPoP proof keys** of callers | ES256, or PS256 with an RSA key of at least 2048 bits. The `ath` hash is SHA-256 | Checking each caller's proof, which carries the public key | Holding a private key. A proof with a private key is refused | Made by the caller for each request. A proof must be made within 60 seconds of the server's clock. |
| **Identity provider keys** | Set by the identity provider | Signing the identity provider's own tokens | Anything Axis signs | Delegated to the identity provider. |
| **Tenant database passwords** | 32 random bytes, shown as 64 lowercase hex characters | Logging in to the tenant's runtime and schema database roles | Anything other than that login | Made at `tenant create` and by `tenant rotate-passwords`. Stored encrypted by the installation key. |
| **Realm client secrets** | 32 random bytes, shown as 64 lowercase hex characters | The secrets of the four clients that `tenant create` makes in a tenant's realm: the platform client `axis`, which is the tenant's OIDC client secret, `axis-activation`, `axis-users` and the agent host's `axis-agent`, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). A package runner's client has its own row. | Anything other than signing in as those clients | Made at `tenant create`. Stored encrypted by the installation key. A rerun keeps them. |
| **Package runner client secret** | 32 random bytes, shown as 64 lowercase hex characters | Signing in as the runner's confidential client `runner-<id>` in the tenant's realm with the client credentials grant, see [customer-hosted runners](package-runners.md#customer-hosted-runners) | Anything else. Axis does not store it, and no route answers it again | Made by `POST /api/tenant/package-runners`, which answers it once, see [package runners](user-administration.md#package-runners). The identity provider holds it. Revoking the runner deletes its client. |
| **OIDC client secret, SMTP password and `secret` bindings** | Given by the operator and stored encrypted by the installation key | Calling the identity provider, the mail server and a connector | Anything else. Never printed | The OIDC client secret is set by `tenant create` and `tenant restore`, and no command replaces it. The SMTP password and `secret` bindings are replaced with `tenant email set --password` and `tenant binding set --secret`. The identity provider holds a copy of the SMTP password in the tenant's realm, so the realm can send a password reset link. The installation key does not encrypt that copy, because it lives in the identity provider's own database. The admin API shows it masked, and Axis never prints it. |
| **Tenant model key** | Given by a tenant administrator and stored encrypted by the installation key, bound to the tenant and the field `model_key` | Calling the model provider for the tenant's own requests, see [a tenant's own key](model-bindings.md#a-tenants-own-key) | Anything else. Never printed and never answered by a route, which shows only its last four characters | Set, changed and removed with `PUT` and `DELETE /api/tenant/model-key`, see [the tenant's model key](user-administration.md#the-tenants-model-key). `platform rekey` encrypts it again. No other tenant's secret decrypts it. |
| **Webhook signing secret** | HMAC-SHA256, in the Standard Webhooks format | Signing outgoing webhook calls, see [webhook delivery](outbound-webhooks.md#webhook-delivery) | Anything else | Shared by two parties. The `secret` binding may hold two secrets during a change. A `whsec_` secret is the base64 of the key, any other secret is the key's UTF-8 bytes. |
| **Connector OAuth2 client secret** | Stored as a `secret` binding | Asking the authorization server for an access token, see [OAuth2 auth](integration-resources.md#oauth2-auth) | Anything else | Replaced with `tenant binding set --secret`. |
| **Proxy certificate and key** | A publicly trusted certificate, with TLS 1.2 and 1.3 and only the strong cipher suites | Serving HTTPS to clients, see [TLS](installation.md#tls) | Anything else. The Axis hosts hold no TLS key | The operator's proxy holds the key and renews the certificate. The Compose example reads it from `/certs`. |
| **Server certificates that Axis checks** | PostgreSQL under `VerifyFull`. The identity provider, SMTP and OTLP certificates | Verifying the servers that Axis connects to | Client certificates. Axis presents none | PostgreSQL uses `Root Certificate` for a private authority. The other servers use the system trust store. |
| **Hashes** | SHA-256 for resource versions, release content, fingerprints, file contents, names shortened to 16 hex characters and the Studio write lock key. SHA-1 only to name module resources as version 5 UUIDs, and HMAC-SHA1 for the TOTP codes of the tenant realm, which the identity provider computes | Identity, integrity and naming | Hiding a secret or a password. Axis hashes no password | No key. A hash change is a code change, see [crypto agility](#crypto-agility). |
| **Tenant data key** and **lookup key** | AES-256-GCM for the data key, HMAC-SHA256 for the lookup key, 32 bytes each | Encrypting the values of `sensitive` fields, bound to the tenant, entity, record and field. The lookup key makes the exact lookup of a field that declares one, see [exact lookup](data-encryption.md#exact-lookup). Also the refresh token of an [authoring request](authoring-requests.md#acting-for-the-author), bound to the tenant and the request | Secrets other than that token, files and any other value | Made with the tenant and stored encrypted by the installation key. `tenant data-key rotate` adds a numbered data key and lookup key, re-encrypts the tenant's values and recomputes their lookup hashes, and `tenant data-key retire` deletes the old keys. See [tenant keys](data-encryption.md#tenant-keys). |
| **Backup key** | AES-256-GCM, in chunks of 1 MiB | Encrypting a tenant backup archive, see [encrypted backups](data-encryption.md#encrypted-backups) | Anything else. It is never the installation key | Outside a cell: given to the admin tool by the operator in a file, `--backup-key-file`, and kept outside the installation. In a cell: one 32-byte key for each tenant, made by `tenant create` and stored in `platform.tenant_backup_keys`, wrapped by the cell's backup key and bound to the tenant id; `tenant backup` and `tenant restore` read it and refuse `--backup-key-file`; a restore makes a new key for the restored tenant. A lost key loses its archives. |
| **Key file of the key service** `KeyService:KeyFile` | AES-256-GCM, 32 bytes | Wrapping the installation key and the agent host's private key, each bound to its purpose, with `platform key wrap`. The hosts unwrap them at start | Anything else. It is never the installation key and never wraps a tenant secret | A file of 32 random bytes in base64 that the operator makes, for example with `openssl rand -base64 32`, and keeps outside the installation. A missing, unreadable or malformed file, and a value that does not unwrap with it, stop the start with a message that names the setting and never a value or a path. A lost key file loses the wrapped keys, so the operator keeps a copy. No command rotates it. |
| **Cell backup key** | AES-256-GCM, 32 bytes | Wrapping the backup keys of the tenants of a cell in `platform.tenant_backup_keys` | Anything else. It is never the installation key and never wraps another secret. `platform rekey` does not touch what it wraps | A file that `Cell:BackupKeyFile` names, which comes from the cloud provider's key service. It is never in the database or in an archive. `tenant create`, `tenant backup` and `tenant restore` read it in a cell and stop with exit code `2` when it is missing. A lost key loses every tenant key of the cell and so every archive. No command rotates it. |
| **Host key pair** of the agent host | HPKE (RFC 9180) with DHKEM(P-256, HKDF-SHA256), HKDF-SHA256 and AES-256-GCM | Opening the secrets that people seal for the agent host, see [which host opens which secret](#which-host-opens-which-secret) | Anything the installation key encrypts | The private half never leaves the agent host. The operator makes it as an EC P-256 PEM for `Ai:HostKeyFile`, and every replica of the agent host holds the same file. With AI authoring on under the `directory` source, the agent host publishes the public half and its key id, the RFC 7638 thumbprint, in `platform.host_keys` at start, and a signed-in person reads it at `GET /api/agent-host/key`. With `KeyService:Kind` set, the file holds the private half wrapped by `platform key wrap --purpose agent-host-key`, and the agent host unwraps it at start. A key service that wraps the private half follows the key service rule of the [key policy](#key-policy). A lost private key means people set their keys again. |
| **A person's AI key** _(planned for M35)_ | Sealed in the browser for the agent host, with a data key of its own | Calling the model provider for the person's own requests | Anything else. It goes only to its provider's API host, is never in a prompt, a log, a trace, problem details or a backup in clear, and is never answered except its last four characters | Added, changed and removed by the person. Removing it destroys its data key. |
| **Key service key** | Held by AWS KMS, the AWS KMS key that `KeyService:KeyId` names. On premises it is the key file above | Wrapping the host private keys and the installation key | Anything else. It never leaves the key service | AWS KMS owns it, and CloudTrail logs every unwrap. The operator's key policy limits unwrap to each host's identity. |
| **Image signatures** _(planned for M32)_ | cosign keyless signing through the release workflow's GitHub identity | Signing Axis's images and their SBOMs | Anything else | One short-lived certificate per signing, recorded in the public transparency log. |
| **Random values** | The system's random generator, `RandomNumberGenerator`, for nonces, session ids, key bytes, passwords, client secrets, the secret of a package runner, the backup name suffix, the verification token of a [tenant host request](tenant-lifecycle.md#hosts) and the [weekly restore check](tenant-backup-and-restore.md#weekly-restore-check)'s pick of a tenant | Values that must not be guessed | None beyond the uses listed | No key. |

### The agent host's public key

A signed-in person reads the public half of the agent host's key pair from the server, which answers the row that the agent host published in `platform.host_keys`. A value sealed with that key opens only in the agent host, because the server, the worker and the installation key hold no private half. Nothing vouches for the key except the agent host publishing it, see [threat I34](threat-model-installation.md).

Before anything seals with the key, the web app checks the answer. It refuses a suite other than kem 16, kdf 1 and aead 2, a key that is not an EC P-256 JWK, a point that Web Crypto does not import as a P-256 public key, and a key id that is not the key's RFC 7638 thumbprint. A missing key (404) is a normal state, and any other failure or refused key stops the seal.

| Route | What it does |
| --- | --- |
| `GET /api/agent-host/key` | Answers `{ keyId, kem, kdf, aead, publicKey }` with `kem` 16, `kdf` 1 and `aead` 2, the suite ids of RFC 9180, and `publicKey` as `{ kty, crv, x, y }`, an EC P-256 JWK. It answers a `401` with no session and a `404` when no key is published |

### Crypto agility

- **One cipher for stored secrets.** Every stored secret goes through one class, `TenantSecretCipher`. A change of cipher starts in that one place.
- **Rekey moves every value.** `platform rekey` decrypts and re-encrypts every tenant secret and data protection key in one transaction. Hosts accept the previous installation key during the change, so no one is signed out.
- **New keys may use new algorithms.** The framework writes each data protection key's algorithm into the key. A new key can use a new algorithm while the old keys still decrypt.
- **Stored values name no algorithm.** An encrypted value holds a nonce, the ciphertext and a tag. Nothing in it names its form. So a change of form is a code change. A release reads the old form and writes the new one, and rekey moves every value in one transaction.
- **Token algorithms are lists.** The token checks take their algorithms from one list each in the server code. `BearerCallerOptions.Algorithms` serves the bearer token checks of machine callers and of the cell operator, and `DpopProof.Algorithms` serves DPoP proofs. Changing a list is a code change.
- **Passwords and secrets change through commands.** `tenant rotate-passwords`, `tenant email set` and `tenant binding set` replace them. See [tenant setting commands](tenant-setting-commands.md).
- **No post-quantum algorithm.** Axis uses no post-quantum algorithm. Adopting one would be a code change to the cipher and to the token checks.

## Which host opens which secret

_(planned for M35)_

Each secret has the hosts that open it and the hosts that cannot, so a reader sees who can read a secret and who cannot, the operator included.

| Secret | Opened by | Cannot open |
| --- | --- | --- |
| A person's AI key | The agent host only, with its own private key, in memory, for the call that needs it | The server, the worker, the admin tool and the operator |
| The installation's own secrets | The server, the worker, the admin tool and the agent host, each by unwrapping the installation key at the cloud's or the customer's key service | A caller or a person with no host identity |

Code that uses a key touches it while it uses it. Signed images, see I5 in the [installation threat model](threat-model-installation.md), and the review of the agent host's changes guard that limit.

## Data classification

Axis sorts its data into five levels, from the most protected to the least. Each level sets the rules for its data, and each rule names the control that meets it and the test that checks the control.

Axis encrypts only the secrets, the values of `sensitive` fields and tenant backup archives. Secrets are encrypted in the platform tables, and sensitive values in the tenant tables, see [sensitive field values](data-encryption.md#sensitive-field-values). Backup archives are encrypted, see [encrypted backups](data-encryption.md#encrypted-backups). For all other data, the operator's PostgreSQL or disk encryption protects data at rest.

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
- **Access control.** Only the server, the worker, the admin tool and the agent host read secrets, each under its own database role. The worker has no schema connection. Test: `DatabaseRoleTests`.
- **Privacy.** A secret holds no personal data. Test: none.
- **Sealed secrets** _(planned for M35)_. A secret that a person gives follows [which host opens which secret](#which-host-opens-which-secret).

### Restricted

- **Encryption at rest.** The operator's PostgreSQL or disk encryption protects most of this data. Test: none, because the control is the operator's. A backup archive is encrypted with AES-256-GCM under the operator's backup key, see [encrypted backups](data-encryption.md#encrypted-backups). Test: `AdminEncryptedBackupTests`. A `sensitive` field's value is encrypted with AES-256-GCM under its tenant's data key, and stored as ciphertext in a `bytea` column, also in a child row, on every path that writes it: the record API, imports (the dry run and the commit), inbound endpoints, seeds, process steps, connector and package call outputs, task completion, the operator's call resolve and scenarios, see [tenant keys](data-encryption.md#tenant-keys). Test: `RecordSensitiveValueTests` for the record API, `RecordImportSensitiveValueTests` for imports and inbound endpoints, `StartupActivationTests` for seeds, `ProcessSensitiveValueTests` for process steps, `ConnectorCallSensitiveOutputTests` for connector call outputs, `ScenarioRunnerTests` for scenarios and `LeaveRequestReasonStorageTests` for the tenant database as a whole.
- **Encryption in transit.** Every connection uses TLS, see [TLS](installation.md#tls). Test: `DatabaseTlsRequirementTests`.
- **Integrity.** A file's SHA-256 is stored with it. Each change to a record writes an audit record in the same transaction, see [Business](#business). Test: `FileUploadTests` for the file hash and `AuditRecordTests` for the audit record.
- **Retention.** Axis sets no period for records or user profiles. A pending file is purged after 24 hours, and a deleted file is purged by the worker's sweep. An attached file lasts as long as its record. The operator keeps and removes backup archives. Test: `FileCleanUpTests`.
- **Logging.** These values never appear in a log, a span or telemetry. Test: `TraceCorrelationTests`.
- **Access to logs.** Logs and telemetry hold no field value or address, and no tenant user id except that of a user whose notification finds no email, see [email](notifications.md#email). A refused provisioning call names its operator caller, who is no tenant user. Who reads the logs is the operator's choice, see [logs](telemetry.md#logs). Test: `TraceCorrelationTests`.
- **Access control.** Policies and field grants decide who reads and writes each field. A write to a field that no grant covers is refused, and a read leaves the field out. Test: `RecordFieldPolicyTests`.
- **Masking in process inspection.** A `sensitive` field's input is hidden in process inspection, see [sensitive fields](roles-and-policies.md#sensitive-fields). The flag hides nothing else: the record API decrypts the value on a read and shows it to users whose grants cover the field. Test: `ProcessInstanceSensitiveFieldTests`.
- **Privacy.** Axis names no regulation. The application owner marks sensitive personal data `sensitive`, such as health, finances, identity numbers and biometrics, and sets retention and access in the application to match the rules that apply. Basic personal data, such as a name or an email, may stay in a Business field, so it can be searched. A history row keeps the user id and no display name, see [record audit](record-audit.md), and a session audit record keeps a shortened client address, see [audit records](audit-records-and-sequence-counters.md#audit-records-and-sequence-counters). Test: none.

### Business

- **Encryption at rest.** The operator's PostgreSQL or disk encryption protects these records. Axis encrypts no column. Test: none, because the control is the operator's.
- **Encryption in transit.** Every connection uses TLS, see [TLS](installation.md#tls). Test: `DatabaseTlsRequirementTests`.
- **Integrity.** Each audit record is written in the same transaction as the change it records. The database rejects any update, delete or truncate of the audit table. Test: `AuditRecordTests`.
- **Erasing history** _(planned for M41)_. The one exception to the audit table's integrity is the audited [erasure path](record-audit.md#erasing-values-from-history). It clears values from history, writes `history.erased` and never drops the tenant's data key.
- **Retention.** The database rejects any delete of an audit record. Notifications are deleted after `Notifications:RetentionDays`, 90 days by default. Other records last as long as the application keeps them. Test: `NotificationRetentionTests`.
- **Deleted records** _(planned for M41)_. A deleted record waits in the [recycle bin](recycle-bin.md) for the period the tenant sets, 30 days by default, before the worker purges it. Its history stays.
- **Logging.** Field values never appear in a log, a span or telemetry. Test: `TraceCorrelationTests`.
- **Access to logs.** As for Restricted. Test: `TraceCorrelationTests`.
- **Access control.** Policies and field grants, as for Restricted. Test: `RecordFieldPolicyTests`.
- **Privacy.** Personal data does not belong in a Business field. The owner marks such a field `sensitive`, and then treats it as Restricted. Test: none.

### Internal

- **Encryption at rest.** The operator's PostgreSQL or disk encryption protects the configuration. Test: none, because the control is the operator's.
- **Encryption in transit.** Every connection uses TLS. Outside Development, telemetry export must use `https`, see [telemetry export](host-settings.md#telemetry-export). Test: `ServerTelemetryTests`.
- **Integrity.** A release's content hash is SHA-256. The database rejects any change to the release history. Test: `ReleaseHistoryTests`.
- **Retention.** The database rejects any change to the release history, so no Axis command removes it. Logs and telemetry last as long as the operator's collector keeps them. Test: `ReleaseHistoryTests`.
- **Logging.** Logs and telemetry name things and hold no values, see [what telemetry never holds](telemetry.md#what-telemetry-never-holds). Test: `TraceCorrelationTests`.
- **Access to logs.** The operator protects the logs and telemetry. Their content needs no data grant. Test: none.
- **Access control.** Studio edits an application only for users with `Studio:AuthorRole`, and publishes only with `Studio:PublisherRole`, see [publishing](authoring-publishing.md#publishing). Test: `StudioTextWriteTests`.
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
