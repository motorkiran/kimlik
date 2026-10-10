using System.Text;

namespace Kimlik.Application.Accounts;

/// <summary>Reads phone numbers as people write them into E.164, the form Kimlik keeps and sends to: <c>+905321234567</c>.</summary>
public static class PhoneNumbers
{
    /// <summary>
    /// The number in E.164, or <see langword="null"/> when it is not one. Spaces, dashes, dots and brackets are ignored;
    /// <c>00</c> stands for <c>+</c>; and with <paramref name="defaultCountryCode"/>, a number without a country code, with
    /// or without the leading <c>0</c>, is in that country.
    /// </summary>
    public static string? Normalize(string? input, string? defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new StringBuilder(input.Length);
        foreach (var character in input.Trim())
        {
            if (char.IsAsciiDigit(character) || (character == '+' && digits.Length == 0))
            {
                digits.Append(character);
            }
            else if (!char.IsWhiteSpace(character) && character is not ('-' or '.' or '(' or ')'))
            {
                return null;
            }
        }

        var number = digits.ToString();
        number = number switch
        {
            ['+', ..] => number,
            ['0', '0', ..] => "+" + number[2..],
            _ when defaultCountryCode is null => null,
            ['0', ..] => "+" + defaultCountryCode + number[1..],
            _ => "+" + defaultCountryCode + number,
        };

        // A country code and the number: 8 to 15 digits, none of which is a leading zero.
        return number is { Length: >= 9 and <= 16 } && number[1] != '0' && number.Skip(1).All(char.IsAsciiDigit) ? number : null;
    }

    /// <summary>Whether texts may go to the number, an E.164 one, under <see cref="SmsOptions.AllowedCountryCodes"/>.</summary>
    public static bool IsAllowed(string number, SmsOptions options) =>
        options.AllowedCountryCodes.Count == 0 || options.AllowedCountryCodes.Exists(code => number.StartsWith("+" + code, StringComparison.Ordinal));
}
