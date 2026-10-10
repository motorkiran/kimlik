using Kimlik.Application.Accounts;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Sms;

/// <summary>Netgsm's REST v2 API, from <c>Kimlik:Sms:Netgsm</c>: the subscriber's API user and an approved sender name.</summary>
public sealed class NetgsmOptions
{
    public const string SectionName = "Kimlik:Sms:Netgsm";

    /// <summary>The subscriber number or API sub-user (<c>usercode</c>).</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>The sender name (<c>msgheader</c>), as approved in the Netgsm panel.</summary>
    public string? Header { get; set; }

    /// <summary>An optional application name Netgsm shows in its reports (<c>appname</c>).</summary>
    public string? AppName { get; set; }
}

/// <summary>İleti Merkezi's JSON API, from <c>Kimlik:Sms:IletiMerkezi</c>.</summary>
public sealed class IletiMerkeziOptions
{
    public const string SectionName = "Kimlik:Sms:IletiMerkezi";

    /// <summary>The API key, from Settings, Security, API access in the İleti Merkezi panel.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The hash the panel shows next to the key; it is used as it is.</summary>
    public string? ApiHash { get; set; }

    /// <summary>The sender name, as approved in the panel.</summary>
    public string? Sender { get; set; }
}

/// <summary>Twilio's Messaging API, from <c>Kimlik:Sms:Twilio</c>.</summary>
public sealed class TwilioOptions
{
    public const string SectionName = "Kimlik:Sms:Twilio";

    public string? AccountSid { get; set; }

    public string? AuthToken { get; set; }

    /// <summary>The number texts come from, in E.164; or use <see cref="MessagingServiceSid"/>.</summary>
    public string? From { get; set; }

    /// <summary>A messaging service that picks the sender, instead of <see cref="From"/>.</summary>
    public string? MessagingServiceSid { get; set; }
}

/// <summary>Fails at startup when the chosen provider is missing a setting it needs.</summary>
internal sealed class ValidateSmsProvider(IOptions<NetgsmOptions> netgsm, IOptions<IletiMerkeziOptions> iletiMerkezi, IOptions<TwilioOptions> twilio)
    : IValidateOptions<SmsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsOptions options)
    {
        var missing = options.Provider switch
        {
            SmsProvider.Netgsm => Missing(NetgsmOptions.SectionName, ("Username", netgsm.Value.Username), ("Password", netgsm.Value.Password), ("Header", netgsm.Value.Header)),
            SmsProvider.IletiMerkezi => Missing(
                IletiMerkeziOptions.SectionName, ("ApiKey", iletiMerkezi.Value.ApiKey), ("ApiHash", iletiMerkezi.Value.ApiHash), ("Sender", iletiMerkezi.Value.Sender)),
            SmsProvider.Twilio => Missing(
                TwilioOptions.SectionName,
                ("AccountSid", twilio.Value.AccountSid),
                ("AuthToken", twilio.Value.AuthToken),
                ("From or MessagingServiceSid", twilio.Value.From ?? twilio.Value.MessagingServiceSid)),
            _ => [],
        };

        return missing.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail($"Texts through {options.Provider} need {string.Join(", ", missing)}.");
    }

    private static List<string> Missing(string section, params (string Name, string? Value)[] settings) =>
        [.. settings.Where(setting => string.IsNullOrWhiteSpace(setting.Value)).Select(setting => $"{section}:{setting.Name}")];
}
