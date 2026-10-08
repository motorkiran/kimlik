using Kimlik.Domain.Common;

namespace Kimlik.Application.Common;

public static class CommonErrors
{
    public static readonly Error InvalidCursor = Error.Validation("common.invalid_cursor", "The cursor is not valid for this list.");

    public static Error InvalidParameter(string name) => Error.Validation("common.invalid_parameter", $"The '{name}' parameter is not valid.");
}
