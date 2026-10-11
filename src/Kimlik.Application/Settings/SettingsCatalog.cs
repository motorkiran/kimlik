using Kimlik.Application.Accounts;
using Kimlik.Application.Branding;
using Kimlik.Application.Captcha;
using Kimlik.Application.Interactions;
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
    private static readonly SettingsSection<SocialLoginOptions> Social = new(SocialLoginOptions.SectionName);
    private static readonly SettingsSection<InteractionOptions> Interactions = new(InteractionOptions.SectionName);

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

        .. Provider("Google", options => options.Google, "Sign-in with Google appears once the client ID is set."),
        .. Provider("Microsoft", options => options.Microsoft, "Sign-in with Microsoft appears once the client ID is set."),
        Entry(Social, "Microsoft:Tenant", SettingType.Text, "Whose Microsoft accounts can sign in: common, organizations, consumers or one tenant's ID.",
            options => options.Microsoft.Tenant, group: "Microsoft sign-in"),
        .. Provider("GitHub", options => options.GitHub, "Sign-in with GitHub appears once the client ID is set."),
        Entry(Social, "Apple:ClientId", SettingType.Text, "Sign-in with Apple appears once its Services ID is set here, with the team, key ID and key.",
            options => options.Apple.ClientId, group: "Apple sign-in"),
        Entry(Social, "Apple:TeamId", SettingType.Text, "The team of the Apple developer account.", options => options.Apple.TeamId, group: "Apple sign-in"),
        Entry(Social, "Apple:KeyId", SettingType.Text, "The ID of the key that signs Kimlik's client secret for Apple.", options => options.Apple.KeyId, group: "Apple sign-in"),
        Entry(Social, "Apple:PrivateKey", SettingType.Secret, "The PEM text of the .p8 key Apple issued.", options => options.Apple.PrivateKey, group: "Apple sign-in"),
        Entry(Social, "Apple:TrustEmail", SettingType.Boolean, "Addresses Apple verified count as verified.", options => Text(options.Apple.TrustEmail), group: "Apple sign-in"),

        Entry(Interactions, "LoginUrl", SettingType.Url, "A login app of your own that signs people in instead of Kimlik's pages, with login challenges.",
            options => options.LoginUrl?.ToString(), group: "Login app"),
    ];

    public static SettingDefinition? Find(string key) =>
        All.FirstOrDefault(definition => string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What the validators that run at startup say about the options of the sections that <paramref name="keys"/> belong to,
    /// as <paramref name="configuration"/> sets them: the failures of each section they reject, by its name.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Validate(IConfiguration configuration, IEnumerable<string> keys, IServiceProvider services) =>
        keys.Select(Find).OfType<SettingDefinition>().Select(definition => definition.Section).Distinct()
            .Select(section => (section.Name, Failures: section.Validate(configuration, services)))
            .Where(result => result.Failures.Count > 0)
            .ToDictionary(result => result.Name, result => result.Failures, StringComparer.OrdinalIgnoreCase);

    private static SettingDefinition Entry<TOptions>(
        SettingsSection<TOptions> section, string property, SettingType type, string description, Func<TOptions, string?> read, IReadOnlyList<string>? choices = null, string? group = null)
        where TOptions : class, new() => new()
        {
            Key = $"{section.Name}:{property}",
            Group = group ?? section.Name["Kimlik:".Length..],
            Type = type,
            Description = description,
            Choices = choices ?? [],
            Section = section,
            Read = options => read((TOptions)options),
        };

    /// <summary>The client ID, secret and email trust of a social provider.</summary>
    private static SettingDefinition[] Provider(string name, Func<SocialLoginOptions, SocialProviderOptions> provider, string description)
    {
        var group = $"{name} sign-in";
        return
        [
            Entry(Social, $"{name}:ClientId", SettingType.Text, description, options => provider(options).ClientId, group: group),
            Entry(Social, $"{name}:ClientSecret", SettingType.Secret, $"Kimlik's client secret at {name}.", options => provider(options).ClientSecret, group: group),
            Entry(Social, $"{name}:TrustEmail", SettingType.Boolean, $"Addresses {name} verified count as verified, so new accounts skip email verification.",
                options => Text(provider(options).TrustEmail), group: group),
        ];
    }

    private static string Text(bool value) => value ? "true" : "false";
}
