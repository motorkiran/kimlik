using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>The access model as one document.</summary>
public sealed class ProvisioningClient
{
    private readonly KimlikHttp _http;

    internal ProvisioningClient(KimlikHttp http) => _http = http;

    /// <summary>Describes the current model, without client secrets. Requires <c>kimlik.roles:read</c> and <c>kimlik.clients:read</c>.</summary>
    public Task<ProvisioningDocument> ExportAsync(CancellationToken cancellationToken = default) =>
        _http.GetAsync<ProvisioningDocument>("provisioning", cancellationToken);

    /// <summary>
    /// Creates what is missing and updates what differs, in one transaction; nothing is deleted. Requires
    /// <c>kimlik.roles:write</c> and <c>kimlik.clients:write</c>.
    /// </summary>
    public Task<ProvisioningResult> ApplyAsync(ProvisioningDocument document, CancellationToken cancellationToken = default) =>
        _http.SendAsync<ProvisioningResult>(HttpMethod.Post, "provisioning", document, cancellationToken);
}
