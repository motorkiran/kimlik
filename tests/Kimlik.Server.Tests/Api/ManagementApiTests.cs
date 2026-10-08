using Kimlik.Server.Api;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Api;

public sealed class ManagementApiTests(KimlikServerFixture server)
{
    [Fact]
    public void EveryEndpoint_RequiresASystemPermission()
    {
        var endpoints = server.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(ManagementApi.BasePath, StringComparison.Ordinal) == true)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        endpoints.ShouldAllBe(endpoint => endpoint.Metadata.GetMetadata<RequiredPermission>() != null);
    }
}
