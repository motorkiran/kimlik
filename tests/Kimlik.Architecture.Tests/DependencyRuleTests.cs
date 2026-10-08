using System.Reflection;
using Kimlik.Application.Users;
using Kimlik.AspNetCore;
using Kimlik.Client;
using Kimlik.Contracts;
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
    private static readonly Assembly Contracts = typeof(KimlikScopes).Assembly;
    private static readonly Assembly Application = typeof(UserErrors).Assembly;
    private static readonly Assembly Infrastructure = typeof(KimlikDbContext).Assembly;
    private static readonly Assembly Client = typeof(KimlikClient).Assembly;
    private static readonly Assembly AspNetCore = typeof(KimlikUser).Assembly;

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
    public void Contracts_DependOnNothingButTheRuntime()
    {
        ReferencedAssemblies(Contracts).ShouldAllBe(name => name.StartsWith("System", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_DoesNotDependOnTheWebOrTheDatabaseProvider()
    {
        ReferencedAssemblies(Application).ShouldNotContain(
            name => name == "Kimlik.Infrastructure"
                || name == "Kimlik.Server"
                || name == "Microsoft.AspNetCore.Http.Abstractions"
                || name.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnTheHost()
    {
        ReferencedAssemblies(Infrastructure).ShouldNotContain("Kimlik.Server");
    }

    [Fact]
    public void Client_SharesOnlyTheContracts()
    {
        KimlikReferences(Client).ShouldBe(["Kimlik.Contracts"]);
    }

    [Fact]
    public void AspNetCore_SharesOnlyTheContractsAndTheClient()
    {
        KimlikReferences(AspNetCore).ShouldAllBe(name => name == "Kimlik.Contracts" || name == "Kimlik.Client");
    }

    private static string[] KimlikReferences(Assembly assembly) =>
        [.. ReferencedAssemblies(assembly).Where(name => name.StartsWith("Kimlik.", StringComparison.Ordinal))];

    private static string[] ReferencedAssemblies(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)];
}
