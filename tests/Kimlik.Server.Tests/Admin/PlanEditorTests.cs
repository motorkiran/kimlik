using Bunit;
using Kimlik.Admin.Components.Pages.Plans;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

namespace Kimlik.Server.Tests.Admin;

/// <summary>
/// The plan editor, which lists every feature of the installation. It takes turns with the tests that delete features,
/// so that none disappears while a plan is being saved.
/// </summary>
[Collection(typeof(FeatureCatalog))]
public sealed class PlanEditorTests(KimlikServerFixture server)
{
    [Fact]
    public async Task PlanEditor_SavesFeatureValues()
    {
        var (planId, flag, limit) = await server.QueryDatabaseAsync(async context =>
        {
            var now = DateTimeOffset.UtcNow;
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var flag = Feature.Create($"flag_{suffix}", "Flag", null, FeatureType.Boolean, now).Value;
            var limit = Feature.Create($"limit_{suffix}", "Limit", null, FeatureType.Limit, now).Value;
            var plan = Plan.Create($"plan_{suffix}", "Plan", null, now).Value;
            context.Features.AddRange(flag, limit);
            context.Plans.Add(plan);
            await context.SaveChangesAsync();
            return (plan.Id, flag, limit);
        });
        await using var admin = new AdminComponents(server, Guid.NewGuid());
        var page = admin.Render<PlanDetail>(parameters => parameters.Add(detail => detail.Id, planId));
        page.WaitForElement("#save-plan");

        // Every feature of the installation is listed; other tests add theirs.
        await page.InvokeAsync(() => page.FindComponents<MudSwitch<bool>>().Single(control => IsFor(control.Instance, flag.Key)).Instance.ValueChanged.InvokeAsync(true));
        await page.InvokeAsync(() => page.FindComponents<MudCheckBox<bool>>().Single(control => IsFor(control.Instance, limit.Key)).Instance.ValueChanged.InvokeAsync(true));
        // Found and clicked in one go, so that no render in between leaves the click on a stale handler.
        await page.InvokeAsync(() => page.Find("#save-plan").Click());

        admin.WaitForNotification("The plan was saved.");
        var values = await server.QueryDatabaseAsync(context => context.Plans.Where(plan => plan.Id == planId).SelectMany(plan => plan.Features).ToListAsync());
        values.Single(value => value.FeatureId == flag.Id).Enabled.ShouldBeTrue();
        values.Single(value => value.FeatureId == limit.Id).Limit.ShouldBeNull();

        static bool IsFor(MudComponentBase control, string feature) =>
            control.UserAttributes.TryGetValue("data-feature", out var key) && key as string == feature;
    }
}

/// <summary>Tests that delete features, and tests that list them all, run one at a time.</summary>
[CollectionDefinition]
public sealed class FeatureCatalog;
