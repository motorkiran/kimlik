using Kimlik.Contracts;
using Kimlik.Server.Api;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Api;

public sealed class ManagementApiTests(KimlikServerFixture server)
{
    [Fact]
    public void EveryEndpoint_RequiresASystemPermission_OrASignedInUser()
    {
        var endpoints = server.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(ManagementApi.BasePath, StringComparison.Ordinal) == true)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        endpoints.ShouldAllBe(endpoint =>
            endpoint.Metadata.GetMetadata<RequiredPermission>() != null || endpoint.Metadata.GetMetadata<RequiredSignedInUser>() != null);
    }

    [Fact]
    public void OnlyOrganizationAndInvitationEndpoints_TakeTokensForTheOrganizationsScope()
    {
        var open = server.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<RequiredSignedInUser>()?.Audiences.Contains(KimlikScopes.Organizations) == true)
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .ToList();

        open.ShouldNotBeEmpty();
        open.ShouldAllBe(route =>
            (route.StartsWith("/api/v1/me/organizations", StringComparison.Ordinal) || route.StartsWith("/api/v1/me/invitations", StringComparison.Ordinal))
            && !route.Contains("api-keys", StringComparison.Ordinal));
    }
}
