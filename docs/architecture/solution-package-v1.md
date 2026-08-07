# Solution Package v1

> **Navigation**: [docs/architecture/solutions.md](./solutions.md) · [docs/use-cases/solutions/README.md](../use-cases/solutions/README.md) · [AGENTS.md](../../AGENTS.md)

This is the normative byte contract for `application/vnd.axis.solution.v1+json`; [docs/architecture/solutions.md](./solutions.md) owns its lifecycle and adapter realization. Any representation omitted here is rejected.

## Canonical bytes and shared grammar

Payload and typed-component documents are UTF-8 without BOM, NFC strings, and one JSON value with no insignificant whitespace. Object properties use the exact order declared here; arrays use the stated order. Unknown or duplicate properties, `null`, floating point/exponent numbers, untrimmed strings, and non-canonical escapes are invalid. Strings use `\"` and `\\`; U+0008, U+0009, U+000A, U+000C, and U+000D must use the short escapes `\b`, `\t`, `\n`, `\f`, and `\r` respectively; every other U+0000..U+001F control uses exactly uppercase-hex `\u00XX`. Escaped `/`, lower-case hex, a `\u00XX` form for a control with a required short escape, and escapes for printable or non-ASCII characters are invalid; printable and non-ASCII values are literal UTF-8. Integers are `0` or `-?[1-9][0-9]*`.

`key` is `[a-z][a-z0-9_]{0,62}`; `token` is `[a-z][a-z0-9_-]{0,62}`; `componentKey` is `[a-z][a-z0-9_.:@-]{0,199}`; `semanticPath` is `[a-z][a-z0-9_-]*(\\.[a-z][a-z0-9_-]*)*`, at most 200 characters; `ruleKey` is `semanticPath`, at most 120 characters; `typeId` is `[a-z][a-z0-9_.-]{0,127}`; `sha256` is 64 lower-case hex ASCII; `base64url` is unpadded RFC 4648 URL-safe base64. `languageTag` is a syntactically valid canonical BCP 47 tag; tags are compared case-insensitively but encoded once in canonical BCP 47 casing and object members sort by encoded ordinal value. `rfc3339` is UTC `YYYY-MM-DDTHH:MM:SSZ`; `gitRevision` is 40 or 64 lower-case hex; `uri` is absolute HTTPS, at most 2048 characters. `semver` is SemVer 2.0.0 `MAJOR.MINOR.PATCH` (no numeric leading zero), optional prerelease/build; identity preserves exact spelling.

## Payload schema

The root object has exactly these required properties in this order:

```text
schemaVersion: integer 1
solutionKey: key
solutionVersion: semver
axisOpenApiSha256: sha256
publisher: { publisherId: key, publisherKeyId: key }
provenance: { sourceRevision: gitRevision, buildId: NFC string 1..128, builtAt: rfc3339, sourceUri: uri }
components: Component[1..256]
```

`publisher` and `provenance` preserve the property order shown. `axisOpenApiSha256` is the exact committed Axis OpenAPI document digest the package was built against. A component preserves `type`, `key`, `sha256`, `content`, `dependsOn`; its `key` is `componentKey`; `content` is `base64url`, decodes to at most 1 MiB, and its SHA-256 equals `sha256`. Components are strictly ordinal sorted `(type,key)` and unique. Each dependency is exactly `{ "type": typeId, "key": componentKey }`, ordered `type,key`; each list is strictly sorted/unique, cannot self-reference, and references another listed component. Total edges are at most 512, the graph is a DAG, and maximum directed depth is 32. The full uploaded envelope is at most 10 MiB. Package dependencies do not exist.

## Component schemas

`content` is the exact byte handoff to the owning adapter, which owns semantic validation/application. It must conform to the selected fixed schema below, not an API response projection. New schema needs a new `type` ID.

| Type | Component key | Exact document (all members required unless `?`) |
|---|---|---|
| `authorization.policy.v1` | `policyKey` (`key`) | `{schemaVersion:1,policyKey:key,roles:[Role+],grants:[Grant+]}`. `Role` is `{key:key,presentation:{languageTag:{displayName:NFC string 1..256,description?:NFC string 1..2048}}}`; presentation members sort by canonical language tag, are unique case-insensitively, and include `en`. `Grant` is `{roleKey:key,actionKey:semanticPath,resourceType:semanticPath,resourceKey?:semanticPath,scope:"None"|"Own"|"All"}`. Roles sort by key; grants sort `(roleKey,actionKey,resourceType,resourceKey-or-empty,scope)`; all are unique and every roleKey exists. |
| `business-object.definition.v1` | `objectKey` (`key`) | `{schemaVersion:1,objectKey:key,name:NFC string 1..256,fields:[Field+]}`. `Field` is `{fieldKey:key,label:NFC string 1..256,order:integer >=0,fieldType:"Text"|"Integer"|"Decimal"|"Date"|"DateTime"|"Boolean"|"Choice",choiceConfiguration?:Choice,bindingKeys:[componentKey]}`. Fields use consecutive order 0..n-1 and unique field keys; every binding list is strictly ordinal sorted/unique and references a listed `rule.binding.v1` component which the Business Object component declares in `dependsOn`. `Choice` is `{selectionMode:"Single"|"Multiple",options:[{optionKey:key,label:NFC string 1..256,order:integer >=0}+]}` with unique option keys/consecutive order. Choice requires `choiceConfiguration`; every other type omits it. |
| `rule.binding.v1` | `definitionKey@definitionVersion:targetType:targetId:useCaseOrTrigger` (`componentKey`) | `{schemaVersion:1,definitionKey:ruleKey,definitionVersion:integer >=1,targetType:"business-object-field",targetId:semanticPath,useCaseOrTrigger:token,inputMappings:{inputKey:Mapping},priority:integer signed-32-bit,enabled:boolean,failureBehavior:"FailClosed"|"FailOpen"}`. `targetId` is exactly `objectKey+"."+fieldKey`; map keys sort ordinal. Mapping is `{kind:"Context",contextKey:semanticPath,literalValues:[]}` or `{kind:"Literal",contextKey?:never,literalValues:[NFC string]}`. The Rules adapter requires the referenced immutable built-in definition/version to exist and validates exact input coverage, type, and cardinality. The binding may target the semantic Business Object field before that object exists; the Business Object component therefore depends on the binding, and the binding declares no component dependency on the object or built-in definition. |

For `authorization.policy.v1`, the Authorization adapter validates every grant against the product Contracts action descriptor registry. Nullable `resourceKey` is exact, never wildcard. Non-record actions accept only `None`; record-scoped actions accept only `Own` or `All`. For one exact request, matching active record grants resolve `All` over `Own`; a non-record match resolves `None`; no exact match denies. An invalid descriptor or combination rejects the whole component during preflight.

No component owns server-generated IDs, revisions, timestamps, actor IDs, Workspace IDs, or API-only lifecycle/action fields. The current external reference product's `manifest.json` is source evidence for the business-object-field target, `record.value` context mapping, and field/rule identities; it is not a v1 package.

## DSSE v1.0.2

The envelope follows DSSE v1.0.2. `payloadType`, `payload`, and `signatures` are required; unknown envelope properties are ignored. `payloadType` is exactly `application/vnd.axis.solution.v1+json`. `payload` and `sig` each accept RFC 4648 standard or URL-safe base64, padded or unpadded; whitespace, invalid padding, and an input that mixes the two alphabets reject. There is exactly one signature; `keyid` may be absent or empty and is an unauthenticated lookup hint. Decode payload once, compute standard DSSE PAE over those exact bytes, and hand exactly those verified bytes to the Axis schema. Verify ES256 with SHA-256/P-256 and a 64-byte P1363 `r||s` signature. Payload unknowns remain invalid.

| Vector | Bytes / result |
|---|---|
| V-001 positive PAE | payload type UTF-8 hex `6170706c69636174696f6e2f766e642e617869732e736f6c7574696f6e2e76312b6a736f6e`; payload hex `7b7d`; expected PAE ASCII `DSSEv1 37 application/vnd.axis.solution.v1+json 2 {}`. |
| V-002 negative canonical | `efbbbf7b7d` (BOM), `7b0a7d` (newline), and `7b2278223a312c2278223a317d` (duplicate) reject. In a string, `\u000A` rejects because newline requires `\n`; `\u000b` rejects because hex must be uppercase; `\u000B` is the one valid escape for U+000B. All reject/accept decisions occur before adapter planning. |
| V-003 base64 | Envelope `payload:"e30="` and `payload:"e30"` both decode `{}`; standard `+/` and URL-safe `-_` alphabets are each accepted with valid optional padding, while whitespace, bad padding, and mixed alphabets reject. Component `content:"e30"` decodes `{}` and `e30=` rejects because component content remains canonical unpadded `base64url`. |
| V-004 envelope handling | Unknown envelope field is ignored and empty/omitted `keyid` is accepted as a hint, but empty `sig`, non-64-byte ES256 signatures, or a signed payload with any unknown Axis property rejects. |

The implementation must commit a conformance fixture generated by a test-only P-256 private key that is never committed. The fixture contains only public SPKI, envelope bytes, payload bytes, expected PAE bytes, and signature, proving verification never reserializes the payload.
