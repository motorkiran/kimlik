using Kimlik.Server.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Identity;

/// <summary>
/// Passkeys belong to the host of <see cref="ServerOptions.PublicUrl"/>, and only its origin may use them. They must
/// verify the user, with a PIN or biometric, so that a passkey counts as two factors, and be discoverable, so that
/// people can sign in without typing their address. No attestation is asked for: any authenticator will do.
/// </summary>
internal sealed class ConfigurePasskeys(IOptions<ServerOptions> server) : IConfigureOptions<IdentityPasskeyOptions>
{
    public void Configure(IdentityPasskeyOptions options)
    {
        var publicUrl = server.Value.PublicUrl!;
        var origin = publicUrl.GetLeftPart(UriPartial.Authority);

        options.ServerDomain = publicUrl.Host;
        options.UserVerificationRequirement = "required";
        options.ResidentKeyRequirement = "required";
        options.AttestationConveyancePreference = "none";
        options.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && string.Equals(context.Origin, origin, StringComparison.Ordinal));
    }
}
