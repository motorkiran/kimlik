using System.Net;

namespace Kimlik.Client;

/// <summary>
/// An error returned by Kimlik. <see cref="Code"/> is stable and machine-readable, such as <c>user.not_found</c>;
/// <see cref="Errors"/> lists invalid request fields.
/// </summary>
public sealed class KimlikApiException : Exception
{
    public KimlikApiException(HttpStatusCode statusCode, string? code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public HttpStatusCode StatusCode { get; }

    public string? Code { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
