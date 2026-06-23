# Multi-Tenant Auth Architecture (Cloudflare → Envoy → Keycloak)

Edge-terminated, BFF-style authentication for multiple client domains under
`*.protofast.dev`, with **Envoy ext_authz** delegating to a central auth service that resolves tenant → Keycloak realm dynamically. Angular SSR renders identity-aware HTML; the browser only ever holds an opaque session cookie. 

## Components


| Component          | Host                                             | Role                                                             |
| ------------------ | ------------------------------------------------ | ---------------------------------------------------------------- |
| **Cloudflare**     | edge for `*.protofast.dev`, `auth.protofast.dev` | TLS termination, WAF, CDN, `cloudflared` tunnel to Envoy         |
| **Envoy**          | origin (behind tunnel)                           | Single wildcard vhost; `ext_authz` gate + route table            |
| **auth-svc**       | internal cluster only — **no public domain**     | tenant→realm map, OIDC flow, ext_authz `Check`, session issuance |
| **Angular SSR**    | internal upstream cluster                        | static bundles + anonymous + personalized SSR HTML               |
| **app / gRPC API** | internal upstream cluster                        | business backend; trusts injected internal JWT                   |
| **Keycloak**       | `auth.protofast.dev`                             | realms: `protofast`, `myfitness`, `theplot`                      |


> Only `*.protofast.dev` (client apps) and `auth.protofast.dev` (Keycloak) are
> publicly reachable through Cloudflare. **auth-svc, Angular SSR, and the API are
> internal-only** — reachable solely as Envoy upstream clusters / via ext_authz,
> never directly from the internet.

## Realm / client mapping

3 realms, 4 clients — staff share a realm; each product is isolated.


| Domain                    | Realm       | Client          | Users                 |
| ------------------------- | ----------- | --------------- | --------------------- |
| `protofast.dev`           | `protofast` | `protofast-web` | Root / public + staff |
| `admin.protofast.dev`     | `protofast` | `admin`         | Staff admin console   |
| `myfitness.protofast.dev` | `myfitness` | `myfitness-web` | Product tenant users  |
| `theplot.protofast.dev`   | `theplot`   | `theplot-web`   | Product tenant users  |


The realm/client map lives in **auth-svc data, not Envoy config** — adding a
tenant is a DB row, not a redeploy.

---

## Topology

```mermaid
flowchart TB
    Browser([Browser<br/>myfitness.protofast.dev])

    subgraph CF[Cloudflare Edge]
        direction TB
        CFfeat[DNS · TLS · WAF · CDN cache<br/>cloudflared tunnel]
    end

    subgraph ENVOY[Envoy — vhost *.protofast.dev]
        direction TB
        EXT[ext_authz filter]
        ROUTER{Route table}
    end

    AUTH[auth-svc<br/>INTERNAL ONLY<br/>realm map · OIDC · ext_authz Check]
    SSR[Angular SSR Node<br/>INTERNAL · static + SSR HTML]
    API[app / gRPC API<br/>INTERNAL · trusts internal JWT]

    subgraph KC[Keycloak — auth.protofast.dev]
        direction LR
        R1[(realm protofast)]
        R2[(realm myfitness)]
        R3[(realm theplot)]
    end

    Browser -->|Host preserved| CF
    CF -->|tunnel| ENVOY
    EXT -.->|Check| AUTH
    ROUTER -->|/login /signup /signin-oidc| AUTH
    ROUTER -->|/ /pricing /app/*| SSR
    ROUTER -->|/api /payments| API
    SSR -->|/api + internal JWT| API
    AUTH -->|back-channel<br/>token exchange| KC
    CF -->|auth.protofast.dev vhost| KC
```



---

## Route buckets (single wildcard vhost)

```mermaid
flowchart LR
    REQ[Request to *.protofast.dev] --> R{path}
    R -->|/assets/* *.js *.css| STATIC[Angular SSR static<br/>ext_authz OFF<br/>CDN cacheable]
    R -->|/ /pricing| PUB[Angular SSR anonymous<br/>ext_authz OPTIONAL<br/>short TTL]
    R -->|/login /signup /reset<br/>/signin-oidc /signout| OIDC[auth-svc<br/>runs OIDC flow]
    R -->|/app/*| PROT[Angular SSR personalized<br/>ext_authz ENFORCE<br/>no-store]
    R -->|/api/* /payments/*| BACK[app / gRPC backend<br/>ext_authz ENFORCE]
```



---

## Flow A — root user: sign in on `protofast.dev`, redirect to `admin`

Staff log in once; the admin console authenticates **silently** because both
clients share the `protofast` realm and Keycloak holds an SSO session.

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant CF as Cloudflare
    participant E as Envoy
    participant A as auth-svc
    participant KC as Keycloak (realm protofast)

    B->>CF: GET protofast.dev/login
    CF->>E: tunnel (Host: protofast.dev)
    E->>A: route /login
    A-->>B: 302 → KC /realms/protofast/auth?client_id=protofast-web
    B->>KC: authenticate
    KC-->>KC: set SSO session cookie (auth.protofast.dev)
    KC-->>B: 302 → protofast.dev/signin-oidc?code=...
    B->>E: GET /signin-oidc?code=...
    E->>A: route /signin-oidc
    A->>KC: code → token exchange (secret)
    A-->>B: Set-Cookie session (host protofast.dev) · 302 → admin.protofast.dev/app
    B->>E: GET admin.protofast.dev/app
    E->>A: ext_authz Check (no admin cookie)
    A-->>B: 302 → KC /realms/protofast/auth?client_id=admin
    B->>KC: (SSO session already exists)
    KC-->>B: SILENT 302 → admin.protofast.dev/signin-oidc?code=...
    B->>E: GET /signin-oidc?code=...
    E->>A: route /signin-oidc
    A->>KC: code → token exchange
    A-->>B: Set-Cookie session (host admin.protofast.dev) · 302 → /app
    B->>E: admin console loads (no re-login)
```



---

## Flow B — tenant user: sign up on `myfitness.protofast.dev`

Realm-isolated. A `myfitness` session presented to `theplot.protofast.dev`
fails ext_authz → fresh login against the `theplot` realm.

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant CF as Cloudflare
    participant E as Envoy
    participant A as auth-svc
    participant KC as Keycloak (realm myfitness)

    B->>CF: GET myfitness.protofast.dev/signup
    CF->>E: tunnel (Host: myfitness.protofast.dev) — bypass cache
    E->>A: route /signup (direct, no gate)
    A-->>A: Host → realm=myfitness, client=myfitness-web
    A-->>B: Set-Cookie correlation · 302 → KC /realms/myfitness/auth?prompt=create
    B->>KC: register (myfitness realm only)
    KC-->>B: 302 → myfitness.protofast.dev/signin-oidc?code=...
    B->>E: GET /signin-oidc?code=...
    E->>A: route /signin-oidc
    A->>KC: code → token exchange (myfitness secret)
    A-->>A: upsert user in DB (first-login provisioning)
    A-->>B: Set-Cookie session (host-only myfitness.protofast.dev) · 302 → /app
    B->>E: GET /app/dashboard
    E->>A: ext_authz Check (valid session)
    A-->>E: 200 + x-user-id, x-tenant=myfitness, x-roles, x-internal-jwt
    E->>+SSR: route /app + identity headers
    SSR->>API: GET /api/... (x-internal-jwt)
    API-->>SSR: data
    SSR-->>-B: personalized HTML (Cache-Control: private, no-store)
```



> `SSR` = Angular SSR Node server, `API` = app / gRPC backend (omitted from the
> participant list above for brevity; both are Envoy upstreams).

---

## Identity & token relay (BFF)

The browser never sees a Keycloak token — only an opaque session cookie.

```mermaid
flowchart LR
    subgraph Browser
        C[session cookie<br/>opaque, HttpOnly]
    end
    subgraph Envoy
        X[ext_authz validates cookie<br/>injects identity + internal JWT]
    end
    subgraph auth-svc
        S[(session store<br/>Keycloak tokens,<br/>refresh)]
    end
    C --> X
    X -.Check.-> S
    X -->|x-user-id x-tenant x-roles<br/>x-internal-jwt| SSR[Angular SSR]
    X -->|x-internal-jwt| API[gRPC backend]
    SSR -->|forwards x-internal-jwt| API
```



---

## Cloudflare cache rules


| Content                            | Cache?              | Directive                             |
| ---------------------------------- | ------------------- | ------------------------------------- |
| `/assets/*`, hashed `*.js`/`*.css` | Yes, long TTL       | `public, max-age=31536000, immutable` |
| `/`, `/pricing` (anonymous SSR)    | Cautious, short TTL | `public, max-age=60`, host-keyed      |
| `/app/*` (personalized SSR)        | **Never**           | `private, no-store`                   |
| any `Set-Cookie` response          | **Never**           | bypass                                |
| `/api/*`                           | **Never**           | `no-store`                            |


**Cache key must include `Host`** so tenants never share entries. Personalized
SSR + shared CDN cache = cross-user data leak if mis-set — this is the single
highest-risk item.

---

## Operational gotchas

1. **Host preservation through `cloudflared`** — ext_authz realm resolution
  depends entirely on the original `Host`; don't let the tunnel ingress
   rewrite it.
2. **Forwarded proto** — TLS terminates at Cloudflare, so Envoy/Keycloak/auth-svc
  must trust `X-Forwarded-Proto: https` to build `https://` redirect URIs.
   Keycloak: `KC_PROXY_HEADERS=xforwarded`, `KC_HOSTNAME=auth.protofast.dev`.
3. **SSR cache poisoning** — see cache table; emit `private, no-store` on
  personalized responses.
4. **WAF vs OIDC** — exclude `/signin-oidc` and the back-channel from bot/CAPTCHA
  challenges.
5. **Cookie attributes** — `Secure; HttpOnly; SameSite=Lax` (Lax required so the
  cookie survives the top-level redirect back from Keycloak; Strict would drop
   it).

---

## Why one vhost + ext_authz (vs vhost-per-tenant)


|                                 | vhost / tenant          | **1 vhost + ext_authz**    |
| ------------------------------- | ----------------------- | -------------------------- |
| Add a tenant                    | Envoy config push       | DB row                     |
| Realm selection                 | static per vhost        | dynamic per request (Host) |
| vhost-count scaling             | bounded                 | non-issue                  |
| public-but-identity-aware pages | awkward                 | natural                    |
| login/signup/reset logic        | spread in filter config | centralized in auth-svc    |
| cost                            | config only             | build & run auth-svc       |


