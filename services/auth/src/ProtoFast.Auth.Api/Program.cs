using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Correlation;
using ProtoFast.Auth.Api.Endpoints;
using ProtoFast.Auth.Api.Identity;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Services;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Tenancy;
using ProtoFast.Auth.Data;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.Secrets;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Configuration
    .AddEnvironmentVariables("Shared_")
    .AddEnvironmentVariables("Auth_");
if (builder.Environment.IsProduction())
{
    builder.Configuration.AddSecretsManager(options => builder.Configuration.Bind("Secrets", options));
}

builder.Services.AddGrpc();

builder.Services.Configure<TenantOptions>(builder.Configuration.GetSection("Tenants"));
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection("Keycloak"));
builder.Services.Configure<SessionPolicyOptions>(builder.Configuration.GetSection("Session"));
builder.Services.Configure<InternalJwtOptions>(builder.Configuration.GetSection("InternalJwt"));

builder.AddRedisClient("redis");        // IConnectionMultiplexer (Aspire wires the connection string)
builder.AddNpgsqlDataSource("auth");    // NpgsqlDataSource for the AuthDbContext
builder.Services.AddAuthDbContext();
builder.Services.AddHttpClient();       // back-channel to Keycloak

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITenantResolver, TenantResolver>();
builder.Services.AddSingleton<ISessionStore, RedisSessionStore>();
builder.Services.AddSingleton<ICorrelationStore, RedisCorrelationStore>();
builder.Services.AddSingleton<IKeycloakGateway, KeycloakGateway>();
builder.Services.AddSingleton<IInternalJwtFactory, InternalJwtFactory>();
builder.Services.AddSingleton<SessionResolver>();
builder.Services.AddScoped<AuthFlow>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapAuthEndpoints();                  // /signin /signup /signin-oidc /signout /reset (HTTP)
app.MapGrpcService<AuthorizationService>(); // ext_authz Check (gRPC)

app.Run();

// Exposed so the integration-test WebApplicationFactory can boot the real host.
public partial class Program;
