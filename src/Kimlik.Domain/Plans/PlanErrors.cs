using Kimlik.Domain.Common;

namespace Kimlik.Domain.Plans;

public static class PlanErrors
{
    public static readonly Error InvalidFeatureKey = Error.Validation(
        "plan.invalid_feature_key", "Feature keys are up to 64 lowercase letters, digits, '_' and '-', starting with a letter.");

    public static readonly Error InvalidPlanKey = Error.Validation(
        "plan.invalid_plan_key", "Plan keys are up to 64 lowercase letters, digits, '_' and '-', starting with a letter.");

    public static readonly Error InvalidName = Error.Validation("plan.invalid_name", "A name is required and is at most 100 characters.");

    public static readonly Error InvalidDescription = Error.Validation("plan.invalid_description", "A description is at most 256 characters.");

    public static readonly Error InvalidFeatureValue = Error.Validation(
        "plan.invalid_feature_value", "Boolean features take true or false; limits take a whole number of zero or more, or null for unlimited.");

    public static readonly Error UnknownFeature = Error.Validation("plan.unknown_feature", "One or more features do not exist.");

    public static readonly Error FeatureNotFound = Error.NotFound("feature.not_found", "The feature does not exist.");

    public static readonly Error FeatureExists = Error.Conflict("feature.already_exists", "A feature with this key already exists.");

    public static readonly Error PlanNotFound = Error.NotFound("plan.not_found", "The plan does not exist.");

    public static readonly Error PlanExists = Error.Conflict("plan.already_exists", "A plan with this key already exists.");

    public static readonly Error PlanArchived = Error.Validation("plan.archived", "The plan is archived and takes no new subscribers.");

    public static readonly Error SubscriptionNotFound = Error.NotFound("subscription.not_found", "The subscription does not exist.");

    public static readonly Error SubscriptionExists = Error.Conflict(
        "subscription.exists", "The subscriber already has a current subscription; change its plan instead.");

    public static readonly Error SubscriptionEnded = Error.Validation("subscription.ended", "The subscription has ended or is already canceled.");

    public static readonly Error InvalidSubscriptionStatus = Error.Validation(
        "subscription.invalid_status", "A subscription can be set to trialing or active; cancel it to end it.");

    public static readonly Error InvalidSubscription = Error.Validation(
        "subscription.invalid", "A trial needs an end, a period must end after it starts, and an external reference is at most 200 characters.");

    public static readonly Error SubscriberNotFound = Error.NotFound("subscription.subscriber_not_found", "The user or organization does not exist.");

    public static readonly Error PlanInUse = Error.Conflict("plan.in_use", "The plan has subscriptions; archive it instead.");

    public static readonly Error NotABasePlan = Error.Validation("plan.not_a_base_plan", "Subscriptions are to base plans; add-ons go with them.");

    public static readonly Error NotAnAddOn = Error.Validation("plan.not_an_add_on", "Only add-ons go with a subscription, next to its one base plan.");

    public static readonly Error InvalidQuantity = Error.Validation("subscription.invalid_quantity", "An add-on's quantity is from 1 to 10,000.");
}
