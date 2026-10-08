using Kimlik.Domain.Common;

namespace Kimlik.Application.Users;

public static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("user.not_found", "The user does not exist.");
}
