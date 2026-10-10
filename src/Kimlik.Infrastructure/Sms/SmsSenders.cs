using System.Net.Http.Json;
using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Sms;

internal static class SmsSenders
{
    /// <summary>The HTTP client every provider uses; tests replace its handler.</summary>
    public const string HttpClientName = "Kimlik.Sms";

    public static IServiceCollection AddSms(this IServiceCollection services)
    {
        services.AddOptions<NetgsmOptions>().BindConfiguration(NetgsmOptions.SectionName);
        services.AddOptions<IletiMerkeziOptions>().BindConfiguration(IletiMerkeziOptions.SectionName);
        services.AddOptions<TwilioOptions>().BindConfiguration(TwilioOptions.SectionName);
        services.AddSingleton<IValidateOptions<SmsOptions>, ValidateSmsProvider>();

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<ISmsThrottle, SmsThrottle>();
        services.AddSingleton<NetgsmSmsSender>();
        services.AddSingleton<IletiMerkeziSmsSender>();
        services.AddSingleton<TwilioSmsSender>();
        services.AddSingleton<UnconfiguredSmsSender>();
        services.AddSingleton<ISmsSender>(provider => provider.GetRequiredService<IOptions<SmsOptions>>().Value.Provider switch
        {
            SmsProvider.Netgsm => provider.GetRequiredService<NetgsmSmsSender>(),
            SmsProvider.IletiMerkezi => provider.GetRequiredService<IletiMerkeziSmsSender>(),
            SmsProvider.Twilio => provider.GetRequiredService<TwilioSmsSender>(),
            _ => provider.GetRequiredService<UnconfiguredSmsSender>(),
        });

        return services;
    }

    /// <summary>The provider's JSON answer, or nothing when it sent none.</summary>
    public static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Used when no provider is chosen; nothing should send texts then, so sending fails clearly.</summary>
internal sealed partial class UnconfiguredSmsSender(ILogger<UnconfiguredSmsSender> logger) : ISmsSender
{
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        LogNotConfigured(logger);
        throw new InvalidOperationException("Text messages are not configured. Set Kimlik:Sms:Provider and the provider's settings.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A text message was not sent because text messages are not configured")]
    private static partial void LogNotConfigured(ILogger logger);
}
