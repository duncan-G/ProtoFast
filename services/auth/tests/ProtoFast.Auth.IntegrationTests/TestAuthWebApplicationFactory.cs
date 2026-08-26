using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoFast.Auth.Api.Accounts;
using ProtoFast.Auth.Api.Correlation;
using ProtoFast.Auth.Api.Sessions;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>
/// Boots the real auth host with hermetic test config: a freshly generated internal-JWT keypair
/// and a stubbed session store, so the full Program.cs wiring is exercised without Redis/Postgres/
/// Keycloak. The full OIDC round-trip belongs in a Testcontainers suite (guide §9) and is out of
/// scope for these offline tests.
/// </summary>
public sealed class TestAuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public string InternalJwtPublicKeyPem { get; }

    private readonly string _privateKeyPem;

    public TestAuthWebApplicationFactory()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privateKeyPem = ec.ExportPkcs8PrivateKeyPem();
        InternalJwtPublicKeyPem = ec.ExportSubjectPublicKeyInfoPem();

        // The Aspire clients read their connection strings while Program.cs is still registering
        // services, before this factory's in-memory config is layered in — so these two have to
        // arrive as environment variables. Neither client dials until it is used, and the stubbed
        // stores mean nothing ever reaches Redis or Postgres.
        Environment.SetEnvironmentVariable("ConnectionStrings__redis", "localhost:6379");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__auth",
            "Host=localhost;Port=5432;Database=auth;Username=auth;Password=test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Port 1 never answers: the back-channel is deliberately unreachable so the tests
                // stay hermetic and can assert what happens when Keycloak can't be reached.
                ["Keycloak:Authority"] = "http://127.0.0.1:1",
                ["Keycloak:PublicAuthority"] = "https://auth.protofast.test",
                ["InternalJwt:PrivateKeyPem"] = _privateKeyPem,
                ["InternalJwt:KeyId"] = "test-1",
                ["Tenants:ByHost:protofast.dev:Realm"] = "protofast",
                ["Tenants:ByHost:protofast.dev:ClientId"] = "protofast-web",
            }));

        // Swap the Redis-backed stores for in-memory stubs so the tests need no running Redis.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionStore>();
            services.AddSingleton<StubSessionStore>();
            services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<StubSessionStore>());

            services.RemoveAll<ICorrelationStore>();
            services.AddSingleton<ICorrelationStore, StubCorrelationStore>();

            services.RemoveAll<IEmailChangeStore>();
            services.AddSingleton<IEmailChangeStore, StubEmailChangeStore>();
        });
    }
}
