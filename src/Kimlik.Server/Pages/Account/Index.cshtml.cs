using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Users;
using Kimlik.Server.SocialLogin;
using Kimlik.Server.Ui;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages.Account;

/// <summary>The account overview: the profile, and the way to the other account settings.</summary>
public sealed class ProfileModel(
    UserManager<User> userManager,
    MyAccount account,
    ExternalProviders providers,
    IOptions<SmsOptions> sms,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    private static readonly string[] IanaTimeZones =
        [.. TimeZoneInfo.GetSystemTimeZones().Where(zone => zone.HasIanaId).Select(zone => zone.Id).Order(StringComparer.Ordinal)];

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public string? Email { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>The account's phone number, when text messages are set up.</summary>
    public string? PhoneNumber { get; private set; }

    public bool CanUsePhone => sms.Value.Enabled;

    /// <summary>Whether accounts at other providers can be connected.</summary>
    public bool HasProviders => providers.All.Count > 0;

    /// <summary>The languages of the hosted pages, plus the user's current one if it is another.</summary>
    public IReadOnlyList<SelectListItem> Languages { get; private set; } = [];

    /// <summary>The IANA time zones of the system, plus the user's current one if it is another.</summary>
    public IReadOnlyList<SelectListItem> TimeZones { get; private set; } = [];

    public bool Saved { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        Input = new ProfileInput { GivenName = user.GivenName, FamilyName = user.FamilyName, Locale = user.Locale, TimeZone = user.TimeZone };
        Show(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        Show(user);
        if (Input.Locale is { Length: > 0 } locale && !Languages.Any(language => language.Value == locale))
        {
            ModelState.AddModelError("Input.Locale", localizer["Choose a language from the list."]);
        }

        if (Input.TimeZone is { Length: > 0 } timeZone && !TimeZones.Any(zone => zone.Value == timeZone))
        {
            ModelState.AddModelError("Input.TimeZone", localizer["Choose a time zone from the list."]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var updated = await account.UpdateProfileAsync(
            user.Id,
            new UpdateProfileRequest { GivenName = Input.GivenName, FamilyName = Input.FamilyName, Locale = Input.Locale, TimeZone = Input.TimeZone },
            cancellationToken);

        Saved = updated.IsSuccess;
        return Page();
    }

    private void Show(User user)
    {
        Email = user.Email;
        TwoFactorEnabled = user.TwoFactorEnabled;
        PhoneNumber = user.PhoneNumber;

        var languages = new List<SelectListItem> { new(localizer["Not set"], string.Empty) };
        languages.AddRange(HostedUiServiceCollectionExtensions.SupportedCultures.Select(culture =>
            new SelectListItem(culture.TextInfo.ToTitleCase(culture.NativeName), culture.Name)));

        if (user.Locale is { } current && !languages.Any(language => language.Value == current))
        {
            languages.Add(new SelectListItem(current, current));
        }

        Languages = languages;

        var timeZones = new List<SelectListItem> { new(localizer["Not set"], string.Empty) };
        timeZones.AddRange(IanaTimeZones.Select(zone => new SelectListItem(zone, zone)));
        if (user.TimeZone is { } currentZone && !IanaTimeZones.Contains(currentZone))
        {
            timeZones.Add(new SelectListItem(currentZone, currentZone));
        }

        TimeZones = timeZones;
    }
}

public sealed class ProfileInput
{
    [StringLength(User.NameMaxLength, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "First name")]
    public string? GivenName { get; set; }

    [StringLength(User.NameMaxLength, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Last name")]
    public string? FamilyName { get; set; }

    [Display(Name = "Language")]
    public string? Locale { get; set; }

    [Display(Name = "Time zone")]
    public string? TimeZone { get; set; }
}
