using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Auditing;
using Kimlik.Infrastructure.Clients;
using Kimlik.Infrastructure.Email;
using Kimlik.Infrastructure.Identity;
using Kimlik.Infrastructure.Outbox;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Plans;
using Kimlik.Infrastructure.Provisioning;
using Kimlik.Infrastructure.Security;
using Kimlik.Infrastructure.Webhooks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure;

public static class DependencyInjection
{
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        AddPersistence(services);
        AddSecurity(services);
        AddIdentity(services);
        AddOutbox(services);
        AddEmail(services);

        services.AddScoped<IAuditLog, AuditLog>();
        AddWebhooks(services);
        services.AddHostedService<SubscriptionExpirationService>();
        services.AddScoped<ProtocolDataPruner>();
        services.AddHostedService<ProtocolDataPruningService>();
        services.AddHostedService<AuditRetentionService>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .Validate<IConfiguration>(
                (_, configuration) => !string.IsNullOrWhiteSpace(GetConnectionString(configuration)),
                $"The connection string '{DatabaseOptions.ConnectionStringName}' is not configured.")
            .ValidateOnStart();

        // The connection string is resolved when a context is created, so configuration
        // added late (integration tests, design-time tools) is honored.
        services.AddDbContext<KimlikDbContext>((serviceProvider, options) => options
            .UseNpgsql(
                GetConnectionString(serviceProvider.GetRequiredService<IConfiguration>()),
                npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, KimlikDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                serviceProvider.GetRequiredService<OutboxSignalInterceptor>(),
                serviceProvider.GetRequiredService<ClientChangeInterceptor>()));

        services.AddScoped<IKimlikDbContext>(provider => provider.GetRequiredService<KimlikDbContext>());
        services.AddSingleton<ClientChangeSignal>();
        services.AddScoped<ClientChangeInterceptor>();
        services.AddScoped<SystemCatalog>();
        services.AddScoped<DatabasePreparation>();

        services.AddOptions<ProvisioningOptions>()
            .BindConfiguration(ProvisioningOptions.SectionName)
            .Validate(options => options.IsValid(), $"{ProvisioningOptions.SectionName}:FilePath does not exist.")
            .ValidateOnStart();
    }

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddOptions<SecurityOptions>()
            .BindConfiguration(SecurityOptions.SectionName)
            .Validate(
                options => SecurityOptions.IsValidMasterKey(options.MasterKey),
                $"{SecurityOptions.SectionName}:MasterKey must be a base64-encoded 256-bit key. Generate one with 'openssl rand -base64 32'.")
            .ValidateOnStart();

        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<ISecretEncryption, SecretEncryption>();

        // Every instance shares one key ring, stored in PostgreSQL and encrypted with the master key.
        services.AddDataProtection()
            .SetApplicationName("Kimlik")
            .PersistKeysToDbContext<KimlikDbContext>();
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>, ConfigureKeyRingEncryption>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<User>()
            .AddEntityFrameworkStores<KimlikDbContext>()
            .AddUserStore<KimlikUserStore>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider<User>>(PasswordResetTokenProviderOptions.ProviderName)
            .AddTokenProvider<TotpTokenProvider>(TokenOptions.DefaultAuthenticatorProvider)
            .AddTokenProvider<EmailSignInCodeProvider>(EmailSignIn.TokenProvider)
            .AddPasswordValidator<MaximumLengthPasswordValidator<User>>()
            .AddPasswordValidator<BreachedPasswordValidator>();

        services.AddHttpClient(BreachedPasswordValidator.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            client.Timeout = TimeSpan.FromSeconds(3);
            client.DefaultRequestHeaders.Add("Add-Padding", "true");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Kimlik");
        });

        services.AddSingleton<ConfigureIdentity>();
        services.AddSingleton<IConfigureOptions<IdentityOptions>>(provider => provider.GetRequiredService<ConfigureIdentity>());
        services.AddSingleton<IConfigureOptions<PasswordHasherOptions>>(provider => provider.GetRequiredService<ConfigureIdentity>());
        services.AddSingleton<IConfigureOptions<DataProtectionTokenProviderOptions>>(provider => provider.GetRequiredService<ConfigureIdentity>());

        services.AddScoped<IUserSessions, UserSessions>();
    }

    private static void AddOutbox(IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .Validate(options => options.IsValid(), $"{OutboxOptions.SectionName}: intervals must be positive and the batch size between 1 and 1000.")
            .ValidateOnStart();

        services.AddSingleton(new OutboxMessageTypes([typeof(Application.DependencyInjection).Assembly]));
        services.AddSingleton<OutboxSignal>();
        services.AddScoped<OutboxSignalInterceptor>();
        services.AddScoped<IOutbox, Outbox.Outbox>();
        services.AddHostedService<OutboxDispatcher>();
    }

    private static void AddWebhooks(IServiceCollection services)
    {
        services.AddOptions<WebhookOptions>()
            .BindConfiguration(WebhookOptions.SectionName)
            .Validate(options => options.IsValid(), $"{WebhookOptions.SectionName}: intervals and delays must be positive and the batch size between 1 and 100.")
            .ValidateOnStart();

        services.AddSingleton<WebhookSignal>();
        services.AddSingleton<IWebhookSignal>(provider => provider.GetRequiredService<WebhookSignal>());
        services.AddScoped<WebhookSender>();
        services.AddHttpClient(WebhookSender.HttpClientName)
            .ConfigureHttpClient((provider, client) => WebhookHttpClient.Configure(client, provider.GetRequiredService<IOptions<WebhookOptions>>().Value.Timeout))
            .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = WebhookNetwork.Connect(provider.GetRequiredService<IOptions<WebhookOptions>>().Value.AllowPrivateNetworks),
            });
        services.AddHostedService<WebhookDeliveryService>();
    }

    private static void AddEmail(IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();
        services.AddSingleton<IAccountEmailThrottle, AccountEmailThrottle>();
        services.AddSingleton<IEmailTemplateRenderer, FluidEmailTemplateRenderer>();
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<UnconfiguredEmailSender>();
        services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured
            ? provider.GetRequiredService<SmtpEmailSender>()
            : provider.GetRequiredService<UnconfiguredEmailSender>());
    }

    private static string? GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
}
