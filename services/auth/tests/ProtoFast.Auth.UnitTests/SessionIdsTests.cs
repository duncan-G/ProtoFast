using ProtoFast.Auth.Api.Sessions;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

public class SessionIdsTests
{
    [Fact]
    public void Generate_is_url_safe_and_unpadded()
    {
        var id = SessionIds.Generate();

        Assert.DoesNotContain('+', id);
        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('=', id);
        Assert.True(id.Length >= 43); // 32 bytes base64url
    }

    [Fact]
    public void Generate_is_unique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => SessionIds.Generate()).ToHashSet();
        Assert.Equal(1000, ids.Count);
    }

    [Theory]
    [InlineData("pf_session=abc123", "pf_session", "abc123")]
    [InlineData("other=x; pf_session=abc123; more=y", "pf_session", "abc123")]
    [InlineData("PF_SESSION=abc123", "pf_session", "abc123")] // case-insensitive name
    [InlineData("pf_session=abc123", "missing", null)]
    [InlineData("", "pf_session", null)]
    [InlineData(null, "pf_session", null)]
    public void ParseCookie_extracts_named_value(string? header, string name, string? expected)
    {
        Assert.Equal(expected, SessionIds.ParseCookie(header, name));
    }

    [Fact]
    public void ParseCookie_trims_surrounding_whitespace()
    {
        Assert.Equal("v", SessionIds.ParseCookie("  pf_session = v  ", "pf_session"));
    }
}
