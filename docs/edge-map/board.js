/*
 * ProtoFast edge patchboard — an interactive map of the Envoy configuration in proxy/.
 *
 * Open index.html in a browser; no build step, no dependencies, no server needed.
 *
 * The topology below is HAND-MAINTAINED. It is not generated from the templates, so a
 * routing change in proxy/ has to be mirrored here in the same commit. Everything the page
 * draws comes from the data structures in the first half of this file:
 *
 *   CLIENTS, clusterDetail, clientRoutes(), KEYCLOAK_ROUTES,
 *   listenerDetail(), clientVhostDetail(), INGRESS      — what each hop is and how it is configured
 *   buildGraph(mode)                                    — assembles nodes + edges for one ENVOY_MODE
 *
 * The second half (from "Rendering") is generic: it lays the nodes out in five columns,
 * draws the wires between them with an SVG overlay, and renders the inspector panel.
 */

(function () {
  "use strict";

  const esc = (s) => String(s).replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));

  /* ------------------------------------------------------------------ *
   * Config facts, straight from proxy/ and the deploy compose files.
   * ------------------------------------------------------------------ */

  const CLIENTS = [
    { id: "admin", domain: "admin.example.com", devPort: 20000, isDefault: false },
    { id: "protofast", domain: "protofast.example.com", devPort: 20001, isDefault: true }
  ];

  const FILTER_CHAIN = String.raw`http_filters:
  - envoy.filters.http.cors
  - envoy.filters.http.grpc_web
  - envoy.filters.http.ext_authz   # → auth
  - envoy.filters.http.router`;

  const LOCAL_REPLY = String.raw`local_reply_config:
  mappers:
    - filter:
        and_filter:
          filters:
            - response_flag_filter:
                flags: ["UH","UF","UC","UT","NC","NR","URX"]
            - header_filter:            # :path
                string_match:
                  safe_regex:
                    regex: "^/(api|payments|otlp|account|realms|resources)/.*"
                invert_match: true
            - header_filter:            # x-envoy-original-path
                treat_missing_header_as_empty: true
                invert_match: true
      status_code: 500
      body: { filename: /etc/envoy/errors/upstream_unavailable.html }`;

  const clusterDetail = {
    clients_host: {
      kind: "cluster", tone: "up", title: "clients_host",
      sub: "The unified Angular SSR host. One Node process serves every client; it dispatches on the <code>x-client</code> header the virtual host injects, falling back to <code>DEFAULT_CLIENT</code>.",
      facts: [["Address", "clients:4000 (Docker DNS)"], ["Discovery", "STRICT_DNS"], ["LB", "ROUND_ROBIN"], ["Protocol", "HTTP/1.1"], ["Env", "CLIENTS_HOST_HOST / _PORT"]],
      blocks: [
        { h: "Serves", list: ["The catch-all <code>/</code> route — SSR pages", "<code>/assets/</code> and every hashed bundle or font"] },
        { why: "Rendered from the generic <b>envoy.cluster.yaml.tmpl</b> with no <code>http2_protocol_options</code>, so Envoy talks HTTP/1.1 to it — unlike the gRPC services." }
      ],
      src: "proxy/envoy.cluster.yaml.tmpl · entrypoint.sh"
    },
    auth: {
      kind: "cluster", tone: "up", title: "auth",
      sub: "auth-svc. Wears two hats: the ext_authz identity check the listener makes on every request, and a normal upstream for the browser OIDC flow and account JSON.",
      facts: [["Address", "${HOST_B_IP}:8080"], ["Discovery", "STRICT_DNS"], ["Protocol", "h2c (cleartext HTTP/2)"], ["Env", "AUTH_HOST / AUTH_PORT"]],
      blocks: [
        { h: "Reached by", list: ["<b>ext_authz</b> — gRPC, 0.5 s, on every request that does not opt out", "<code>/signin</code>, <code>/signup</code>, <code>/signin-oidc</code>, <code>/signout</code>, <code>/reset</code>, <code>/add-passkey</code>", "<code>/account/</code> — account management JSON"] },
        { why: "The old <code>/auth/</code> gRPC-web route is gone. Nothing calls auth over gRPC-web; ext_authz dials this cluster directly from the listener filter." },
        { h: "Not exposed", p: "<code>/backchannel-logout</code> is deliberately absent from the sign-in allowlist. Keycloak posts it a logout token over the private network, and it deletes sessions on the strength of that token alone." }
      ],
      src: "proxy/envoy.yaml.tmpl"
    },
    payments: {
      kind: "cluster", tone: "up", title: "payments",
      sub: "payments-svc, spoken to as gRPC-web from the browser and translated by the listener's grpc_web filter.",
      facts: [["Address", "${HOST_B_IP}:8081"], ["Discovery", "STRICT_DNS"], ["Protocol", "h2c (cleartext HTTP/2)"], ["Env", "PAYMENTS_HOST / PAYMENTS_PORT"]],
      blocks: [{ h: "Reached by", list: ["<code>/payments/</code> — prefix stripped, no timeout"] }],
      src: "proxy/envoy.yaml.tmpl"
    },
    api: {
      kind: "cluster", tone: "up", title: "api",
      sub: "The main gRPC API service. Requires the JWT that ext_authz injects — an anonymous request reaches it and is rejected there, not at the edge.",
      facts: [["Address", "${HOST_B_IP}:8082"], ["Discovery", "STRICT_DNS"], ["Protocol", "h2c (cleartext HTTP/2)"], ["Env", "API_HOST / API_PORT"]],
      blocks: [{ h: "Reached by", list: ["<code>/api/</code> — prefix stripped, no timeout"] }],
      src: "proxy/envoy.yaml.tmpl"
    },
    keycloak: {
      kind: "cluster", tone: "up", title: "keycloak",
      sub: "Keycloak on Host B. Only the browser login surface is published through here; the admin console, account console, JWKS and the master realm stay on the private network.",
      facts: [["Address", "${HOST_B_IP}:8083"], ["Discovery", "STRICT_DNS"], ["Protocol", "HTTP/1.1 (not h2c)"], ["Env", "KEYCLOAK_HOST / _PORT / _DOMAIN"]],
      blocks: [
        { why: "Setting <code>KEYCLOAK_HOST</code> is the switch that creates both this cluster and the Keycloak virtual host. Dev leaves it unset and reaches Keycloak directly through Aspire." }
      ],
      src: "proxy/entrypoint.sh"
    },
    otel_collector_http_cluster: {
      kind: "cluster", tone: "up", title: "otel_collector_http_cluster",
      sub: "OTLP/HTTP receiver. Browser telemetry lands here after the <code>/otlp/v1/</code> route rewrites the prefix away.",
      facts: [["Address", "otel-collector:4318"], ["Discovery", "logical_dns, V4_ONLY"], ["Protocol", "HTTP/1.1"], ["Env", "OTEL_HTTP_HOST / _PORT"]],
      blocks: [{ h: "Reached by", list: ["<code>/otlp/v1/</code> → <code>/v1/</code>, tracing and access logs suppressed"] }],
      src: "proxy/envoy.yaml.tmpl"
    },
    otel_collector_grpc_cluster: {
      kind: "cluster", tone: "up", title: "otel_collector_grpc_cluster",
      sub: "Envoy's own telemetry sink — never a route target. Stats, OTel access logs and spans all leave through here.",
      facts: [["Address", "otel-collector:4317"], ["Discovery", "logical_dns, V4_ONLY"], ["Protocol", "HTTP/2"], ["Flush", "stats every 5 s"], ["Timeout", "0.5 s"]],
      blocks: [
        { h: "Carries", list: ["<code>stats_sinks</code> — OpenTelemetry sink, 5 s interval", "OTel access log stream, <code>log_name: envoy-proxy</code>", "Spans from the <code>envoy.tracers.opentelemetry</code> provider"] },
        { h: "Conditional upstream TLS", p: "When <code>OTEL_GRPC_PORT</code> is 443 the entrypoint injects an UpstreamTlsContext with SNI, ALPN h2 and <code>ACCEPT_UNTRUSTED</code> — that is the Container Apps internal ingress. On 4317 it stays cleartext.", code: `if [ "$OTEL_GRPC_PORT" = "443" ]; then
  transport_socket:
    name: envoy.transport_sockets.tls
    typed_config:
      sni: \${OTEL_GRPC_HOST}
      common_tls_context:
        alpn_protocols: [ "h2" ]
        validation_context:
          trust_chain_verification: ACCEPT_UNTRUSTED` }
      ],
      src: "proxy/envoy.yaml.tmpl · entrypoint.sh"
    },
    deny: {
      kind: "local reply", tone: "deny", title: "direct_response",
      sub: "Not an upstream. Envoy answers these itself and the request never leaves the process.",
      facts: [["404", "everything outside the Keycloak allowlist"], ["405", "methods beyond GET/HEAD/POST/OPTIONS"], ["500", "styled HTML when a page upstream is down"]],
      blocks: [
        { why: "404 rather than 403 on the Keycloak vhost: a distinct <b>blocked</b> status would map the surface for anyone probing it." },
        { h: "The 500 page", p: "When a page route's cluster is unreachable, <code>local_reply_config</code> serves <code>errors/upstream_unavailable.html</code> with <code>no-store</code>, so cloudflared never falls through to Cloudflare's own error page.", code: LOCAL_REPLY }
      ],
      src: "proxy/envoy.keycloak-vhost.yaml.tmpl · envoy.listener.yaml.tmpl"
    }
  };

  function devClientCluster(c) {
    return {
      kind: "cluster", tone: "up", title: "client_" + c.id,
      sub: "The Angular dev server for <b>" + c.id + "</b>, running on the developer's machine and reached through <code>host.docker.internal</code>.",
      facts: [["Address", "${CLIENT_" + c.id.toUpperCase() + "_HOST}:${…_PORT}"], ["Discovery", "STRICT_DNS"], ["Protocol", "HTTPS (ng serve)"], ["Verification", "ACCEPT_UNTRUSTED"]],
      blocks: [
        { why: "<code>ng serve</code> uses the Aspire dev certificate, so the entrypoint attaches an UpstreamTlsContext that accepts it without validation.", },
        { h: "Serves", list: ["The catch-all <code>/</code> route — live HMR pages", "<code>/assets/</code> and every static bundle"] }
      ],
      src: "proxy/entrypoint.sh (DEV_UPSTREAM_TLS)"
    };
  }

  /* ---------- routes on a client virtual host ---------- */

  function clientRoutes(web) {
    return [
      {
        id: "otlp", label: "/otlp/v1/", sub: "browser telemetry", cluster: "otel_collector_http_cluster",
        tags: [["no ext_authz", "open"], ["no tracing", "quiet"], ["rewrite", ""]],
        d: {
          kind: "route", title: 'prefix: "/otlp/v1/"',
          sub: "Browser spans and metrics, proxied to the collector so the page never talks to it cross-origin.",
          facts: [["Cluster", "otel_collector_http_cluster"], ["Rewrite", "/otlp/v1/ → /v1/"], ["Timeout", "0s"], ["ext_authz", "disabled"], ["Sampling", "0 / 0 / 0"]],
          blocks: [
            { h: "Why the silence", p: "Sampling is pinned to zero and both access loggers filter this prefix out. Logging telemetry uploads would generate telemetry about telemetry." },
            { h: "Filtered on two headers", p: "The router rewrites the prefix before the loggers run, so the filters test <code>:path</code> <em>and</em> <code>x-envoy-original-path</code>. The latter needs <code>treat_missing_header_as_empty</code> — an absent header does not satisfy <code>invert_match</code>, and without it every unrewritten request would be dropped from the logs." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      },
      {
        id: "signin", label: "^/(signin|signup|signin-oidc|…)$", sub: "OIDC browser flow", cluster: "auth",
        tags: [["no ext_authz", "open"], ["allowlist", ""]],
        d: {
          kind: "route", title: "safe_regex — sign-in surface",
          sub: "The OIDC browser flow on auth-svc: plain HTTP redirects and <code>Set-Cookie</code>, not gRPC.",
          facts: [["Cluster", "auth"], ["ext_authz", "disabled"], ["Methods", "any"]],
          blocks: [
            { h: "Pattern", code: String.raw`safe_regex:
  regex: "^/(signin|signup|signin-oidc|signout|reset|add-passkey)(\?.*)?$"` },
            { h: "Why the optional query", p: "<code>safe_regex</code> matches <code>:path</code> <em>with</em> its query string, unlike <code>prefix</code> and <code>path</code>. The guard sends <code>/signin?returnUrl=…</code> and the OIDC callback always arrives as <code>/signin-oidc?code=&amp;state=</code>; without <code>(\\?.*)?</code> both fall through to the SPA and 404." },
            { why: "This is an <b>allowlist</b>, not a prefix. auth-svc also serves <code>/backchannel-logout</code>, which deletes sessions on the strength of a logout token alone — widening this to a prefix would hand that endpoint to the internet." },
            { h: "Keep it small", p: "Envoy rejects an RDS update whose RE2 program size exceeds 100. Folding <code>/account/*</code> into this alternation tripped that limit and left both virtual hosts with no routes at all." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      },
      {
        id: "account", label: "/account/", sub: "account JSON", cluster: "auth",
        tags: [["no ext_authz", "open"]],
        d: {
          kind: "route", title: 'prefix: "/account/"',
          sub: "Account management JSON on auth-svc — passkeys, profile, sessions.",
          facts: [["Cluster", "auth"], ["ext_authz", "disabled"]],
          blocks: [
            { h: "Why a prefix here", p: "<code>/account/*</code> is disjoint from <code>/backchannel-logout</code>, so a prefix is safe — and it keeps the sign-in regex under the RE2 program-size limit. The SPA's own page lives at <code>/app/account</code> and is unaffected." },
            { why: "ext_authz is off because these endpoints resolve the session cookie themselves." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      },
      {
        id: "assets", label: "/assets/", sub: "static assets", cluster: web,
        tags: [["no ext_authz", "open"], ["no tracing", "quiet"]],
        d: {
          kind: "route", title: 'prefix: "/assets/"',
          sub: "CDN-cacheable assets. They carry no identity, so the identity check is skipped.",
          facts: [["Cluster", web], ["ext_authz", "disabled"], ["Sampling", "0 / 0 / 0"]],
          blocks: [
            { h: "Why tracing is off", p: "The browser loads these through <code>&lt;script&gt;</code> and <code>&lt;link&gt;</code> tags, which cannot carry a <code>traceparent</code> — FetchInstrumentation only patches <code>fetch()</code>. Envoy would mint a fresh root trace per asset: orphan spans that can never join the document-load trace." },
            { why: "The <code>x-client</code> header is set at the <b>virtual host</b> level so it covers these routes too. Without it the SSR host falls back to <code>DEFAULT_CLIENT</code> and one client ends up serving another's hashed bundles." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      },
      {
        id: "files", label: "^/….(js|css|woff2|png|…)$", sub: "hashed bundles", cluster: web,
        tags: [["no ext_authz", "open"], ["no tracing", "quiet"]],
        d: {
          kind: "route", title: "safe_regex — asset extensions",
          sub: "Everything Angular emits outside <code>/assets/</code>: hashed bundles, source maps, fonts, images.",
          facts: [["Cluster", web], ["ext_authz", "disabled"], ["Sampling", "0 / 0 / 0"]],
          blocks: [
            { h: "Pattern", code: String.raw`safe_regex:
  regex: "^/[^?]*\.(?:js|css|map|woff2?|ttf|otf|eot|png|jpe?g|gif|svg|ico|webp|avif)$"` },
            { h: "Note the [^?]*", p: "Because <code>safe_regex</code> sees the query string, the character class stops the match at <code>?</code> — a versioned <code>/main.js?v=2</code> still resolves as an asset." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      },
      {
        id: "payments", label: "/payments/", sub: "gRPC-web", cluster: "payments",
        tags: [["ext_authz", ""], ["rewrite", ""], ["streaming", "quiet"]],
        d: {
          kind: "route", title: 'prefix: "/payments/"',
          sub: "gRPC-web from the browser, unwrapped by the listener filter and forwarded as h2c.",
          facts: [["Cluster", "payments"], ["Rewrite", "/payments/ → /"], ["Timeout", "0s"], ["grpc_timeout_header_max", "0s"], ["ext_authz", "enabled"]],
          blocks: [
            { h: "Why the zero timeouts", p: "Both the route timeout and the gRPC timeout header cap are disabled so long-lived server-streaming calls are not cut off mid-stream." },
            { why: "ext_authz runs here, but it never blocks. No session means the request arrives anonymous; payments-svc rejects it because the injected JWT is missing." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      },
      {
        id: "api", label: "/api/", sub: "gRPC-web", cluster: "api",
        tags: [["ext_authz", ""], ["rewrite", ""], ["streaming", "quiet"]],
        d: {
          kind: "route", title: 'prefix: "/api/"',
          sub: "The main gRPC-web surface. Identical shape to <code>/payments/</code>.",
          facts: [["Cluster", "api"], ["Rewrite", "/api/ → /"], ["Timeout", "0s"], ["grpc_timeout_header_max", "0s"], ["ext_authz", "enabled"]],
          blocks: [
            { h: "The rewrite has a side effect", p: "By the time an upstream failure is known, <code>:path</code> is already <code>/foo</code> — the original <code>/api/foo</code> lives in <code>x-envoy-original-path</code>. That is why the error-page and access-log filters test both headers." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      },
      {
        id: "spa", label: "/", sub: "catch-all — SSR pages", cluster: web, alt: "deny",
        tags: [["ext_authz", ""], ["error page", "block"]],
        d: {
          kind: "route", title: 'prefix: "/"',
          sub: "Everything else: server-rendered Angular pages. The only route where an identity actually changes what the user sees.",
          facts: [["Cluster", web], ["ext_authz", "enabled"], ["On upstream down", "500 + styled HTML"]],
          blocks: [
            { h: "Identity", p: "ext_authz resolves the session cookie against auth-svc with a 0.5 s budget and <code>failure_mode_allow</code>. No session, or auth down, means the page renders anonymous rather than 5xx." },
            { h: "Failure surface", p: "Page routes are the only ones that get the HTML error page — the gRPC-web, telemetry and JSON surfaces keep Envoy's default empty body, which is why the mapper's regex excludes their prefixes." }
          ],
          src: "proxy/envoy.vhost.yaml.tmpl"
        }
      }
    ];
  }

  /* ---------- routes on the Keycloak virtual host ---------- */

  const KEYCLOAK_ROUTES = [
    {
      id: "kc-method", label: ":method ∉ GET|HEAD|POST|OPTIONS", sub: "405 Method Not Allowed", cluster: "deny", deny: true,
      tags: [["direct 405", "block"]],
      d: {
        kind: "route", title: "method guard → 405",
        sub: "Login is GET and POST. Everything else is refused before any path rule is considered.",
        facts: [["Response", "405, inline body"], ["Position", "first — evaluated before every allow rule"]],
        blocks: [{ why: "A later path widening still cannot <code>PUT</code> or <code>DELETE</code> against the admin API." }],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    },
    {
      id: "kc-master", label: "/realms/master", sub: "404 Not Found", cluster: "deny", deny: true,
      tags: [["direct 404", "block"]],
      d: {
        kind: "route", title: 'prefix: "/realms/master" → 404',
        sub: "The master realm holds the bootstrap admin.",
        facts: [["Response", "404, inline body"], ["Position", "before the allow rules"]],
        blocks: [{ why: "The allow rules below match a realm-agnostic <code>[^/?]+</code>. Denying master first is what stops that pattern from serving the bootstrap admin's login form." }],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    },
    {
      id: "kc-auth", label: "…/openid-connect/auth", sub: "authorization endpoint", cluster: "keycloak",
      tags: [["allowed", "open"]],
      d: {
        kind: "route", title: "safe_regex — authorize",
        sub: "The interactive sign-in entry point the client redirects the browser to.",
        facts: [["Cluster", "keycloak"], ["Timeout", "0s"]],
        blocks: [{ h: "Pattern", code: String.raw`regex: "^/realms/[^/?]+/protocol/openid-connect/auth(\?.*)?$"` }],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    },
    {
      id: "kc-logout", label: "…/openid-connect/logout", sub: "logout endpoint", cluster: "keycloak",
      tags: [["allowed", "open"]],
      d: {
        kind: "route", title: "safe_regex — logout",
        sub: "Front-channel logout, reached from the browser.",
        facts: [["Cluster", "keycloak"], ["Timeout", "0s"]],
        blocks: [{ h: "Pattern", code: String.raw`regex: "^/realms/[^/?]+/protocol/openid-connect/logout(\?.*)?$"` }],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    },
    {
      id: "kc-login", label: "…/login-actions/*", sub: "form posts", cluster: "keycloak",
      tags: [["allowed", "open"]],
      d: {
        kind: "route", title: "safe_regex — login actions",
        sub: "Where the rendered login form posts: credentials, OTP, required actions, password reset.",
        facts: [["Cluster", "keycloak"], ["Timeout", "0s"]],
        blocks: [{ h: "Pattern", code: String.raw`regex: "^/realms/[^/?]+/login-actions/.*"` }],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    },
    {
      id: "kc-broker", label: "…/broker/…", sub: "social IdP · start + callback", cluster: "keycloak",
      tags: [["allowed", "open"], ["dormant", "quiet"]],
      d: {
        kind: "route", title: "safe_regex — identity brokering",
        sub: "Start and callback for a social identity provider.",
        facts: [["Cluster", "keycloak"], ["Status", "no providers enabled today"]],
        blocks: [
          { h: "Pattern", code: String.raw`regex: "^/realms/[^/?]+/broker/[^/?]+/(login|endpoint).*"` },
          { why: "Providers are off in the realm import. The routes exist so that enabling one in the admin console is the only step needed — no proxy change, no redeploy." }
        ],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    },
    {
      id: "kc-res", label: "/resources/", sub: "login theme assets", cluster: "keycloak",
      tags: [["allowed", "open"]],
      d: {
        kind: "route", title: 'prefix: "/resources/"',
        sub: "CSS, fonts and images for the login theme. Without these the sign-in page renders unstyled.",
        facts: [["Cluster", "keycloak"], ["Timeout", "0s"]],
        blocks: [],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    },
    {
      id: "kc-catch", label: "/", sub: "404 — everything else", cluster: "deny", deny: true,
      tags: [["direct 404", "block"]],
      d: {
        kind: "route", title: 'prefix: "/" → 404',
        sub: "The default is refusal. Admin console, account console, JWKS, token and userinfo endpoints all land here.",
        facts: [["Response", "404, inline body"], ["Position", "last"]],
        blocks: [{ why: "Add a rule <b>above</b> this one when a browser-facing endpoint is needed — never a hole here." }],
        src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
      }
    }
  ];

  /* ---------- listeners, virtual hosts, ingress ---------- */

  function listenerDetail(name, port, rdsFile, routeCfg, quic) {
    return {
      kind: "listener", title: name,
      sub: quic
        ? "The HTTP/3 twin on the same port number, over UDP. Browsers switch to it after seeing the <code>alt-svc</code> header the virtual host adds."
        : "Where the connection is accepted and the whole filter chain runs. Everything downstream of here is shaped by these settings.",
      facts: [
        ["Socket", (quic ? "UDP " : "TCP ") + "0.0.0.0:" + port],
        ["Codec", quic ? "HTTP3" : "auto (h2, http/1.1)"],
        ["ALPN", quic ? '["h3"]' : '["h2", "http/1.1"]'],
        ["Routes", "RDS · " + rdsFile],
        ["Config name", routeCfg],
        ["Remote address", "use_remote_address: true"]
      ],
      blocks: [
        { h: "Filter chain", code: FILTER_CHAIN },
        {
          h: "Identity check (ext_authz)",
          p: "gRPC to the <code>auth</code> cluster with a 0.5 s timeout and <code>failure_mode_allow: true</code>. It never blocks — no session simply means anonymous, and if auth is down the request continues rather than 5xx-ing. The APIs still reject those requests because they require the JWT it would have injected."
        },
        {
          h: "Scheme rewrite",
          p: "<code>scheme_header_transformation</code> overwrites the scheme with <code>http</code>. The upstream Kestrel servers listen on HTTP/2 without TLS and reject a request carrying an <code>https</code> scheme header.",
          code: String.raw`scheme_header_transformation:
  scheme_to_overwrite: http`
        },
        {
          why: "That rewrite also rewrites <code>x-forwarded-proto</code>, which is why the Keycloak virtual host re-asserts it as <b>https</b>."
        },
        {
          h: "When an upstream is down",
          p: "Page routes get a non-cacheable 500 with the styled HTML body. The gRPC-web, telemetry, JSON and Keycloak surfaces are excluded by regex and keep Envoy's default empty body.",
          code: LOCAL_REPLY
        },
        {
          h: "Observability",
          list: [
            "Access log → <code>/dev/stdout</code>, one line per request",
            "Access log → OTel, <code>log_name: envoy-proxy</code>, with <code>%UPSTREAM_CLUSTER%</code> attached",
            "Traces → <code>envoy.tracers.opentelemetry</code>, service name <code>envoy-proxy</code>",
            "Both loggers filter out <code>/otlp/</code> on <code>:path</code> and <code>x-envoy-original-path</code>"
          ]
        },
        {
          h: "Downstream TLS",
          p: quic
            ? "QuicDownstreamTransport wrapping the same certificate pair, ALPN <code>h3</code>."
            : "DownstreamTlsContext with the certificate pair from <code>ENVOY_TLS_CERT</code> / <code>ENVOY_TLS_KEY</code>."
        }
      ],
      src: "proxy/envoy.listener.yaml.tmpl"
    };
  }

  function clientVhostDetail(c, mode, web) {
    const publish = mode === "publish";
    const domains = publish ? (c.isDefault ? '["' + c.domain + '", "*"]' : '["' + c.domain + '"]') : '["*"]';
    const facts = [
      ["Domains", domains],
      ["Web cluster", web],
      ["alt-svc", 'h3=":' + (publish ? "8443" : c.devPort) + '"; ma=86400'],
      ["x-client", c.id + " (overwrite)"],
      ["Routes", "8"]
    ];
    if (publish && c.isDefault) facts.push(["Default", "answers unmatched hosts"]);
    return {
      kind: "virtual host", title: c.id,
      sub: publish
        ? "Selected by the <code>Host</code> header on the shared listener. One per client domain."
        : "The only virtual host on this listener — in dev the client is chosen by port, so it matches every host.",
      facts: facts,
      blocks: [
        {
          h: "x-client",
          p: "Injected at the virtual-host level so it covers every route, including ones added later. The unified SSR host dispatches on it and falls back to <code>DEFAULT_CLIENT</code> when it is absent; the API, auth and telemetry clusters simply ignore it.",
          code: String.raw`request_headers_to_add:
  - header: { key: "x-client", value: "` + c.id + String.raw`" }
    append_action: OVERWRITE_IF_EXISTS_OR_ADD`
        },
        { why: "Every route that can reach the web cluster must carry it — the static-asset routes included, or one client serves another's hashed bundles and the page 404s." },
        {
          h: "CORS",
          p: publish
            ? "Origin is pinned to the exact client domain."
            : "Pages and API share an origin per listener, so CORS is only a safety net for local origins.",
          code: publish
            ? String.raw`allow_origin_string_match:
  - exact: "https://` + c.domain + String.raw`"`
            : String.raw`allow_origin_string_match:
  - safe_regex:
      regex: "^https?://(localhost|127\.0\.0\.1)(:[0-9]+)?$"`
        },
        {
          h: "CORS policy",
          list: [
            "Methods GET, POST, PUT, DELETE, OPTIONS",
            "Credentials allowed; preflight cached 86400 s",
            "Exposes <code>grpc-status</code>, <code>grpc-message</code>, <code>error-code(s)</code>, <code>retry-after-seconds</code>",
            "Allows the gRPC-web and W3C trace headers: <code>x-grpc-web</code>, <code>grpc-timeout</code>, <code>traceparent</code>, <code>baggage</code>"
          ]
        },
        {
          h: "alt-svc",
          p: "Advertises the HTTP/3 listener on the same port. This response header is the only thing that moves a browser onto QUIC."
        }
      ],
      src: "proxy/envoy.vhost.yaml.tmpl"
    };
  }

  const KEYCLOAK_VHOST_DETAIL = {
    kind: "virtual host", title: "keycloak",
    sub: "A separate origin on its own domain, publishing exactly one thing: the browser sign-in surface. Everything else 404s and never reaches Keycloak.",
    facts: [["Domains", '["auth.example.com"]'], ["Env", "KEYCLOAK_DOMAIN"], ["ext_authz", "disabled (vhost-wide)"], ["Routes", "5 allowed · 3 refused"], ["Exists when", "KEYCLOAK_HOST is set"]],
    blocks: [
      { h: "Shape of the allowlist", p: "Two denials first — non-browser methods, then the master realm — followed by five allowed patterns, then a catch-all 404. Order is the security property here." },
      {
        h: "x-forwarded-proto",
        p: "Re-asserted as <code>https</code>. The listener's scheme rewrite (needed for the cleartext gRPC upstreams) also rewrites XFP to <code>http</code>; Keycloak then sees a plaintext request and <code>sslRequired=external</code> answers <code>error=ssl_required</code>. TLS always terminates at the listener, so <code>https</code> is truthful here.",
        code: String.raw`request_headers_to_add:
  - header: { key: "x-forwarded-proto", value: "https" }
    append_action: OVERWRITE_IF_EXISTS_OR_ADD`
      },
      { why: "ext_authz is disabled for the whole virtual host: Keycloak resolves its own SSO cookie, so the round trip would only fetch headers it ignores." },
      { h: "Keep the regexes small", p: "Envoy rejects an RDS update whose RE2 program size exceeds 100, and a rejected update leaves this virtual host with <em>no routes at all</em>." }
    ],
    src: "proxy/envoy.keycloak-vhost.yaml.tmpl"
  };

  const INGRESS = {
    publish: {
      label: "cloudflared", sub: "Cloudflare Tunnel",
      d: {
        kind: "entry", title: "cloudflared",
        sub: "The only thing in front of Envoy. Public TLS terminates at the Cloudflare edge; the tunnel daemon then dials Envoy over the Docker network.",
        facts: [["Origin", "https://envoy:8443"], ["noTLSVerify", "true"], ["Public TLS", "Cloudflare edge"], ["Internal cert", "self-signed, CN=envoy"]],
        blocks: [
          { h: "The internal certificate", p: "Baked into the image at build time — RSA 2048, 3650 days. It is never validated and never faces the public internet, so it needs no rotation story.", code: String.raw`RUN openssl req -x509 -newkey rsa:2048 -days 3650 -nodes \
      -keyout /etc/envoy/internal-tls.key \
      -out /etc/envoy/internal-tls.crt \
      -subj "/CN=envoy"` },
          { h: "Also listening", p: "An admin listener on <code>0.0.0.0:9901</code>, unpublished, with its access log at <code>/tmp/admin_access.log</code>. The container's health check hits <code>/ready</code> on it." },
          { h: "Node identity", p: "<code>id: protofast-proxy</code>, <code>cluster: protofast-proxy-cluster</code>. Stats flush to the OTel collector every 5 s." }
        ],
        src: "proxy/Dockerfile · deploy/docker-compose.host-edge.yml"
      }
    },
    dev: {
      label: "browser", sub: "https://localhost:2000x",
      d: {
        kind: "entry", title: "browser → localhost",
        sub: "No tunnel in dev. The browser hits a per-client listener directly, so the client is chosen by port rather than by hostname.",
        facts: [["admin", "https://localhost:20000"], ["protofast", "https://localhost:20001"], ["Certificate", "Aspire dev cert"], ["Admin", "0.0.0.0:9901"]],
        blocks: [
          { h: "Why the ports are pinned", p: "Keycloak's redirect URIs are exact — <code>https://localhost:20000|20001/signin-oidc</code>. A target-port-only mapping would take a random host port and the authorize request would fail with <code>invalid_redirect_uri</code>." },
          { h: "Reaching the host machine", p: "The container runs with <code>--add-host=host.docker.internal:host-gateway</code> so it can dial the dev servers and services on the developer's machine." }
        ],
        src: "apphost/EnvoyProxy/EnvoyProxyResourceBuilderExtensions.cs"
      }
    }
  };
  INGRESS["dev-host"] = {
    label: "browser", sub: "https://localhost:2000x",
    d: Object.assign({}, INGRESS.dev.d, {
      sub: "Same per-client listeners as dev, but the catch-all now points at the built SSR host container — a local smoke test of the publish artifact."
    })
  };

  /* ------------------------------------------------------------------ *
   * Graph assembly per mode
   * ------------------------------------------------------------------ */

  const MODE_NOTE = {
    publish: "<b>publish</b> — one listener on :8443, one virtual host per client domain, everything routed to the unified SSR host. This is what runs behind the tunnel.",
    dev: "<b>dev</b> — one HTTPS listener per client, each catch-all pointing at that client's <code>ng serve</code> on the developer's machine.",
    "dev-host": "<b>dev-host</b> — the same per-client listeners, but catch-alls route to the built SSR host container instead of a dev server."
  };

  function buildGraph(mode) {
    const publish = mode === "publish";
    const nodes = {};
    const edges = [];
    const cols = { ingress: [], listener: [], vhost: [], route: [], cluster: [] };

    function add(col, node) { nodes[node.id] = node; cols[col].push(node); return node; }

    // entry
    const ing = INGRESS[mode];
    add("ingress", { id: "ingress", kind: "ingress", label: ing.label, sub: ing.sub, d: ing.d, children: [] });

    // listeners + virtual hosts
    if (publish) {
      const l1 = add("listener", { id: "l:http_listener", kind: "listener", label: "http_listener", sub: "TCP :8443 · h2, http/1.1", tags: [["tls", ""]], d: listenerDetail("http_listener", 8443, "envoy.rds.yaml", "service_routes", false), children: [] });
      const l2 = add("listener", { id: "l:http_listener_quic", kind: "listener", label: "http_listener_quic", sub: "UDP :8443 · HTTP/3", tags: [["quic", "quiet"]], d: listenerDetail("http_listener_quic", 8443, "envoy.rds.yaml", "service_routes", true), children: [] });
      edges.push(["ingress", l1.id, "data"], ["ingress", l2.id, "data"]);

      CLIENTS.forEach(function (c) {
        const v = add("vhost", {
          id: "v:" + c.id, kind: "vhost", label: c.id, sub: c.domain + (c.isDefault ? "  ·  *" : ""),
          tags: c.isDefault ? [["default", "open"]] : [],
          d: clientVhostDetail(c, mode, "clients_host"), routes: clientRoutes("clients_host"), children: []
        });
        edges.push([l1.id, v.id, "data"], [l2.id, v.id, "data"]);
      });

      const kv = add("vhost", { id: "v:keycloak", kind: "vhost", label: "keycloak", sub: "auth.example.com", tags: [["allowlist", "block"]], d: KEYCLOAK_VHOST_DETAIL, routes: KEYCLOAK_ROUTES, children: [] });
      edges.push([l1.id, kv.id, "data"], [l2.id, kv.id, "data"]);
    } else {
      CLIENTS.forEach(function (c) {
        const web = mode === "dev" ? "client_" + c.id : "clients_host";
        const l = add("listener", {
          id: "l:listener_" + c.id, kind: "listener", label: "listener_" + c.id,
          sub: "TCP+UDP :" + c.devPort, tags: [["tls", ""], ["quic", "quiet"]],
          d: listenerDetail("listener_" + c.id, c.devPort, "envoy.rds." + c.id + ".yaml", "routes_" + c.id, false), children: []
        });
        const v = add("vhost", {
          id: "v:" + c.id, kind: "vhost", label: c.id, sub: 'domains: ["*"]',
          d: clientVhostDetail(c, mode, web), routes: clientRoutes(web), children: []
        });
        edges.push(["ingress", l.id, "data"], [l.id, v.id, "data"]);
      });
    }

    // clusters
    const clusterIds = [];
    if (mode === "dev") CLIENTS.forEach(function (c) { clusterIds.push("client_" + c.id); });
    else clusterIds.push("clients_host");
    clusterIds.push("auth", "api", "payments");
    if (publish) clusterIds.push("keycloak");
    clusterIds.push("otel_collector_http_cluster", "otel_collector_grpc_cluster", "deny");

    const CLUSTER_SUB = {
      clients_host: "clients:4000 · http/1.1",
      auth: "host-b:8080 · h2c",
      api: "host-b:8082 · h2c",
      payments: "host-b:8081 · h2c",
      keycloak: "host-b:8083 · http/1.1",
      otel_collector_http_cluster: "otel-collector:4318",
      otel_collector_grpc_cluster: "otel-collector:4317 · h2",
      deny: "answered by Envoy"
    };
    const DEV_SUB = { auth: "host.docker.internal · h2c", api: "host.docker.internal · h2c", payments: "host.docker.internal · h2c", otel_collector_http_cluster: "otel collector · http", otel_collector_grpc_cluster: "otel collector · h2" };

    clusterIds.forEach(function (id) {
      const isClient = id.indexOf("client_") === 0;
      const c = isClient ? CLIENTS.filter(function (x) { return "client_" + x.id === id; })[0] : null;
      const det = isClient ? devClientCluster(c) : clusterDetail[id];
      const sub = isClient ? "ng serve · https" : ((!publish && DEV_SUB[id]) || CLUSTER_SUB[id] || "");
      add("cluster", { id: "c:" + id, kind: "cluster", label: id, sub: sub, deny: id === "deny", d: det, children: [] });
    });

    // out-of-band wires from every listener
    cols.listener.forEach(function (l) {
      edges.push([l.id, "c:auth", "ctl"]);
      edges.push([l.id, "c:otel_collector_grpc_cluster", "ctl"]);
    });

    return { mode: mode, nodes: nodes, edges: edges, cols: cols };
  }

  /* ------------------------------------------------------------------ *
   * Rendering
   * ------------------------------------------------------------------ */

  const board = document.getElementById("board");
  const svg = document.getElementById("wires");
  const inspector = document.getElementById("inspector");
  const COLS = { ingress: "Entry", listener: "Listeners", vhost: "Virtual hosts", route: "Routes", cluster: "Upstreams" };

  let state = { mode: "publish", graph: null, vhost: null, sel: null };

  function nodeHtml(n) {
    const tags = (n.tags || []).map(function (t) {
      return '<span class="tag ' + (t[1] || "") + '">' + esc(t[0]) + "</span>";
    }).join("");
    return '<button type="button" class="node' + (n.deny ? " deny" : "") + '" data-kind="' + n.kind + '" data-id="' + esc(n.id) + '">' +
      '<div class="n-t">' + esc(n.label) + "</div>" +
      (n.sub ? '<div class="n-s">' + n.sub + "</div>" : "") +
      (tags ? '<div class="tags">' + tags + "</div>" : "") +
      "</button>";
  }

  function renderBoard() {
    const g = state.graph;
    const vh = g.nodes[state.vhost];
    const routes = vh && vh.routes ? vh.routes : [];

    // route nodes are rebuilt for the active virtual host only
    g.edges = g.edges.filter(function (e) { return e[2] !== "route"; });
    Object.keys(g.nodes).forEach(function (k) { if (k.indexOf("r:") === 0) delete g.nodes[k]; });
    g.cols.route = routes.map(function (r) {
      const id = "r:" + r.id;
      const n = { id: id, kind: "route", label: r.label, sub: r.sub, tags: r.tags, deny: !!r.deny, d: r.d, children: [] };
      g.nodes[id] = n;
      g.edges.push([vh.id, id, "route"], [id, "c:" + r.cluster, "route"]);
      if (r.alt) g.edges.push([id, "c:" + r.alt, "route"]);
      return n;
    });

    Object.keys(COLS).forEach(function (col) {
      const el = document.getElementById("col-" + col);
      const items = g.cols[col];
      el.innerHTML = '<div class="col-h">' + COLS[col] + "<span>" + items.length + "</span></div>" +
        items.map(nodeHtml).join("");
    });

    applyHighlight();
    requestAnimationFrame(drawWires);
  }

  /* highlight: the chain from the entry down to the selection, plus its own outgoing wires */
  function chainEdges() {
    const g = state.graph, sel = state.sel;
    if (!sel) return null;
    const on = new Set();
    const seen = new Set();
    // walk backwards to the entry
    (function up(id) {
      if (seen.has(id)) return;
      seen.add(id);
      g.edges.forEach(function (e) {
        if (e[1] === id) { on.add(e[0] + ">" + e[1]); up(e[0]); }
      });
    })(sel);
    // and one hop forward
    g.edges.forEach(function (e) { if (e[0] === sel) on.add(e[0] + ">" + e[1]); });
    return on;
  }

  function applyHighlight() {
    const on = chainEdges();
    const nodesOn = new Set();
    if (on) on.forEach(function (k) { const p = k.split(">"); nodesOn.add(p[0]); nodesOn.add(p[1]); });
    board.querySelectorAll(".node").forEach(function (el) {
      const id = el.getAttribute("data-id");
      el.classList.toggle("on", state.sel === id);
      el.classList.toggle("dim", !!on && !nodesOn.has(id));
    });
  }

  function drawWires() {
    const g = state.graph;
    const br = board.getBoundingClientRect();
    const on = chainEdges();
    const parts = [];
    g.edges.forEach(function (e) {
      const a = board.querySelector('.node[data-id="' + cssEsc(e[0]) + '"]');
      const b = board.querySelector('.node[data-id="' + cssEsc(e[1]) + '"]');
      if (!a || !b) return;
      const ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
      const x1 = ra.right - br.left, y1 = ra.top + ra.height / 2 - br.top;
      const x2 = rb.left - br.left, y2 = rb.top + rb.height / 2 - br.top;
      const dx = Math.max(18, (x2 - x1) * 0.42);
      const cls = ["wire"];
      if (e[2] === "ctl") cls.push("wire--ctl");
      if (on) cls.push(on.has(e[0] + ">" + e[1]) ? "wire--on" : "wire--off");
      parts.push('<path class="' + cls.join(" ") + '" d="M' + x1 + " " + y1 + " C" + (x1 + dx) + " " + y1 + "," + (x2 - dx) + " " + y2 + "," + x2 + " " + y2 + '"/>');
    });
    svg.setAttribute("viewBox", "0 0 " + br.width + " " + br.height);
    svg.innerHTML = parts.join("");
  }

  function cssEsc(s) { return s.replace(/["\\]/g, "\\$&"); }

  /* ---------- inspector ---------- */

  function blockHtml(b) {
    if (b.why) return '<div class="blk"><div class="why">' + b.why + "</div></div>";
    let out = '<div class="blk">';
    if (b.h) out += "<h3>" + esc(b.h) + "</h3>";
    if (b.p) out += "<p>" + b.p + "</p>";
    if (b.list) out += "<ul>" + b.list.map(function (i) { return "<li>" + i + "</li>"; }).join("") + "</ul>";
    if (b.code) out += '<pre class="code">' + esc(b.code) + "</pre>";
    return out + "</div>";
  }

  function renderInspector(id) {
    const n = state.graph.nodes[id];
    if (!n) { renderOverview(); return; }
    const d = n.d;
    const tone = d.tone === "up" ? " up" : d.tone === "deny" ? " deny" : "";
    let html = '<p class="i-kind' + tone + '">' + esc(d.kind) + "</p><h2>" + esc(d.title) + "</h2>";
    if (d.sub) html += '<p class="i-sub">' + d.sub + "</p>";
    if (d.facts && d.facts.length) {
      html += '<dl class="facts">' + d.facts.map(function (f) {
        return "<dt>" + esc(f[0]) + "</dt><dd>" + esc(f[1]) + "</dd>";
      }).join("") + "</dl>";
    }
    html += (d.blocks || []).map(blockHtml).join("");

    // jump links along the wire
    const g = state.graph;
    const next = g.edges.filter(function (e) { return e[0] === id && g.nodes[e[1]]; })
      .map(function (e) { return e[1]; });
    const uniq = next.filter(function (v, i) { return next.indexOf(v) === i; }).slice(0, 6);
    if (uniq.length) {
      html += '<div class="blk"><h3>Downstream of here</h3><div class="jump">' +
        uniq.map(function (t) { return '<button type="button" data-jump="' + esc(t) + '">' + esc(g.nodes[t].label) + " →</button>"; }).join("") +
        "</div></div>";
    }
    if (d.src) html += '<p class="src">' + esc(d.src) + "</p>";
    inspector.innerHTML = html;
  }

  function renderOverview() {
    inspector.innerHTML =
      '<p class="i-kind">the board</p><h2>How a request lands</h2>' +
      '<p class="i-sub">Click any block to open it. The lit wire shows the path a request took to get there.</p>' +
      '<div class="blk"><ul>' +
      "<li><b>Entry</b> — TLS terminates and the connection is accepted.</li>" +
      "<li><b>Listener</b> — CORS, gRPC-web, the identity check, then the router. Route table comes from RDS on disk.</li>" +
      "<li><b>Virtual host</b> — picked by <code>Host</code> in publish, by port in dev. Adds <code>x-client</code> and the CORS policy.</li>" +
      "<li><b>Route</b> — first match wins, top to bottom. Some rewrite the path, some skip the identity check, some answer on the spot.</li>" +
      "<li><b>Upstream</b> — a cluster, or a direct response that never leaves Envoy.</li>" +
      "</ul></div>" +
      '<div class="blk"><h3>Two things worth knowing</h3>' +
      "<p>The identity check <b>never blocks</b>. It runs on most routes, resolves the session, and lets the request through either way — services reject anonymous callers themselves.</p>" +
      "<p>Route order is a security property. The Keycloak virtual host denies before it allows, and its catch-all is a 404.</p></div>" +
      '<p class="src">proxy/ · envoy.yaml.tmpl, listener, rds, vhost, keycloak-vhost, cluster</p>';
  }

  /* ---------- events ---------- */

  board.addEventListener("click", function (ev) {
    const btn = ev.target.closest(".node");
    if (!btn) return;
    const id = btn.getAttribute("data-id");
    const n = state.graph.nodes[id];
    if (n && n.kind === "vhost" && state.vhost !== id) {
      state.vhost = id; state.sel = id; renderBoard(); renderInspector(id); return;
    }
    state.sel = state.sel === id ? null : id;
    applyHighlight();
    drawWires();
    if (state.sel) renderInspector(state.sel); else renderOverview();
  });

  inspector.addEventListener("click", function (ev) {
    const b = ev.target.closest("[data-jump]");
    if (!b) return;
    const id = b.getAttribute("data-jump");
    state.sel = id;
    applyHighlight(); drawWires(); renderInspector(id);
    const el = board.querySelector('.node[data-id="' + cssEsc(id) + '"]');
    if (el) el.scrollIntoView({ block: "nearest", inline: "nearest", behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
  });

  document.querySelectorAll(".seg button").forEach(function (b) {
    b.addEventListener("click", function () {
      setMode(b.getAttribute("data-mode"));
    });
  });

  function setMode(mode) {
    state.mode = mode;
    state.graph = buildGraph(mode);
    state.vhost = state.graph.cols.vhost[0].id;
    state.sel = null;
    document.querySelectorAll(".seg button").forEach(function (b) {
      b.setAttribute("aria-pressed", String(b.getAttribute("data-mode") === mode));
    });
    document.getElementById("modeNote").innerHTML = MODE_NOTE[mode];
    renderBoard();
    renderOverview();
  }

  let raf = null;
  window.addEventListener("resize", function () {
    if (raf) cancelAnimationFrame(raf);
    raf = requestAnimationFrame(drawWires);
  });
  document.querySelector(".board-scroll").addEventListener("scroll", function () {
    if (raf) cancelAnimationFrame(raf);
    raf = requestAnimationFrame(drawWires);
  });
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(drawWires);

  /* ---------- theme toggle ---------- */

  var THEME_KEY = "pf-edge-theme";
  var root = document.documentElement;

  function readStoredTheme() {
    try { return localStorage.getItem(THEME_KEY); } catch (e) { return null; }  // file:// can throw
  }

  function applyTheme(t) {
    if (t) root.setAttribute("data-theme", t); else root.removeAttribute("data-theme");
  }

  applyTheme(readStoredTheme());

  document.getElementById("themeToggle").addEventListener("click", function () {
    var cur = root.getAttribute("data-theme");
    var next = cur === "dark" ? "light"
             : cur === "light" ? "dark"
             : (window.matchMedia("(prefers-color-scheme: dark)").matches ? "light" : "dark");
    applyTheme(next);
    try { localStorage.setItem(THEME_KEY, next); } catch (e) { /* ignore */ }
    drawWires();
  });

  setMode("publish");
})();
