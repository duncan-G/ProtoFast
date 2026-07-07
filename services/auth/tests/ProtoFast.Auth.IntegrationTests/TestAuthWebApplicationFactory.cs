using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:redis"] = "localhost:6379",
                ["ConnectionStrings:auth"] = "Host=localhost;Port=5432;Database=auth;Username=auth;Password=test",
                ["Keycloak:Authority"] = "http://localhost:8080",
                ["InternalJwt:PrivateKeyPem"] = _privateKeyPem,
                ["InternalJwt:KeyId"] = "test-1",
                ["Tenants:ByHost:protofast.dev:Realm"] = "protofast",
                ["Tenants:ByHost:protofast.dev:ClientId"] = "protofast-web",
            }));

        // Swap the Redis store for an in-memory stub so the tests need no running Redis.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionStore>();
            services.AddSingleton<ISessionStore, StubSessionStore>();
        });
    }
}
