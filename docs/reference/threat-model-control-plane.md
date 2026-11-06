# Threat model: the control plane

_(planned for M33)_

This page holds the threats to the control plane, the **C** scope of the
[threat model](threat-model.md). The control plane runs sign-up, trials and billing for Axis SaaS,
apart from the cells. How its rows are found, rated and checked is in
[threat model method](threat-model-method.md).

## The control plane

The control plane has seven trust boundaries. Each has its own diagram and table below: the sign-up
page and API, the calls to the cells, the directory's DNS writes, the payment webhooks, the e-invoice
provider, and the billing portal's sign-in and its pages.

```mermaid
flowchart LR
    subgraph people [People]
        Visitor[Visitor who signs up]
        Payer[Owner or billing contact]
    end
    subgraph control [Regional control plane]
        SignUp[Sign-up page and API]
        Directory[Region directory, DNS jobs and cell client]
        Webhooks[Payment webhook routes, inbox and ledger jobs]
        Issuer[E-invoice issuing job and adapter]
        Billing[Billing portal]
    end
    subgraph dns [DNS provider]
        Records[(Tenant subdomain CNAME records)]
    end
    subgraph cell [Cell]
        Internal[Internal listener with mutual TLS]
    end
    subgraph idp [Control plane identity provider]
        Realm[axis-billing realm]
    end
    subgraph providers [External payment and e-invoice providers]
        PayOS[PayOS]
        Paddle[Paddle]
        EInvoice[E-invoice provider]
    end
    Visitor -->|HTTPS| SignUp
    SignUp -->|creates tenants through| Directory
    Directory -->|signed DNS updates| Records
    Directory -->|calls over mutual TLS| Internal
    Payer -->|HTTPS| Billing
    Billing -->|signs in through| Realm
    Payer -->|pay page loads Paddle's checkout| Paddle
    PayOS -->|signed webhooks to| Webhooks
    Paddle -->|signed webhooks to| Webhooks
    Issuer -->|issues e-invoices over HTTPS to| EInvoice
```

### Sign-up page and API

The sign-up page and its API take requests from anyone on the internet, before anyone signs in.
The page is planned for M33, see [sign-up](sign-up.md).

```mermaid
flowchart LR
    subgraph people [People on the internet]
        Visitor[Visitor who fills in the form]
        Bot[Bot or script]
    end
    subgraph signup [Sign-up on the control plane]
        Page[Sign-up page]
        Api[Sign-up API and challenge]
        Pending[(Pending sign-ups and held subdomains)]
        Mail[Verification email]
    end
    Visitor -->|form and subdomain check| Page
    Page -->|calls| Api
    Bot -->|repeated requests| Api
    Api -->|stores pending sign-up| Pending
    Api -->|queues the link| Mail
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Sign-up spam fills the cells with tenants that nobody uses. | Sign-up page and API | Denial of service | High | **Gap** | Closed by [#2460](https://github.com/phuongnse/axis/issues/2460), which adds proof of work and rate limits, and by [#2461](https://github.com/phuongnse/axis/issues/2461), which allows one trial per verified email and refuses disposable domains. |
| C4 | A replayed or leaked verification link creates a tenant for someone else. | Sign-up page and API | Spoofing | High | **Gap** | Closed by [#2459](https://github.com/phuongnse/axis/issues/2459), which makes the link single-use, checks it once within 24 hours and stores only a hash of its token. |
| C5 | A forged or replayed proof of work passes the bot check. | Sign-up page and API | Tampering | Medium | **Gap** | Closed by [#2460](https://github.com/phuongnse/axis/issues/2460), which signs each challenge, expires it after 10 minutes and accepts one solution once. |

Open threats, which no issue closes yet, see [threat model method](threat-model-method.md#how-it-is-filled):

- C2 (Information disclosure, Medium): the subdomain check reveals which tenant subdomains exist.
- C3 (Information disclosure, Medium): a sign-up answer for a verified email or a disposable domain reveals
  whether that email has signed up.

Repudiation and elevation of privilege do not apply to the page itself. A new tenant's records and grants
are covered by P3 and P7 in the [product threat model](threat-model-product.md).

### Control plane to cells

The control plane reaches each cell only over the cell's internal listener, with mutual TLS. The
control plane's side of that call is in [reaching the cells](control-plane.md#reaching-the-cells).

```mermaid
flowchart LR
    subgraph cp [Regional control plane]
        Client[Cell client with its certificate and secret]
    end
    subgraph cell [Cell]
        Internal[Internal listener]
        Api[Provisioning API under /api/cell/]
    end
    subgraph attacker [Someone with a stolen credential]
        Thief[Holder of a copied certificate or secret]
    end
    Client -->|mutual TLS and bearer token| Internal
    Internal -->|serves| Api
    Thief -->|calls the listener| Internal
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| C6 | A stolen control plane client certificate or secret acts on every tenant of a cell. | Control plane to cells | Spoofing | Critical | **Gap** | [#2995](https://github.com/phuongnse/axis/issues/2995) adds the listener's check of the client certificate. The control plane's secrets reach it only as projected files, see `scripts/helm-controlplane.test.mjs`. |
| C7 | The control plane calls a cell without mutual TLS. | Control plane to cells | Information disclosure | High | **Mitigated** | `CellClientTests` (the client presents its certificate and a bearer token over https, a client without the certificate fails, a certificate of another authority is not trusted, and http is never called) and `ControlPlaneOptionsTests` (an http internal or token URL of a cell, and a listed cell without its client certificate, stop the start). |
| C8 | A cell's internal listener accepts a call without the control plane's certificate. | Control plane to cells | Spoofing | Critical | **Gap** | Closed by [#2995](https://github.com/phuongnse/axis/issues/2995), which requires a client certificate from the region's internal certificate authority on the internal listener. |

Repudiation does not need a row: each job writes a platform audit record with its actor, see
[provisioning API](tenant-lifecycle.md#provisioning-api).

### Region directory and DNS

The region directory's jobs write each tenant's subdomain record to its cell's ingress, through the DNS
provider's dynamic update interface. See [routing a host](region-directory.md#routing-a-host).

```mermaid
flowchart LR
    subgraph cp [Regional control plane]
        Jobs[Region directory DNS jobs]
    end
    subgraph dns [DNS provider]
        Server[Authoritative DNS server]
        Zone[(Tenant subdomain CNAME records)]
    end
    subgraph cell [Cell]
        Ingress[Ingress of the cell]
    end
    Jobs -->|signed dynamic update| Server
    Server -->|writes or removes| Zone
    Zone -->|CNAME to| Ingress
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| C9 | A hijacked `CNAME` sends a tenant's traffic to another host. | Region directory and DNS | Tampering | High | **Mitigated** | `Rfc2136DnsRecordsTests` (the adapter signs its dynamic updates with TSIG, a server refuses an update signed with a wrong key, and an answer without a valid signature is a failure) and `CellPickerTests` (placing a tenant writes its record to its own cell's ingress). |
| C10 | A record left behind after a tenant's deletion lets someone take over the subdomain. | Region directory and DNS | Spoofing | High | **Mitigated** | `DirectoryCorrectionTests` (a tenant the correction drops gets an idempotent removal job, and a removal for a tenant that is back keeps its record) and `CellPickerTests` (a late write for a tenant that is gone writes nothing). |

Information disclosure and elevation of privilege do not apply to a `CNAME` record, which holds only a
name and a target.

### Payment webhooks

_(planned for M34)_

PayOS and Paddle tell the control plane about each payment with a signed webhook. The control plane
checks each one, keeps it once and applies it in order, see [webhooks](payments.md#webhooks).

```mermaid
flowchart LR
    subgraph providers [Payment providers]
        PayOS[PayOS]
        Paddle[Paddle]
    end
    subgraph attacker [Attacker]
        Forger[Forger or replayer of a captured webhook]
    end
    subgraph cp [Regional control plane]
        Route[POST /api/payments/payos/webhook and POST /api/payments/paddle/webhook]
        Check[Signature check over the raw body]
        Inbox[(billing.webhook_inbox)]
        Apply[Ordered apply job]
        Ledger[(billing.ledger_entries)]
        Recon[Nightly reconciliation]
    end
    PayOS -->|signed webhook| Route
    Paddle -->|signed webhook| Route
    Forger -->|forged or replayed webhook| Route
    Route -->|checked by| Check
    Check -->|stores the event in| Inbox
    Inbox -->|read in order by| Apply
    Apply -->|writes payment entries to| Ledger
    Recon -->|reads the day's transactions from| PayOS
    Recon -->|reads the day's transactions from| Paddle
    Recon -->|records missed payments in| Ledger
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| C12 | A forged webhook marks an invoice as paid. | Payment webhooks | Spoofing | Critical | **Gap** | [#2482](https://github.com/phuongnse/axis/issues/2482) checks PayOS's checksum over the raw body before it parses anything. [#2487](https://github.com/phuongnse/axis/issues/2487) checks Paddle's signature the same way. |
| C13 | A replayed webhook applies a payment or a state change twice. | Payment webhooks | Tampering | High | **Gap** | [#2482](https://github.com/phuongnse/axis/issues/2482) stores each event once, unique on the provider and its event id, and [#2487](https://github.com/phuongnse/axis/issues/2487) refuses a Paddle timestamp older than 5 minutes. `LedgerTests` shows that a second entry with the same idempotency key adds nothing. |
| C14 | A lost webhook leaves a paid invoice due and makes the tenant read-only. | Payment webhooks | Denial of service | Medium | **Gap** | [#2488](https://github.com/phuongnse/axis/issues/2488) reads each provider's transactions every night and records a missed payment as if its webhook had come. |
| C15 | A leaked PayOS checksum key or Paddle secret lets someone forge webhooks or act on Axis's provider account. | Payment webhooks | Spoofing | High | **Gap** | [#2480](https://github.com/phuongnse/axis/issues/2480) keeps PayOS's keys in Kubernetes Secrets and out of logs. [#2487](https://github.com/phuongnse/axis/issues/2487) adds the Paddle adapter but does not say where Paddle's secret is kept. |

There is no repudiation row for the webhooks: each event is kept in the inbox, and an event that does not
fit the state alerts the operator, see [webhooks](payments.md#webhooks).

### E-invoice provider

_(planned for M34)_

The control plane issues each Vietnamese e-invoice through a provider adapter, and the provider keeps it as
the law asks, see [through an adapter](e-invoices.md#through-an-adapter).

```mermaid
flowchart LR
    subgraph cp [Regional control plane]
        Issue[E-invoice issuing job]
        Adapter[E-invoice adapter]
        Ledger[(Amounts from the ledger)]
        Details[(Buyer from the billing details)]
        Operator[Operator correction with Axis.ControlPlane.Admin]
    end
    subgraph secrets [Kubernetes Secrets]
        Creds[Provider credentials]
    end
    subgraph provider [E-invoice provider]
        Api[Provider API over HTTPS]
    end
    subgraph thief [Someone with leaked credentials]
        Holder[Holder of the credentials]
    end
    Ledger -->|amounts| Issue
    Details -->|buyer| Issue
    Issue -->|sends the invoice to| Adapter
    Creds -->|read by| Adapter
    Adapter -->|HTTPS with credentials| Api
    Operator -->|adjusts or replaces through| Adapter
    Holder -->|issues, adjusts or replaces invoices| Api
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| C16 | A tampered e-invoice goes out under Axis's tax code with a wrong buyer, lines or amount. | E-invoice provider | Tampering | High | **Gap** | [#2486](https://github.com/phuongnse/axis/issues/2486) takes the amounts from the ledger and the buyer from the billing details. [#2495](https://github.com/phuongnse/axis/issues/2495) allows a correction only through the operator's command, with a reason. |
| C17 | Leaked e-invoice credentials let someone issue, adjust or replace invoices in Axis's name. | E-invoice provider | Spoofing | High | **Gap** | [#2486](https://github.com/phuongnse/axis/issues/2486) keeps the provider's credentials in Kubernetes Secrets. |

Repudiation does not need a row for the e-invoices: each correction writes an audit record, see
[after it is issued](e-invoices.md#after-it-is-issued).

### Billing portal sign-in

The billing portal signs people in through the `axis-billing` realm of the control plane's identity
provider. Its accounts are apart from the operator and tenant realms, see [billing portal](billing-portal.md#accounts).

```mermaid
flowchart LR
    subgraph people [People]
        Owner[Tenant owner or billing contact]
        Attacker[Someone with a stolen password]
    end
    subgraph portal [Billing portal]
        Site[Billing portal site]
    end
    subgraph idp [Control plane identity provider]
        Realm[axis-billing realm with verified email and a second factor]
    end
    Owner -->|signs in| Site
    Attacker -->|signs in| Site
    Site -->|sends to| Realm
    Realm -->|session| Site
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| C11 | Someone takes over a billing account and acts as a tenant's owner. | Billing portal sign-in | Spoofing | High | **Gap** | [#2463](https://github.com/phuongnse/axis/issues/2463) makes the account's email verified and offers a second factor at the first sign-in. |

### Billing portal pages

_(planned for M34)_

The portal's pages show the subscription and take payments. Every route checks its caller on the server, see
[rules](billing-portal.md#rules). The portal's account takeover is C11, above.

```mermaid
flowchart LR
    subgraph people [People]
        Owner[Owner or billing contact with a session]
        Other[Account of another tenant]
    end
    subgraph portal [Billing portal pages]
        Routes[Portal routes]
        Grant[Server-side grant check per tenant]
        Data[(Billing data of the tenant)]
        PayPage[Pay page]
        Script[Any other script on the pay page]
    end
    subgraph paddle [Paddle]
        Checkout[Checkout script and frame]
    end
    Owner -->|calls| Routes
    Other -->|calls another tenant's route| Routes
    Routes -->|checks the role with| Grant
    Grant -->|allows only the tenant's own data| Data
    Grant -->|answers 404 to| Other
    PayPage -->|loads only| Checkout
    Script -->|tries to read the session or change the payment| PayPage
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| C18 | A billing contact or owner of one tenant reads or changes another tenant's billing. | Billing portal pages | Elevation of privilege | High | **Gap** | [#2463](https://github.com/phuongnse/axis/issues/2463) answers `404` to any other tenant's page, and [#2468](https://github.com/phuongnse/axis/issues/2468) checks the account's role on every route. |
| C19 | Paddle's checkout script, or another script on the pay page, reads the session or changes the payment. | Billing portal pages | Tampering | Medium | **Gap** | [#2487](https://github.com/phuongnse/axis/issues/2487) allows Paddle's checkout script and frame on the pay page only, through that page's Content-Security-Policy. |
