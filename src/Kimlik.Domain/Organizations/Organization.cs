using System.Text.RegularExpressions;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Organizations;

/// <summary>A group of users, such as a company, workspace or team. Members hold organization roles in it.</summary>
public sealed partial class Organization
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 64;

    // Used by EF Core.
    private Organization()
    {
    }

    public Guid Id { get; private init; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// A unique, URL-friendly handle such as <c>acme</c>, which clients may use instead of the ID. It can change,
    /// unlike the ID.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Organization> Create(string name, string slug, DateTimeOffset now)
    {
        var validation = Validate(name, slug);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new Organization
        {
            Id = Guid.CreateVersion7(now),
            Name = name.Trim(),
            Slug = slug,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public Result Update(string name, string slug, DateTimeOffset now)
    {
        var validation = Validate(name, slug);
        if (validation.IsFailure)
        {
            return validation;
        }

        Name = name.Trim();
        Slug = slug;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Lowercase letters, digits and inner hyphens. A slug never reads as an ID, so a reference to an organization
    /// is unambiguous.
    /// </summary>
    public static bool IsValidSlug(string? slug) =>
        slug is { Length: > 0 and <= SlugMaxLength } && SlugPattern().IsMatch(slug) && !Guid.TryParse(slug, out _);

    private static Result Validate(string? name, string? slug)
    {
        if (name is null || name.Trim().Length is 0 or > NameMaxLength)
        {
            return OrganizationErrors.InvalidName;
        }

        return IsValidSlug(slug) ? Result.Success() : OrganizationErrors.InvalidSlug;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
