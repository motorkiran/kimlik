using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Application.Settings;

public static class SettingsErrors
{
    public static Error Unknown(string key) => Error.Validation("settings.unknown", $"'{key}' is not a setting that can change at runtime.");

    public static Error Fixed(string key) =>
        Error.Conflict("settings.fixed", $"'{key}' is set in the environment, which fixes it; change it there.");

    public static Error InvalidValue(string key, string expected) => Error.Validation("settings.invalid_value", $"'{key}' takes {expected}.");

    public static Error Invalid(IEnumerable<string> failures) => Error.Validation("settings.invalid", string.Join(" ", failures));
}

/// <summary>The secrets among the stored settings, encrypted with the master key and bound to their setting.</summary>
public static class StoredSettingSecrets
{
    public const string Purpose = "settings.secret";

    public static string Encrypt(ISecretEncryption encryption, string key, string value) => encryption.Encrypt(value, Purpose, OwnerOf(key));

    public static string Decrypt(ISecretEncryption encryption, Setting setting) =>
        setting.IsSecret ? encryption.Decrypt(setting.Value, Purpose, OwnerOf(setting.Key)) : setting.Value;

    private static Guid OwnerOf(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key))[..16]);
}

/// <summary>Every setting that can change at runtime, with its value in effect and where that comes from.</summary>
public sealed class GetSettingsHandler(IConfiguration configuration, IStoredSettings stored)
{
    public IReadOnlyList<SettingResponse> Handle() => Describe(configuration, stored);

    internal static List<SettingResponse> Describe(IConfiguration configuration, IStoredSettings stored)
    {
        var bound = SettingsCatalog.All.Select(definition => definition.Section).Distinct().ToDictionary(section => section, section => section.Bind(configuration));
        return
        [
            .. SettingsCatalog.All.Select(definition =>
            {
                var value = definition.Read(bound[definition.Section]);
                var source = stored.SourceOf(definition.Key);
                var secret = definition.Type == SettingType.Secret;
                return new SettingResponse(
                    definition.Key,
                    definition.Group,
                    definition.Type,
                    definition.Description,
                    secret ? null : value,
                    !string.IsNullOrEmpty(value),
                    source,
                    source != SettingSource.Environment,
                    definition.Choices);
            }),
        ];
    }
}

/// <summary>
/// Changes settings at runtime. Each value is checked against its setting, and the result as a whole against the
/// validators that run at startup, before anything is saved; settings fixed in the environment cannot change. The
/// settings include security policies, so changing them takes every installation-wide system permission.
/// </summary>
public sealed partial class UpdateSettingsHandler(
    IKimlikDbContext context,
    IConfiguration configuration,
    IStoredSettings stored,
    AccessGuard guard,
    ISecretEncryption encryption,
    IAuditLog auditLog,
    IServiceProvider services,
    TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<SettingResponse>>> HandleAsync(UpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        var allowed = guard.EnsureHoldsAllSystemPermissions();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var changes = new Dictionary<SettingDefinition, string?>();
        foreach (var (key, value) in request.Values)
        {
            if (SettingsCatalog.Find(key) is not { } definition)
            {
                return SettingsErrors.Unknown(key);
            }

            if (stored.SourceOf(definition.Key) == SettingSource.Environment)
            {
                return SettingsErrors.Fixed(definition.Key);
            }

            var normalized = await NormalizeAsync(definition, value, cancellationToken);
            if (normalized.IsFailure)
            {
                return normalized.Error;
            }

            changes[definition] = normalized.Value;
        }

        var rows = await context.Settings.ToListAsync(cancellationToken);
        var values = rows.ToDictionary(row => row.Key, row => StoredSettingSecrets.Decrypt(encryption, row), StringComparer.OrdinalIgnoreCase);
        foreach (var (definition, value) in changes)
        {
            if (value is null)
            {
                values.Remove(definition.Key);
            }
            else
            {
                values[definition.Key] = value;
            }
        }

        var candidate = stored.WithStored(values);
        var failures = changes.Keys.Select(definition => definition.Section).Distinct().SelectMany(section => section.Validate(candidate, services)).ToList();
        if (failures.Count > 0)
        {
            return SettingsErrors.Invalid(failures);
        }

        var now = timeProvider.GetUtcNow();
        foreach (var (definition, value) in changes)
        {
            var row = rows.SingleOrDefault(candidate => string.Equals(candidate.Key, definition.Key, StringComparison.OrdinalIgnoreCase));
            var secret = definition.Type == SettingType.Secret;
            var storedValue = value is not null && secret ? StoredSettingSecrets.Encrypt(encryption, definition.Key, value) : value;
            if (storedValue is null)
            {
                if (row is not null)
                {
                    context.Settings.Remove(row);
                }
            }
            else if (row is null)
            {
                context.Settings.Add(Setting.Create(definition.Key, storedValue, secret, now));
            }
            else
            {
                row.Change(storedValue, now);
            }
        }

        auditLog.Record(
            AuditActions.SettingsUpdated,
            data: new Dictionary<string, object?>
            {
                ["settings"] = changes.ToDictionary(change => change.Key.Key, change => change.Key.Type == SettingType.Secret && change.Value is not null ? "(secret)" : change.Value),
            });
        await context.SaveChangesAsync(cancellationToken);
        await stored.ReloadAsync(cancellationToken);
        return GetSettingsHandler.Describe(configuration, stored);
    }

    /// <summary>The value as configuration reads it, or <see langword="null"/> to return the setting to its default.</summary>
    private async Task<Result<string?>> NormalizeAsync(SettingDefinition definition, string? value, CancellationToken cancellationToken)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return (string?)null;
        }

        var key = definition.Key;
        switch (definition.Type)
        {
            case SettingType.Boolean:
                return bool.TryParse(value, out var flag) ? flag ? "true" : "false" : SettingsErrors.InvalidValue(key, "true or false");

            case SettingType.Number:
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : SettingsErrors.InvalidValue(key, "a whole number");

            case SettingType.Duration:
                return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) && duration > TimeSpan.Zero
                    ? duration.ToString("c", CultureInfo.InvariantCulture)
                    : SettingsErrors.InvalidValue(key, "a positive duration such as 7.00:00:00");

            case SettingType.Url:
                return Uri.TryCreate(value, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) && value.Length <= 2048
                    ? url.AbsoluteUri
                    : SettingsErrors.InvalidValue(key, "an absolute HTTP or HTTPS URL");

            case SettingType.Color:
                return ColorPattern().IsMatch(value) ? value.ToLowerInvariant() : SettingsErrors.InvalidValue(key, "a color such as #4f46e5");

            case SettingType.Choice:
                return definition.Choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase)) is { } chosen
                    ? chosen
                    : SettingsErrors.InvalidValue(key, $"one of {string.Join(", ", definition.Choices)}");

            case SettingType.Plan:
                return await context.Plans.AnyAsync(plan => plan.Key == value && plan.Kind == Domain.Plans.PlanKind.Base && !plan.IsArchived, cancellationToken)
                    ? value
                    : SettingsErrors.InvalidValue(key, "the key of a plan that is not an add-on or archived");

            default:
                return value.Length <= 1024 ? value : SettingsErrors.InvalidValue(key, "at most 1024 characters");
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
