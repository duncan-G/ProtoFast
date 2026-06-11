# Update Envoy CORS for a new Angular client

The Angular client is a browser app that makes requests **to** Envoy —
it is not an Envoy cluster (the admin client is already an upstream
cluster added during bootstrap). The only Envoy configuration a new
client affects is **CORS allowed origins**, so Envoy accepts
cross-origin requests from the new client's HTTPS origin.

---

## 1. Update CORS wiring in `apphost/Program.cs`

Add the new client's endpoint to the existing Envoy CORS calls:

```csharp
proxy.WithCorsOriginExact(builder, «clientname»Endpoint);
proxy.WithCorsOriginSubdomainRegex(builder, «clientname»Endpoint);
```

These inject the client's origin into the `CORS_ORIGIN_EXACT` and
`CORS_ORIGIN_SUBDOMAIN_REGEX` environment variables that
`entrypoint.sh` substitutes into the CORS fragment templates. In dev
mode the client's origin is its HTTPS URL (e.g.
`https://localhost:4201`); in publish mode it's `https://<host>`.

## 2. Rebuild and verify

```bash
dotnet build apphost
```

If the project was already running, restart with `aspire stop` then
`aspire start` (or `aspire run`).
