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

    public static readonly Error PlanInUse = Error.Conflict("plan.in_use", "The plan has subscriptions; archive it instead.");
}
