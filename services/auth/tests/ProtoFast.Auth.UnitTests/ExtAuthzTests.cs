using ProtoFast.Auth.Api;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Endpoints;
using ProtoFast.Auth.Api.Sessions;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// What auth answers ProtoStrap's edge, which asks about every request to the clients, the api
/// and payments before sending it on (<see cref="ExtAuthzEndpoint"/>).
/// </summary>
public class ExtAuthzTests
{
    private static readonly TenantConfig Admin = new()
    {
        Realm = "operators",
        ClientId = "admin",
        Consoles = new Dictionary<string, string> { ["theplot"] = "admin-theplot" },
    };

    private static readonly TenantConfig Protofast = new() { Realm = "protofast", ClientId = "protofast-web" };

    private static ResolvedIdentity Identity(string? rotated = null, params string[] roles) =>
        new("user-1", "operators", roles, "internal.jwt.value", rotated);

    private static string? Header(ExtAuthzEndpoint.Answer answer, string name) =>
        answer.Headers.Where(h => h.Key == name).Select(h => h.Value).SingleOrDefault();

    [Fact]
    public void An_anonymous_request_passes_marked_anonymous()
    {
        var answer = ExtAuthzEndpoint.Decide(Protofast, "/app", "/app", identity: null, Cookie);

        Assert.Equal(200, answer.Status);
        Assert.Equal("false", Header(answer, AuthHeaders.Authenticated));
        Assert.Null(Header(answer, AuthHeaders.UserId));
        Assert.Null(Header(answer, AuthHeaders.InternalJwt));
    }

    [Fact]
    public void A_signed_in_request_carries_the_identity()
    {
        var answer = ExtAuthzEndpoint.Decide(Protofast, "/app", "/app", Identity(null, "a", "b"), Cookie);

        Assert.Equal(200, answer.Status);
        Assert.Equal("true", Header(answer, AuthHeaders.Authenticated));
        Assert.Equal("user-1", Header(answer, AuthHeaders.UserId));
        Assert.Equal("operators", Header(answer, AuthHeaders.Tenant));
        Assert.Equal("a,b", Header(answer, AuthHeaders.Roles));
        Assert.Equal("internal.jwt.value", Header(answer, AuthHeaders.InternalJwt));
        Assert.Null(Header(answer, "set-cookie"));
    }

    [Fact]
    public void A_rotated_session_sends_the_browser_its_new_cookie()
    {
        // The edge hands set-cookie to the browser (authorize.respond); nothing else sets it.
        var answer = ExtAuthzEndpoint.Decide(Protofast, "/app", "/app", Identity("new-session-id"), Cookie);

        Assert.Equal(200, answer.Status);
        Assert.Equal("pf_session=new-session-id; Path=/", Header(answer, "set-cookie"));
    }

    [Fact]
    public void A_request_for_an_unknown_host_passes_anonymous()
    {
        var answer = ExtAuthzEndpoint.Decide(tenant: null, "/theplot/runs", "/theplot/runs", identity: null, Cookie);

        Assert.Equal(200, answer.Status);
        Assert.Equal("false", Header(answer, AuthHeaders.Authenticated));
    }

    [Fact]
    public void An_anonymous_request_for_a_console_is_sent_to_sign_in()
    {
        var answer = ExtAuthzEndpoint.Decide(Admin, "/theplot/runs", "/theplot/runs?page=2", identity: null, Cookie);

        Assert.Equal(302, answer.Status);
        Assert.Equal("/signin?returnUrl=%2Ftheplot%2Fruns%3Fpage%3D2", answer.Location);
        Assert.Null(Header(answer, AuthHeaders.UserId));
    }

    [Fact]
    public void A_console_turns_away_an_operator_without_its_role()
    {
        var answer = ExtAuthzEndpoint.Decide(Admin, "/theplot/runs", "/theplot/runs", Identity(null, "admin-protofast"), Cookie);

        Assert.Equal(302, answer.Status);
        Assert.Equal("/forbidden", answer.Location);
    }

    [Fact]
    public void A_console_admits_a_holder_of_its_role()
    {
        var answer = ExtAuthzEndpoint.Decide(Admin, "/theplot/runs", "/theplot/runs", Identity(null, "admin-theplot"), Cookie);

        Assert.Equal(200, answer.Status);
        Assert.Equal("admin-theplot", Header(answer, AuthHeaders.Roles));
    }

    [Fact]
    public void The_admin_host_outside_its_consoles_admits_anyone()
    {
        var answer = ExtAuthzEndpoint.Decide(Admin, "/forbidden", "/forbidden", identity: null, Cookie);

        Assert.Equal(200, answer.Status);
    }

    [Fact]
    public void A_turned_away_operator_still_gets_a_rotated_cookie()
    {
        // Losing the new id would sign them out of the platform console too.
        var answer = ExtAuthzEndpoint.Decide(Admin, "/theplot/", "/theplot/", Identity("rotated", "platform"), Cookie);

        Assert.Equal(302, answer.Status);
        Assert.Equal("pf_session=rotated; Path=/", Header(answer, "set-cookie"));
    }

    private static string Cookie(string sessionId) => $"pf_session={sessionId}; Path=/";
}
