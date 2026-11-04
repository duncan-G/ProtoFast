using System.Net;
using ProtoFast.Api.Services.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class PublicAddressesTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2607:f8b0:4005:80a::200e", true)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::8.8.8.8", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00:ec2::254", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("64:ff9b::169.254.169.254", false)]
    public void Only_addresses_on_the_public_internet_are_reached(string address, bool reached) =>
        Assert.Equal(reached, PublicAddresses.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://localhost:9/")]
    public async Task A_link_into_this_host_is_refused_before_connecting(string url)
    {
        using var http = new HttpClient(PublicAddresses.Handler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url, TestContext.Current.CancellationToken));

        Assert.Contains("isn’t on the public internet", error.Message);
    }
}
