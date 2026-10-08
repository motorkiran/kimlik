using System.Diagnostics.CodeAnalysis;

namespace Kimlik.Domain.Common;

/// <summary>An expected failure: a stable, machine-readable code plus a message for people.</summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The domain is not consumed from Visual Basic.")]
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Failure,
}
