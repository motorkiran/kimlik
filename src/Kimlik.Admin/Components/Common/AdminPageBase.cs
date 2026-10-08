using Kimlik.Admin.Security;
using Kimlik.Domain.Common;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Kimlik.Admin.Components.Common;

/// <summary>
/// What admin panel pages share: running operations as the administrator, saying how they went, and asking before
/// what cannot be undone.
/// </summary>
public abstract class AdminPageBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _disposed = new();

    [Inject]
    protected AdminOperations Operations { get; set; } = null!;

    [Inject]
    protected AdminSession Session { get; set; } = null!;

    [Inject]
    protected ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    protected IDialogService Dialogs { get; set; } = null!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = null!;

    /// <summary>Cancelled when the page goes away, such as when the administrator navigates elsewhere.</summary>
    protected CancellationToken Aborted => _disposed.Token;

    /// <summary>Runs a change and tells the administrator how it went.</summary>
    protected async Task<bool> ChangeAsync<THandler>(string permission, Func<THandler, Task<Result>> change, string done)
        where THandler : notnull
    {
        var result = await Operations.RunAsync(permission, change);
        Report(result.IsSuccess ? null : result.Error, done);
        return result.IsSuccess;
    }

    /// <inheritdoc cref="ChangeAsync{THandler}"/>
    protected async Task<T?> ChangeAsync<THandler, T>(string permission, Func<THandler, Task<Result<T>>> change, string done)
        where THandler : notnull
        where T : class
    {
        var result = await Operations.RunAsync(permission, change);
        Report(result.IsSuccess ? null : result.Error, done);
        return result.IsSuccess ? result.Value : null;
    }

    protected void Report(Error? error, string done)
    {
        if (error is null)
        {
            Snackbar.Add(done, Severity.Success);
        }
        else
        {
            Snackbar.Add(error.Message, Severity.Error);
        }
    }

    protected async Task<bool> ConfirmAsync(string title, string message, string yes) =>
        await Dialogs.ShowMessageBoxAsync(title, message, yesText: yes, cancelText: "Cancel") == true;

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
        GC.SuppressFinalize(this);
    }
}
