using System.Buffers;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Kimlik.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Sms;

/// <summary>
/// Sends texts through Netgsm's REST v2 API (<c>POST /sms/rest/v2/send</c>, basic authentication), as informational
/// messages outside İYS, with Turkish characters when the text has them. Netgsm answers <c>code</c> <c>00</c> when it
/// queued the text, and another code, such as <c>30</c> for wrong credentials, when it did not.
/// </summary>
internal sealed class NetgsmSmsSender(IHttpClientFactory httpClients, IOptions<NetgsmOptions> options) : ISmsSender
{
    public static readonly Uri Endpoint = new("https://api.netgsm.com.tr/sms/rest/v2/send");

    private static readonly SearchValues<char> TurkishCharacters = SearchValues.Create("çğıöşüÇĞİÖŞÜ");

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var body = new Dictionary<string, object?>
        {
            ["msgheader"] = settings.Header,
            ["messages"] = new[] { new Dictionary<string, string> { ["msg"] = message.Text, ["no"] = message.To.TrimStart('+') } },
            ["iysfilter"] = "0",
        };

        if (message.Text.AsSpan().IndexOfAny(TurkishCharacters) >= 0)
        {
            body["encoding"] = "TR";
        }

        if (!string.IsNullOrEmpty(settings.AppName))
        {
            body["appname"] = settings.AppName;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}")));

        using var response = await httpClients.CreateClient(SmsSenders.HttpClientName).SendAsync(request, cancellationToken);
        var answer = await SmsSenders.ReadJsonAsync(response, cancellationToken);
        var code = answer?.TryGetProperty("code", out var value) == true ? value.ToString() : null;
        if (!response.IsSuccessStatusCode || code != "00")
        {
            var description = answer?.TryGetProperty("description", out var text) == true ? text.ToString() : null;
            throw new SmsDeliveryException("Netgsm", $"HTTP {(int)response.StatusCode}, code {code ?? "none"}{(description is null ? null : $" ({description})")}.");
        }
    }
}
