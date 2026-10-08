using System.Net;

namespace Kimlik.Server.Tests.Diagnostics;

public sealed class HealthProbeTests(KimlikServerFixture server)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probe_ReturnsHealthy_WhenDatabaseIsReachable(string path)
    {
        using var client = server.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }
}
