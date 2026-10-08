using Kimlik.Client.Resources;

namespace Kimlik.Client;

/// <summary>
/// Calls the Kimlik Management API as a service client. Register it with
/// <see cref="KimlikClientServiceCollectionExtensions.AddKimlikClient"/>; it gets and renews access tokens by itself.
/// Failed calls throw <see cref="KimlikApiException"/>.
/// </summary>
public sealed class KimlikClient
{
    public KimlikClient(HttpClient httpClient)
    {
        var http = new KimlikHttp(httpClient);
        Users = new UsersClient(http);
        Permissions = new PermissionsClient(http);
        Roles = new RolesClient(http);
        ApiResources = new ApiResourcesClient(http);
        Clients = new ClientsClient(http);
        AuditEvents = new AuditEventsClient(http);
        Provisioning = new ProvisioningClient(http);
    }

    public UsersClient Users { get; }

    public PermissionsClient Permissions { get; }

    public RolesClient Roles { get; }

    public ApiResourcesClient ApiResources { get; }

    public ClientsClient Clients { get; }

    public AuditEventsClient AuditEvents { get; }

    public ProvisioningClient Provisioning { get; }
}
