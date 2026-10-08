using System.Reflection;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Persistence;

namespace Kimlik.Architecture.Tests;

/// <summary>
/// Enforces the dependency rules from the design document (section 6.2). The compiler only records
/// references that code actually uses, so these checks catch real dependencies, not project files.
/// </summary>
public sealed class DependencyRuleTests
{
    private static readonly Assembly Domain = typeof(User).Assembly;
    private static readonly Assembly Infrastructure = typeof(KimlikDbContext).Assembly;

    [Fact]
    public void Domain_DependsOnNoOtherLayerOrFramework()
    {
        ReferencedAssemblies(Domain).ShouldNotContain(
            name => name.StartsWith("Kimlik.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal)
                || name.StartsWith("OpenIddict", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnTheHost()
    {
        ReferencedAssemblies(Infrastructure).ShouldNotContain("Kimlik.Server");
    }

    private static string[] ReferencedAssemblies(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)];
}
