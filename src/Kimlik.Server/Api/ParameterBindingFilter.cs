using Kimlik.Domain.Common;

namespace Kimlik.Server.Api;

/// <summary>
/// Stops a request whose route or query values could not be read. Minimal APIs mark such requests with a 400
/// status but, once an endpoint has filters (validation adds one), still run the handler, whose result would
/// replace the status: a mistyped filter would be silently ignored.
/// </summary>
internal sealed class ParameterBindingFilter : IEndpointFilter
{
    private static readonly Error InvalidParameter = Error.Validation(InvalidRequestCode, "A route or query parameter is not valid.");

    /// <summary>The code of every malformed request, whether a parameter or the body could not be read.</summary>
    public const string InvalidRequestCode = "request.invalid";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Response.StatusCode == StatusCodes.Status400BadRequest
            ? ApiResults.Problem(InvalidParameter)
            : await next(context);
}
