using Kimlik.Client.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kimlik.Client;

public static class KimlikClientDefaults
{
    /// <summary>The <see cref="IHttpClientFactory"/> name of the client that calls the Management API.</summary>
    public const string HttpClientName = "Kimlik";

    /// <summary>The <see cref="IHttpClientFactory"/> name of the client that requests access tokens.</summary>
    public const string TokenHttpClientName = "Kimlik.Tokens";
}

public static class KimlikClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="KimlikClient"/>, which calls the Management API as the configured service client.</summary>
    /// <returns>The builder of the Management API's HTTP client, to add resilience or other handlers.</returns>
    public static IHttpClientBuilder AddKimlikClient(this IServiceCollection services, Action<KimlikClientOptions> configure)
    {
        services.AddOptions<KimlikClientOptions>()
            .Configure(configure)
            .Validate(options => options.IsValid(), "Kimlik client: Authority must be an absolute URL, and ClientId, ClientSecret and Scope are required.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ClientCredentialsTokenSource>();
        services.TryAddTransient<AccessTokenHandler>();

        services.AddHttpClient(KimlikClientDefaults.TokenHttpClientName);

        return services.AddHttpClient<KimlikClient>(KimlikClientDefaults.HttpClientName, (provider, http) =>
                http.BaseAddress = provider.GetRequiredService<IOptions<KimlikClientOptions>>().Value.BaseAddress)
            .AddHttpMessageHandler<AccessTokenHandler>();
    }
}
