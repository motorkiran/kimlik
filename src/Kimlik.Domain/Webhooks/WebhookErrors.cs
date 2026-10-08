using Kimlik.Domain.Common;

namespace Kimlik.Domain.Webhooks;

public static class WebhookErrors
{
    public static readonly Error EndpointNotFound = Error.NotFound("webhook.endpoint_not_found", "The webhook endpoint does not exist.");

    public static readonly Error DeliveryNotFound = Error.NotFound("webhook.delivery_not_found", "The webhook delivery does not exist.");

    public static readonly Error InvalidUrl = Error.Validation(
        "webhook.invalid_url", "The URL must be an absolute HTTPS URL without credentials; plain HTTP is accepted for localhost only.");

    public static readonly Error InvalidDescription = Error.Validation("webhook.invalid_description", "A description is at most 256 characters.");

    public static readonly Error UnknownEventType = Error.Validation("webhook.unknown_event_type", "One or more event types do not exist.");
}
