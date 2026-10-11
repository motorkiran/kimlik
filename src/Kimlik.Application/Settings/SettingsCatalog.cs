using Kimlik.Application.Accounts;
using Kimlik.Application.Branding;
using Kimlik.Application.Captcha;
using Kimlik.Application.Mfa;
using Kimlik.Application.Organizations;
using Kimlik.Application.Plans;
using Kimlik.Contracts.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Settings;

/// <summary>An options class whose settings can change at runtime, bound and validated as at startup.</summary>
public abstract class SettingsSection(string name)
{
    /// <summary>The configuration section, such as <c>Kimlik:Accounts</c>.</summary>
    public string Name { get; } = name;

    /// <summary>The options as <paramref name="configuration"/> sets them.</summary>
    internal abstract object Bind(IConfiguration configuration);

    /// <summary>What the validators that run at startup say about the options <paramref name="configuration"/> sets.</summary>
    internal abstract IReadOnlyList<string> Validate(IConfiguration configuration, IServiceProvider services);
}

internal sealed class SettingsSection<TOptions>(string name) : SettingsSection(name)
    where TOptions : class, new()
{
    internal override object Bind(IConfiguration configuration)
    {
        var options = new TOptions();
        configuration.GetSection(Name).Bind(options);
        return options;
    }

    internal override IReadOnlyList<string> Validate(IConfiguration configuration, IServiceProvider services)
    {
        TOptions options;
        try
        {
            options = (TOptions)Bind(configuration);
        }
        catch (InvalidOperationException exception)
        {
            return [exception.Message];
        }

        return
        [
            .. services.GetServices<IValidateOptions<TOptions>>()
                .Select(validator => validator.Validate(Options.DefaultName, options))
                .Where(result => result.Failed)
                .SelectMany(result => result.Failures ?? []),
        ];
    }
}

/// <summary>A product setting that can change at runtime, under its configuration path.</summary>
public sealed class SettingDefinition
{
    public required string Key { get; init; }

    /// <summary>What it is part of, for people: Branding, Accounts and so on.</summary>
    public required string Group { get; init; }

    public required SettingType Type { get; init; }

    public required string Description { get; init; }

    public IReadOnlyList<string> Choices { get; init; } = [];

    internal SettingsSection Section { get; init; } = null!;

    /// <summary>The value in effect, from the bound options, as configuration writes it.</summary>
    internal Func<object, string?> Read { get; init; } = null!;
}

/// <summary>
/// The product settings that can change at runtime (ADR 0002): those that the code reads anew for each request.
/// Settings that wire up infrastructure at startup stay in configuration.
/// </summary>
public static class SettingsCatalog
{
    private static readonly SettingsSection<BrandingOptions> Branding = new(BrandingOptions.SectionName);
    private static readonly SettingsSection<AccountOptions> Accounts = new(AccountOptions.SectionName);
    private static readonly SettingsSection<MfaOptions> Mfa = new(MfaOptions.SectionName);
    private static readonly SettingsSection<OrganizationOptions> Organizations = new(OrganizationOptions.SectionName);
    private static readonly SettingsSection<PlanOptions> Plans = new(PlanOptions.SectionName);
    private static readonly SettingsSection<CaptchaOptions> Captcha = new(CaptchaOptions.SectionName);

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        Entry(Branding, "ProductName", SettingType.Text, "The product's name on the hosted pages and in emails.", options => options.ProductName),
        Entry(Branding, "LogoUrl", SettingType.Url, "A logo above the forms and in emails; the product's name shows when there is none.", options => options.LogoUrl?.ToString()),
        Entry(Branding, "PrimaryColor", SettingType.Color, "The accent color of the hosted pages and emails.", options => options.PrimaryColor),

        Entry(Accounts, "Registration", SettingType.Choice, "Who can create an account: anyone, people with an invitation, or only administrators.",
            options => options.Registration.ToString(), Enum.GetNames<RegistrationMode>()),
        Entry(Accounts, "RequireVerifiedEmail", SettingType.Boolean, "People confirm their email address before they can sign in.", options => Text(options.RequireVerifiedEmail)),
        Entry(Accounts, "EmailSignIn", SettingType.Boolean, "People can sign in with codes sent by email, and have accounts without a password.", options => Text(options.EmailSignIn)),
        Entry(Accounts, "BreachedPasswordCheck", SettingType.Boolean, "New passwords that appeared in data breaches are refused (Have I Been Pwned).", options => Text(options.BreachedPasswordCheck)),
        Entry(Accounts, "PasswordMinimumLength", SettingType.Number, "The fewest characters a new password has, at least 8.", options => options.PasswordMinimumLength.ToString(System.Globalization.CultureInfo.InvariantCulture)),

        Entry(Mfa, "RequireForAdministrators", SettingType.Boolean, "People whose roles carry system permissions set up a second factor.", options => Text(options.RequireForAdministrators)),
        Entry(Mfa, "RequireForEveryone", SettingType.Boolean, "Everyone sets up a second factor.", options => Text(options.RequireForEveryone)),

        Entry(Organizations, "UsersCanCreate", SettingType.Boolean, "People can create organizations themselves.", options => Text(options.UsersCanCreate)),
        Entry(Organizations, "InvitationLifetime", SettingType.Duration, "How long an invitation to an organization stays open.",
            options => options.InvitationLifetime.ToString("c", System.Globalization.CultureInfo.InvariantCulture)),

        Entry(Plans, "DefaultUserPlan", SettingType.Plan, "The plan of users without a subscription.", options => options.DefaultUserPlan),
        Entry(Plans, "DefaultOrganizationPlan", SettingType.Plan, "The plan of organizations without a subscription.", options => options.DefaultOrganizationPlan),

        Entry(Captcha, "Provider", SettingType.Choice, "The CAPTCHA on sign-up, password reset and sign-in code forms.",
            options => options.Provider.ToString(), Enum.GetNames<CaptchaProvider>()),
        Entry(Captcha, "SiteKey", SettingType.Text, "The CAPTCHA provider's site key.", options => options.SiteKey),
        Entry(Captcha, "SecretKey", SettingType.Secret, "The CAPTCHA provider's secret key.", options => options.SecretKey),
    ];

    public static SettingDefinition? Find(string key) =>
        All.FirstOrDefault(definition => string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));

    private static SettingDefinition Entry<TOptions>(
        SettingsSection<TOptions> section, string property, SettingType type, string description, Func<TOptions, string?> read, IReadOnlyList<string>? choices = null)
        where TOptions : class, new() => new()
        {
            Key = $"{section.Name}:{property}",
            Group = section.Name["Kimlik:".Length..],
            Type = type,
            Description = description,
            Choices = choices ?? [],
            Section = section,
            Read = options => read((TOptions)options),
        };

    private static string Text(bool value) => value ? "true" : "false";
}
