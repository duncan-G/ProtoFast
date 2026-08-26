using ProtoFast.Auth.Api.Accounts;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// The code is the only thing standing between a session and the address that account signs in
/// with, so the two properties that matter are that it is genuinely six digits of entropy and
/// that a near miss is not a hit.
/// </summary>
public class EmailChangeCodeTests
{
    [Fact]
    public void A_code_is_always_six_digits()
    {
        // Leading zeros are the case worth covering: formatted wrong, one code in ten is short,
        // and the user typing it back gets told they mistyped.
        for (var i = 0; i < 2_000; i++)
        {
            var code = EmailChangeCode.Generate();

            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.InRange(c, '0', '9'));
        }
    }

    [Fact]
    public void The_mailed_code_matches_and_a_wrong_one_does_not()
    {
        var pending = Pending("123456");

        Assert.True(EmailChangeCode.Matches(pending, "123456"));
        Assert.False(EmailChangeCode.Matches(pending, "123457"));
        Assert.False(EmailChangeCode.Matches(pending, ""));
    }

    /// <summary>
    /// The salt is what keeps a stored digest from being a lookup into a table of the million
    /// possible codes, so the same code under a different salt has to hash differently.
    /// </summary>
    [Fact]
    public void The_same_code_hashes_differently_per_request()
    {
        var first = Pending("123456");
        var second = Pending("123456");

        Assert.NotEqual(first.CodeSalt, second.CodeSalt);
        Assert.NotEqual(first.CodeHash, second.CodeHash);
    }

    [Fact]
    public void The_code_itself_is_never_stored()
    {
        var pending = Pending("123456");

        Assert.DoesNotContain("123456", pending.CodeHash, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", pending.CodeSalt, StringComparison.Ordinal);
    }

    private static PendingEmailChange Pending(string code)
    {
        var salt = EmailChangeCode.NewSalt();
        var now = DateTimeOffset.UnixEpoch;
        return new PendingEmailChange(
            "new@example.com", salt, EmailChangeCode.Hash(salt, code), now, now + EmailChangeCode.Lifetime);
    }
}

/// <summary>
/// The normalised address becomes the account's username as well as its email, and is compared
/// against the address already on the account to decide whether anything is changing at all. Both
/// jobs need one spelling per mailbox.
/// </summary>
public class EmailAddressTests
{
    [Theory]
    [InlineData("ada@example.com", "ada@example.com")]
    [InlineData("  ada@example.com  ", "ada@example.com")]
    [InlineData("Ada@Example.COM", "ada@example.com")]
    public void An_address_is_trimmed_and_lowercased(string raw, string expected)
    {
        Assert.True(EmailAddress.TryNormalize(raw, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("ada@")]
    [InlineData("@example.com")]
    // A display name would have the endpoint mail one address and store another.
    [InlineData("Ada <ada@example.com>")]
    public void Anything_that_is_not_a_bare_address_is_refused(string? raw)
    {
        Assert.False(EmailAddress.TryNormalize(raw, out var email));
        Assert.Equal("", email);
    }

    [Fact]
    public void An_address_past_the_rfc_ceiling_is_refused()
    {
        var tooLong = new string('a', EmailAddress.MaxLength) + "@example.com";

        Assert.False(EmailAddress.TryNormalize(tooLong, out _));
    }
}
