using System.Net.Http.Headers;
using System.Text;
using Kimlik.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Sms;

/// <summary>
/// Sends texts through Twilio's Messaging API (<c>POST /2010-04-01/Accounts/{AccountSid}/Messages.json</c>, basic
/// authentication with the account SID and auth token), from a number or a messaging service. Twilio answers
/// <c>201</c> when it queued the text, and an error with a code, such as <c>21211</c> for an invalid number, when not.
/// </summary>
internal sealed class TwilioSmsSender(IHttpClientFactory httpClients, IOptions<TwilioOptions> options) : ISmsSender
{
    public static Uri EndpointFor(string accountSid) => new($"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages.json");

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var fields = new List<KeyValuePair<string, string>> { new("To", message.To), new("Body", message.Text) };
        fields.Add(string.IsNullOrEmpty(settings.MessagingServiceSid) ? new("From", settings.From!) : new("MessagingServiceSid", settings.MessagingServiceSid));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointFor(settings.AccountSid!)) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));

        using var response = await httpClients.CreateClient(SmsSenders.HttpClientName).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var answer = await SmsSenders.ReadJsonAsync(response, cancellationToken);
            var code = answer?.TryGetProperty("code", out var value) == true ? value.ToString() : "none";
            var reason = answer?.TryGetProperty("message", out var text) == true ? $" ({text})" : null;
            throw new SmsDeliveryException("Twilio", $"HTTP {(int)response.StatusCode}, code {code}{reason}.");
        }
    }
}
