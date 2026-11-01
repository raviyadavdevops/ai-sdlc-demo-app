using InvoiceApp.Api.Auth;
using InvoiceApp.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace InvoiceApp.Tests;

public class AuthPipelineTests
{
    private static ActionExecutingContext ContextFor(UserSession? session)
    {
        var http = new DefaultHttpContext();
        if (session is not null)
        {
            http.Items[HttpContextExtensions.SessionKey] = session;
        }

        return new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Director")]
    [InlineData("Approver")]
    [InlineData("Admin")]
    public void RequireRole_OnApprovalsController_AllowsTheFourApprovalRoles(string role)
    {
        var attribute = ApprovalsRoleAttribute();
        var context = ContextFor(new UserSession("some.user", role));

        attribute.OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void RequireRole_OnApprovalsController_ForbidsARequestWithNoSession()
    {
        var attribute = ApprovalsRoleAttribute();
        var context = ContextFor(session: null);

        attribute.OnActionExecuting(context);

        var result = Assert.IsType<StatusCodeResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("manager")]
    [InlineData("")]
    public void RequireRole_OnApprovalsController_ForbidsAnyOtherRole(string role)
    {
        var attribute = ApprovalsRoleAttribute();
        var context = ContextFor(new UserSession("some.user", role));

        attribute.OnActionExecuting(context);

        var result = Assert.IsType<StatusCodeResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task SessionAuthMiddleware_WithoutToken_Returns401AndNeverReachesTheController()
    {
        var reached = false;
        var middleware = new SessionAuthMiddleware(_ => { reached = true; return Task.CompletedTask; }, new SessionStore());
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/approvals/INV-1/approve";

        await middleware.InvokeAsync(http);

        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.False(reached);
    }

    [Fact]
    public async Task SessionAuthMiddleware_WithUnknownToken_Returns401()
    {
        var reached = false;
        var middleware = new SessionAuthMiddleware(_ => { reached = true; return Task.CompletedTask; }, new SessionStore());
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/approvals/INV-1/approve";
        http.Request.Headers.Authorization = "Bearer not-a-real-token";

        await middleware.InvokeAsync(http);

        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.False(reached);
    }

    [Fact]
    public async Task SessionAuthMiddleware_WithValidToken_AttachesTheServerSideSession()
    {
        var store = new SessionStore();
        var token = store.Create(new UserSession("demo.manager", "Manager"));
        var reached = false;
        var middleware = new SessionAuthMiddleware(_ => { reached = true; return Task.CompletedTask; }, store);
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/approvals/INV-1/approve";
        http.Request.Headers.Authorization = $"Bearer {token}";

        await middleware.InvokeAsync(http);

        Assert.True(reached);
        Assert.Equal(new UserSession("demo.manager", "Manager"), http.GetSession());
    }

    private static RequireRoleAttribute ApprovalsRoleAttribute() =>
        (RequireRoleAttribute)Attribute.GetCustomAttribute(typeof(ApprovalsController), typeof(RequireRoleAttribute))!;
}
