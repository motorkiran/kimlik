using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Kimlik.Server.Tests.ErrorHandling;

public sealed class ProblemDetailsTests(KimlikServerFixture server)
{
    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)HttpStatusCode.NotFound);
        problem.Extensions.ShouldContainKey("traceId");
    }
}
