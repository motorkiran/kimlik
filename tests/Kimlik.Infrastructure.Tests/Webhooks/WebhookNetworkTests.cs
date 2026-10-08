using System.Net;
using Kimlik.Infrastructure.Webhooks;

namespace Kimlik.Infrastructure.Tests.Webhooks;

public sealed class WebhookNetworkTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.100.100.200")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void PrivateAndLocalAddresses_AreNotPublic(string address) =>
        WebhookNetwork.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void InternetAddresses_ArePublic(string address) =>
        WebhookNetwork.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();

    [Fact]
    public async Task Sender_RefusesToConnectToThisMachine()
    {
        using var client = new HttpClient(new SocketsHttpHandler { ConnectCallback = WebhookNetwork.Connect(allowPrivateNetworks: false) });

        var failure = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://localhost:9/"), TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("private network");
    }
}
