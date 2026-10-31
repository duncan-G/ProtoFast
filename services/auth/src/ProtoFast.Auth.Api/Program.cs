using ProtoFast.Auth.Api.Accounts;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Correlation;
using ProtoFast.Auth.Api.Email;
using ProtoFast.Auth.Api.Endpoints;
using ProtoFast.Auth.Api.Identity;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Security;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Tenancy;
using ProtoFast.Auth.Data;
using Amazon.SimpleEmailV2;
using ProtoFast.ProtoStrap;
using ProtoFast.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Its keys come from the environment's secret under ProtoStrap, and from the AppHost's
// environment variables under `aspire run`.
builder.AddServiceDefaults(stop: TimeSpan.FromSeconds(18)); // Inside the spec's stop.

builder.Services.Configure<TenantOptions>(builder.Configuration.GetSection("Tenants"));
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection("Keycloak"));
builder.Services.Configure<SessionPolicyOptions>(builder.Configuration.GetSection("Session"));
builder.Services.Configure<InternalJwtOptions>(builder.Configuration.GetSection("InternalJwt"));
builder.Services.Configure<SubscriptionOptions>(builder.Configuration.GetSection("Subscriptions"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));

builder.AddRedisClient("redis");        // IConnectionMultiplexer (Aspire wires the connection string)
builder.AddNpgsqlDataSource("auth");    // NpgsqlDataSource for the AuthDbContext
builder.Services.AddAuthDbContext();
builder.Services.AddHttpClient();       // back-channel to Keycloak

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITenantResolver, TenantResolver>();
builder.Services.AddSingleton<ISessionStore, RedisSessionStore>();
builder.Services.AddSingleton<ICorrelationStore, RedisCorrelationStore>();
builder.Services.AddSingleton<IEmailChangeStore, RedisEmailChangeStore>();
builder.Services.AddSingleton<IKeycloakGateway, KeycloakGateway>();
builder.Services.AddSingleton<IKeycloakAdmin, KeycloakAdmin>();
// SES with auth's own role in an account; SMTP to smtp4dev under `aspire run`.
if (Replica.UnderProtoStrap)
{
    builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2>(_ => new AmazonSimpleEmailServiceV2Client());
    builder.Services.AddSingleton<IEmailSender, SesEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
builder.Services.AddSingleton<IInternalJwtFactory, InternalJwtFactory>();
builder.Services.AddSingleton<IReplayGuard, RedisReplayGuard>();
builder.Services.AddSingleton<SessionResolver>();
builder.Services.AddSingleton<BackchannelLogout>();
builder.Services.AddScoped<AuthFlow>();
builder.Services.AddScoped<AccountFlow>();

var app = builder.Build();

app.MapDefaultEndpoints();                // /healthz, the spec's ready
app.MapAuthEndpoints();                   // /signin /signup /signin-oidc /signout /reset /add-passkey
app.MapAccountEndpoints();                // /account/me /account/email(/confirm) /account/passkeys /account/delete
app.MapExtAuthz();                        // /ext-authz/{**path}: the edge's authorizer, never routed

app.Run();

// Exposed so the integration-test WebApplicationFactory can boot the real host.
public partial class Program;
