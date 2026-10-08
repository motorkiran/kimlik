using Bunit;
using Kimlik.Admin.Security;
using Kimlik.Domain.Access;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Kimlik.Server.Tests.Admin;

/// <summary>
/// Renders admin panel components with bUnit, as a signed-in administrator, against the real Kimlik under test: the
/// components' operations run in scopes of the test server, so they read and change its database.
/// </summary>
internal sealed class AdminComponents : BunitContext
{
    public AdminComponents(KimlikServerFixture server, Guid administratorId, IReadOnlySet<string>? permissions = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var session = new AdminSession();
        session.Load(administratorId, "admin@example.com", permissions ?? SystemPermissions.Global.Keys.ToHashSet(StringComparer.Ordinal), null, null);
        Services.AddSingleton(session);
        Services.AddSingleton(new AdminOperations(server.Services.GetRequiredService<IServiceScopeFactory>(), session));

        // Dialogs, message boxes, select menus and notifications render into these providers.
        Render<MudPopoverProvider>();
        Dialogs = Render<MudDialogProvider>();
        Notifications = Render<MudSnackbarProvider>();
    }

    public IRenderedComponent<MudDialogProvider> Dialogs { get; }

    public IRenderedComponent<MudSnackbarProvider> Notifications { get; }

    /// <summary>Waits until the administrator is told <paramref name="message"/>.</summary>
    public void WaitForNotification(string message) =>
        Notifications.WaitForAssertion(() => Notifications.Markup.ShouldContain(message), TimeSpan.FromSeconds(5));

    /// <summary>Clicks the button in the open dialog whose text is <paramref name="text"/>.</summary>
    public void Confirm(string text) =>
        Dialogs.WaitForElements("button").Single(button => button.TextContent.Trim() == text).Click();
}
