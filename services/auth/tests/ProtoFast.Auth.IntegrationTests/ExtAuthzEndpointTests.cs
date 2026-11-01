using System.Net;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>
/// The authorizer as ProtoStrap's edge calls it: any method, at /ext-authz followed by the
/// request's own path and query, with the request's Host and cookie.
/// </summary>
public class ExtAuthzEndpointTests(TestAuthWebApplicationFactory factory) : IClassFixture<TestAuthWebApplicationFactory>
{
    private HttpClient Client()
    {
        return factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("GET", "/ext-authz/")]
    [InlineData("GET", "/ext-authz/app/stories/1")]
    [InlineData("POST", "/ext-authz/document.Documents/List")]
    [InlineData("PUT", "/ext-authz/story.Stories/Save?x=1")]
    public async Task Every_method_and_path_is_answered(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Host = "protofast.dev";

        using var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("false", response.Headers.GetValues("x-authenticated").Single());
        Assert.False(response.Headers.Contains("x-user-id"));
    }

    [Fact]
    public async Task A_dead_session_cookie_is_anonymous()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ext-authz/app");
        request.Headers.Host = "protofast.dev";
        request.Headers.Add("Cookie", "pf_session=not-a-session");

        using var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("false", response.Headers.GetValues("x-authenticated").Single());
    }

    [Fact]
    public async Task A_console_sends_an_anonymous_request_to_sign_in()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ext-authz/theplot/runs?page=2");
        request.Headers.Host = "admin.protofast.dev";

        using var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin?returnUrl=%2Ftheplot%2Fruns%3Fpage%3D2", response.Headers.Location?.OriginalString);
    }
}
