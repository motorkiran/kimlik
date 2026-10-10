using System.ComponentModel.DataAnnotations;

namespace Kimlik.Application.Accounts;

/// <summary>The provider that sends text messages, chosen with <c>Kimlik:Sms:Provider</c>.</summary>
public enum SmsProvider
{
    /// <summary>No texts: nothing offers phone numbers or SMS codes.</summary>
    None,
    Netgsm,
    IletiMerkezi,
    Twilio,
}

/// <summary>
/// Phone numbers and text messages, from the <c>Kimlik:Sms</c> section. Each provider's credentials have a section of
/// their own, such as <c>Kimlik:Sms:Netgsm</c>.
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Kimlik:Sms";

    public SmsProvider Provider { get; set; }

    /// <summary>
    /// The country calling code, such as <c>90</c>, of numbers written the national way, as <c>0532 123 45 67</c> or
    /// <c>532 123 45 67</c>. Without it, numbers must start with <c>+</c> or <c>00</c> and their country code.
    /// </summary>
    [RegularExpression("^[1-9][0-9]{0,2}$")]
    public string? DefaultCountryCode { get; set; }

    /// <summary>
    /// The country calling codes Kimlik sends texts to, such as <c>["90"]</c>; all when empty. Limiting them guards
    /// against SMS pumping, where texts to premium numbers abroad run up the bill.
    /// </summary>
    public List<string> AllowedCountryCodes { get; set; } = [];

    public bool Enabled => Provider != SmsProvider.None;
}
