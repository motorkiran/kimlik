using Kimlik.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Admin.Security;

/// <summary>
/// Runs Application handlers for the admin panel, each operation in a scope of its own: a circuit lasts long, a
/// database context should not. The operation acts as the signed-in administrator, once their permission for it is
/// checked, so the panel enforces the same permissions as the Management API.
/// </summary>
public sealed class AdminOperations(IServiceScopeFactory scopeFactory, AdminSession session)
{
    public static readonly Error MissingPermission = Error.Forbidden("admin.missing_permission", "Your roles do not allow this.");

    /// <param name="permission">The permission the operation takes, or <see langword="null"/> for what every administrator may see.</param>
    /// <param name="operation">The operation, given the services of its scope.</param>
    public async Task<T> RunAsync<T>(string? permission, Func<IServiceProvider, Task<T>> operation)
    {
        if (permission is not null && !session.Has(permission))
        {
            throw new UnauthorizedAccessException($"The administrator does not hold {permission}.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<AdminCaller>().Set(session);
        return await operation(scope.ServiceProvider);
    }

    /// <summary>Runs <typeparamref name="THandler"/>, resolved in the operation's scope.</summary>
    public Task<T> RunAsync<THandler, T>(string? permission, Func<THandler, Task<T>> operation)
        where THandler : notnull =>
        RunAsync(permission, services => operation(services.GetRequiredService<THandler>()));
}
