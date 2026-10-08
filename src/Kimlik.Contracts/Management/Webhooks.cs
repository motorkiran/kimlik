using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kimlik.Contracts.Management;

/// <summary>
/// A URL that receives signed events. <c>EventTypes</c> lists the events it subscribes to; empty means all of them.
/// <c>FailingSince</c> is set while its deliveries keep failing.
/// </summary>
public sealed record WebhookEndpointResponse(
    Guid Id,
    string Url,
    string? Description,
    IReadOnlyList<string> EventTypes,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FailingSince);

/// <summary>A new endpoint and the secret that signs its deliveries, which is shown only this once.</summary>
public sealed record CreatedWebhookEndpointResponse(WebhookEndpointResponse Endpoint, string Secret);

/// <summary>A new signing secret, which is shown only this once.</summary>
public sealed record WebhookSecretResponse(string Secret);

public sealed record CreateWebhookEndpointRequest
{
    /// <summary>An HTTPS URL; plain HTTP only for <c>localhost</c>, for development.</summary>
    [Required]
    [StringLength(2048)]
    public required string Url { get; init; }

    [StringLength(256)]
    public string? Description { get; init; }

    /// <summary>The events to send; all of them when omitted or empty.</summary>
    public IReadOnlyList<string>? EventTypes { get; init; }

    public bool Enabled { get; init; } = true;
}

/// <summary>Changes an endpoint with JSON Merge Patch semantics: omitted properties keep their value.</summary>
public sealed record UpdateWebhookEndpointRequest
{
    [StringLength(2048)]
    public string? Url
    {
        get;
        init
        {
            field = value;
            HasUrl = true;
        }
    }

    [StringLength(256)]
    public string? Description
    {
        get;
        init
        {
            field = value;
            HasDescription = true;
        }
    }

    public IReadOnlyList<string>? EventTypes
    {
        get;
        init
        {
            field = value;
            HasEventTypes = true;
        }
    }

    public bool? Enabled { get; init; }

    [JsonIgnore]
    public bool HasUrl { get; private init; }

    [JsonIgnore]
    public bool HasDescription { get; private init; }

    [JsonIgnore]
    public bool HasEventTypes { get; private init; }
}

public enum WebhookDeliveryStatus
{
    /// <summary>Waiting for its first attempt or for a retry.</summary>
    Pending,

    Succeeded,

    /// <summary>Every attempt failed, or the endpoint was disabled or deleted first.</summary>
    Failed,
}

/// <summary>
/// One event sent to one endpoint, with the outcome of its latest attempt. Every attempt carries the event's ID as
/// <c>webhook-id</c>, so receivers can recognize repeats.
/// </summary>
public sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid EndpointId,
    Guid EventId,
    string EventType,
    WebhookDeliveryStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    int? ResponseStatusCode,
    string? ResponseBody,
    string? Error,
    JsonElement Payload);
