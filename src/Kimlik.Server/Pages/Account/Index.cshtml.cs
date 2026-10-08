using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Users;
using Kimlik.Server.Ui;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages.Account;

/// <summary>The account overview: the profile, and the way to the other account settings.</summary>
public sealed class ProfileModel(UserManager<User> userManager, MyAccount account, IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public string? Email { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>The languages of the hosted pages, plus the user's current one if it is another.</summary>
    public IReadOnlyList<SelectListItem> Languages { get; private set; } = [];

    public bool Saved { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await userManager.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        Input = new ProfileInput { GivenName = user.GivenName, FamilyName = user.FamilyName, Locale = user.Locale };
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

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var updated = await account.UpdateProfileAsync(
            user.Id,
            new UpdateUserRequest { GivenName = Input.GivenName, FamilyName = Input.FamilyName, Locale = Input.Locale },
            cancellationToken);

        Saved = updated.IsSuccess;
        return Page();
    }

    private void Show(User user)
    {
        Email = user.Email;
        TwoFactorEnabled = user.TwoFactorEnabled;

        var languages = new List<SelectListItem> { new(localizer["Not set"], string.Empty) };
        languages.AddRange(HostedUiServiceCollectionExtensions.SupportedCultures.Select(culture =>
            new SelectListItem(culture.TextInfo.ToTitleCase(culture.NativeName), culture.Name)));

        if (user.Locale is { } current && !languages.Any(language => language.Value == current))
        {
            languages.Add(new SelectListItem(current, current));
        }

        Languages = languages;
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
}
