using System.Net.Http.Json;
using Kimlik.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Sms;

/// <summary>
/// Sends texts through İleti Merkezi's JSON API (<c>POST /v1/send-sms/json</c>), with the API key and the hash from the
/// panel, as informational messages outside İYS. İleti Merkezi answers status <c>200</c> when it took the order, and
/// another status, such as <c>401</c> for wrong credentials, when it did not.
/// </summary>
internal sealed class IletiMerkeziSmsSender(IHttpClientFactory httpClients, IOptions<IletiMerkeziOptions> options) : ISmsSender
{
    public static readonly Uri Endpoint = new("https://api.iletimerkezi.com/v1/send-sms/json");

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var body = new
        {
            request = new
            {
                authentication = new { key = settings.ApiKey, hash = settings.ApiHash },
                order = new
                {
                    sender = settings.Sender,
                    iys = "0",
                    // The API spells it "receipents".
                    message = new { text = message.Text, receipents = new { number = new[] { message.To } } },
                },
            },
        };

        using var response = await httpClients.CreateClient(SmsSenders.HttpClientName).PostAsJsonAsync(Endpoint, body, cancellationToken);
        var answer = await SmsSenders.ReadJsonAsync(response, cancellationToken);
        var status = answer?.TryGetProperty("response", out var inner) == true && inner.TryGetProperty("status", out var value) ? value : default;
        var code = status.ValueKind == System.Text.Json.JsonValueKind.Object && status.TryGetProperty("code", out var number) ? number.ToString() : null;
        if (!response.IsSuccessStatusCode || code != "200")
        {
            var reason = status.ValueKind == System.Text.Json.JsonValueKind.Object && status.TryGetProperty("message", out var text) ? text.ToString() : null;
            throw new SmsDeliveryException("İleti Merkezi", $"HTTP {(int)response.StatusCode}, status {code ?? "none"}{(reason is null ? null : $" ({reason})")}.");
        }
    }
}
