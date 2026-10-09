using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kimlik.Server.Identity;

/// <summary>
/// Keeps an administrator acting as a user (see <see cref="SignInFlow.ImpersonateAsync"/>) from what only the user may
/// do, sending them to the impersonation page, which says so. With <see cref="ChangesOnly"/>, the pages still show and
/// only their forms are refused.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotWhileImpersonatingAttribute : Attribute, IAsyncPageFilter
{
    public bool ChangesOnly { get; init; }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (SignInFlow.ActorOf(context.HttpContext.User) is null || (ChangesOnly && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))))
        {
            return next();
        }

        context.Result = new RedirectToPageResult("/Impersonation", new { blocked = "true" });
        return Task.CompletedTask;
    }
}
