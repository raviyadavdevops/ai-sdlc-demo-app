using InvoiceApp.Api.Auth;
using InvoiceApp.Api.Controllers;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalsControllerTests
{
    private static Invoice PendingInvoice(decimal amount) =>
        new() { Id = "INV-1", CustomerName = "Test School", Amount = amount, Status = InvoiceStatus.PendingApproval };

    private static ApprovalsController ControllerFor(Invoice invoice, UserSession session, Action<HttpContext>? tamper = null)
    {
        var context = new DefaultHttpContext();
        context.Items[HttpContextExtensions.SessionKey] = session;
        tamper?.Invoke(context);

        var service = new ApprovalService(new FakeInvoiceRepository(invoice));
        return new ApprovalsController(service) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Fact]
    public void Approve_RecordsTheSessionUsernameAndRole()
    {
        var invoice = PendingInvoice(50_000m);
        var controller = ControllerFor(invoice, new UserSession("demo.manager", "Manager"));

        var result = controller.Approve("INV-1");

        Assert.IsType<OkObjectResult>(result.Result);
        var approval = Assert.Single(invoice.Approvals);
        Assert.Equal("demo.manager", approval.Approver);
        Assert.Equal("Manager", approval.Role);
        Assert.Equal("demo.manager", invoice.DecidedBy);
    }

    [Fact]
    public void Approve_AppliesTheSessionRoleLimit_NotTheLegacyApproverRule()
    {
        // 75,000 is fine for the legacy "Approver" role but over the Manager limit, so a 400 proves
        // the controller passes the session role to the three-argument overload.
        var invoice = PendingInvoice(75_000m);
        var controller = ControllerFor(invoice, new UserSession("demo.manager", "Manager"));
        var before = InvoiceSnapshot.Take(invoice);

        var result = controller.Approve("INV-1");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Manager approval limit is 50,000", badRequest.Value!.ToString());
        before.AssertUnchanged(invoice);
    }

    [Fact]
    public void Approve_IgnoresForgedIdentityAndRoleFromTheRequest()
    {
        var invoice = PendingInvoice(75_000m);
        var controller = ControllerFor(invoice, new UserSession("demo.manager", "Manager"), context =>
        {
            context.Request.Headers["X-Role"] = "Admin";
            context.Request.Headers["X-Username"] = "demo.admin";
            context.Request.QueryString = new QueryString("?role=Admin&username=demo.admin&approver=demo.admin");
        });
        var before = InvoiceSnapshot.Take(invoice);

        var result = controller.Approve("INV-1");

        Assert.IsType<BadRequestObjectResult>(result.Result);
        before.AssertUnchanged(invoice);
    }

    [Fact]
    public void Reject_RecordsTheSessionUsernameForAManagerAtAnyAmount()
    {
        var invoice = PendingInvoice(1_000_000m);
        var controller = ControllerFor(invoice, new UserSession("demo.manager", "Manager"));

        var result = controller.Reject("INV-1", new ApprovalsController.RejectRequest("Duplicate invoice"));

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(InvoiceStatus.Rejected, invoice.Status);
        Assert.Equal("demo.manager", invoice.DecidedBy);
    }
}
