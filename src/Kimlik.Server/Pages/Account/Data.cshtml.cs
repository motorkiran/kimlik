using System.Text.Json;
using Kimlik.Application.Users;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// Downloads what Kimlik holds about the user, as one JSON file. It takes a recent sign-in, so that someone holding a
/// stolen session cannot take the account's data with it.
/// </summary>
public sealed class DataModel(PersonalDataExporter exporter, IOptions<HttpJsonOptions> json, TimeProvider timeProvider) : AccountPageModel
{
    public bool SignedInRecently { get; private set; }

    public async Task OnGetAsync() => SignedInRecently = await IsSignedInRecentlyAsync();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        SignedInRecently = await IsSignedInRecentlyAsync();
        if (!SignedInRecently || await exporter.ExportAsync(UserId, withPrivateMetadata: false, cancellationToken) is not { IsSuccess: true } export)
        {
            return Page();
        }

        var file = JsonSerializer.SerializeToUtf8Bytes(export.Value, json.Value.SerializerOptions);
        return File(file, "application/json", $"kimlik-data-{export.Value.ExportedAt:yyyy-MM-dd}.json");
    }

    private async Task<bool> IsSignedInRecentlyAsync() =>
        SignInFlow.SignedInRecently(await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme), timeProvider.GetUtcNow());
}
