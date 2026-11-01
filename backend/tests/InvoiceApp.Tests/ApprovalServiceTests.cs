using System.Globalization;
using System.Text.Json;
using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalServiceTests
{
    private static Invoice InvoiceWith(string id, InvoiceStatus status) =>
        new() { Id = id, CustomerName = "Test School", Amount = 100m, Status = status };

    private static ApprovalService ServiceFor(params Invoice[] invoices) =>
        new(new FakeInvoiceRepository(invoices));

    [Fact]
    public void Submit_MovesDraftToPendingApproval()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Draft);

        var result = ServiceFor(invoice).Submit("INV-1");

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
    }

    [Fact]
    public void Submit_RejectsInvoiceThatIsNotADraft()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Approved));

        Assert.Throws<InvalidOperationException>(() => service.Submit("INV-1"));
    }

    [Fact]
    public void Approve_SetsStatusApproverAndTimestamp()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Approve("INV-1", "demo.approver");

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.approver", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
    }

    [Fact]
    public void Approve_RejectsInvoiceThatIsStillADraft()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Draft));

        Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", "demo.approver"));
    }

    [Fact]
    public void Approve_ThrowsWhenInvoiceDoesNotExist()
    {
        var service = ServiceFor();

        Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", "demo.approver"));
    }

    [Fact]
    public void Reject_RecordsReasonAndApprover()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Reject("INV-1", "demo.approver", "Duplicate invoice");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Equal("Duplicate invoice", result.DecisionNote);
    }

    [Fact]
    public void Reject_RequiresAReason()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", "demo.approver", "  "));
    }

    // ---- Stage 1: single-approver limits (feature 001-invoice-approval-workflow) ----

    private static Invoice PendingInvoice(decimal amount, InvoiceStatus status = InvoiceStatus.PendingApproval) =>
        new() { Id = "INV-1", CustomerName = "Test School", Amount = amount, Status = status };

    public static TheoryData<string, decimal> AtRoleLimit => new()
    {
        { "Manager", ApprovalService.ManagerLimit },
        { "Director", ApprovalService.StandaloneCeiling },
        { "Approver", ApprovalService.StandaloneCeiling },
        { "Admin", ApprovalService.StandaloneCeiling },
    };

    public static TheoryData<string, decimal, string> ManagerOverLimit => new()
    {
        { "Manager", 50_000.01m, "Manager approval limit is 50,000; invoice amount is 50,000.01." },
        { "Manager", 50_001m, "Manager approval limit is 50,000; invoice amount is 50,001." },
        { "Manager", ApprovalService.StandaloneCeiling, "Manager approval limit is 50,000; invoice amount is 500,000." },
    };

    public static TheoryData<string, decimal, string> AboveStandaloneCeiling => new()
    {
        { "Manager", 500_000.01m, "Manager approval limit is 50,000; invoice amount is 500,000.01." },
        { "Director", 500_000.01m, "Director approval limit is 500,000; invoice amount is 500,000.01." },
        { "Approver", 500_000.01m, "Approver approval limit is 500,000; invoice amount is 500,000.01." },
        { "Admin", 500_000.01m, "Admin approval limit is 500,000; invoice amount is 500,000.01." },
        { "Director", 500_001m, "Director approval limit is 500,000; invoice amount is 500,001." },
        { "Admin", 1_000_000m, "Admin approval limit is 500,000; invoice amount is 1,000,000." },
    };

    [Theory]
    [MemberData(nameof(AtRoleLimit))]
    public void Approve_AtExactlyTheRoleLimit_ApprovesAndRecordsOneApproval(string role, decimal amount)
    {
        var service = ServiceFor(PendingInvoice(amount));

        var result = service.Approve("INV-1", "some.user", role);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("some.user", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal("some.user", approval.Approver);
        Assert.Equal(role, approval.Role);
    }

    [Theory]
    [MemberData(nameof(ManagerOverLimit))]
    public void Approve_ManagerAboveFiftyThousand_IsRejectedAndInvoiceUnchanged(string role, decimal amount, string expectedMessage)
    {
        var invoice = PendingInvoice(amount);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "demo.manager", role));

        Assert.Equal(expectedMessage, error.Message);
        before.AssertUnchanged(invoice);
    }

    [Theory]
    [MemberData(nameof(AboveStandaloneCeiling))]
    public void Approve_AboveStandaloneCeiling_IsRejectedForEveryRole_Stage1Interim(string role, decimal amount, string expectedMessage)
    {
        var invoice = PendingInvoice(amount);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "some.user", role));

        Assert.Equal(expectedMessage, error.Message);
        before.AssertUnchanged(invoice);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Approved)]
    [InlineData(InvoiceStatus.Rejected)]
    public void Approve_NonPendingInvoice_IsAConflictBeforeAnyLimitCheckAndUnchanged(InvoiceStatus status)
    {
        var invoice = PendingInvoice(1_000_000m, status);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", "demo.manager", "Manager"));

        before.AssertUnchanged(invoice);
    }

    [Theory]
    [InlineData("Intern")]
    [InlineData("manager")]
    [InlineData("")]
    [InlineData(null)]
    public void Approve_UnknownRole_IsRejectedAndInvoiceUnchanged(string? role)
    {
        var invoice = PendingInvoice(100m);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "some.user", role!));

        Assert.Equal($"Role '{role}' is not eligible to approve invoices.", error.Message);
        before.AssertUnchanged(invoice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Approve_BlankApprover_IsRejectedAndInvoiceUnchanged(string approver)
    {
        var invoice = PendingInvoice(100m);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", approver, "Manager"));

        Assert.Equal("An approver name is required.", error.Message);
        before.AssertUnchanged(invoice);
    }

    [Fact]
    public void Approve_WithRole_ThrowsWhenInvoiceDoesNotExist()
    {
        var service = ServiceFor();

        Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", "demo.manager", "Manager"));
    }

    [Fact]
    public void Approve_LegacyTwoArgumentForm_ActsAsApproverAtTheStandaloneCeiling()
    {
        var service = ServiceFor(PendingInvoice(ApprovalService.StandaloneCeiling));

        var result = service.Approve("INV-1", "demo.approver");

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("Approver", Assert.Single(result.Approvals).Role);
    }

    [Fact]
    public void Approve_LegacyTwoArgumentForm_CannotBypassTheLimit_Stage1Interim()
    {
        var invoice = PendingInvoice(500_000.01m);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "demo.approver"));

        Assert.Equal("Approver approval limit is 500,000; invoice amount is 500,000.01.", error.Message);
        before.AssertUnchanged(invoice);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Director")]
    public void Reject_ByManagerOrDirector_SucceedsAtAnyAmount(string role)
    {
        var invoice = PendingInvoice(1_000_000m);
        var service = ServiceFor(invoice);

        var result = service.Reject("INV-1", $"demo.{role.ToLowerInvariant()}", "Duplicate invoice");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Equal($"demo.{role.ToLowerInvariant()}", result.DecidedBy);
        Assert.Equal("Duplicate invoice", result.DecisionNote);
        Assert.NotNull(result.DecidedAt);
        Assert.Empty(result.Approvals);
    }

    [Fact]
    public void Reject_WithBlankReason_LeavesInvoiceUnchanged()
    {
        var invoice = PendingInvoice(1_000_000m);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Reject("INV-1", "demo.manager", "  "));

        Assert.StartsWith("A reason is required to reject an invoice.", error.Message);
        before.AssertUnchanged(invoice);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Approved)]
    [InlineData(InvoiceStatus.Rejected)]
    public void Reject_NonPendingInvoice_IsAConflictAndInvoiceUnchanged(InvoiceStatus status)
    {
        var invoice = PendingInvoice(1_000_000m, status);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<InvalidOperationException>(() => service.Reject("INV-1", "demo.director", "Too late"));

        Assert.Equal($"Invoice INV-1 is {status}; only pending invoices can be decided.", error.Message);
        before.AssertUnchanged(invoice);
    }

    [Fact]
    public void Approve_WithRole_ReportsAMissingInvoiceByIdAndStoresNothing()
    {
        var repository = new RecordingInvoiceRepository();
        var service = new ApprovalService(repository);

        var error = Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", "demo.manager", "Manager"));

        Assert.Equal("Invoice INV-404 was not found.", error.Message);
        Assert.Empty(repository.UpdatedInvoiceIds);
    }

    [Fact]
    public async Task Approve_ByManyUsersAtOnce_RecordsExactlyOneApproval_StressCheck()
    {
        // Probabilistic smoke test only (it can pass without the lock on a busy or small machine);
        // the two *_WhileAn…IsBeingSaved_* tests below are the deterministic regression tests.
        const int callers = 8;
        var invoice = PendingInvoice(50_000m);
        var service = new ApprovalService(new GatedInvoiceRepository(callers, invoice));

        var outcomes = await Task.WhenAll(Enumerable.Range(1, callers).Select(number =>
            Task.Run(() => Capture(() => service.Approve("INV-1", $"demo.manager{number}", "Manager")))));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.All(outcomes.Where(outcome => outcome is not null), outcome => Assert.IsType<InvalidOperationException>(outcome));
        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        var approval = Assert.Single(invoice.Approvals);
        Assert.Equal(approval.Approver, invoice.DecidedBy);
    }

    [Fact]
    public async Task Approve_WhileAnotherApprovalIsBeingSaved_WaitsForItAndThenIsRefused()
    {
        var invoice = PendingInvoice(50_000m);
        var repository = new BlockingUpdateRepository(invoice);
        var service = new ApprovalService(repository);

        try
        {
            var first = Task.Run(() => service.Approve("INV-1", "demo.manager1", "Manager"));
            Assert.True(repository.UpdateEntered.Wait(TimeSpan.FromSeconds(5)));
            var second = Task.Run(() => Capture(() => service.Approve("INV-1", "demo.manager2", "Manager")));

            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(300)));
            repository.Release.Set();
            await first;
            Assert.IsType<InvalidOperationException>(await second);
        }
        finally
        {
            repository.Release.Set(); // never leave the first decision parked inside Update
        }

        var approval = Assert.Single(invoice.Approvals);
        Assert.Equal("demo.manager1", approval.Approver);
        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
    }

    [Fact]
    public async Task Reject_WhileAnApprovalIsBeingSaved_WaitsForItAndThenIsRefused()
    {
        var invoice = PendingInvoice(50_000m);
        var repository = new BlockingUpdateRepository(invoice);
        var service = new ApprovalService(repository);

        try
        {
            var approval = Task.Run(() => service.Approve("INV-1", "demo.manager", "Manager"));
            Assert.True(repository.UpdateEntered.Wait(TimeSpan.FromSeconds(5)));
            var rejection = Task.Run(() => Capture(() => service.Reject("INV-1", "demo.director", "Duplicate invoice")));

            Assert.NotSame(rejection, await Task.WhenAny(rejection, Task.Delay(300)));
            repository.Release.Set();
            await approval;
            Assert.IsType<InvalidOperationException>(await rejection);
        }
        finally
        {
            repository.Release.Set(); // never leave the approval parked inside Update
        }

        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Single(invoice.Approvals);
        Assert.Null(invoice.DecisionNote);
    }
    [Fact]
    public void Invoice_Approvals_IsASnapshotSoALaterAppendCannotDisturbAReader()
    {
        var invoice = PendingInvoice(100m);
        invoice.AddApproval(new InvoiceApprovalRecord("first.user", "Manager", DateTime.UtcNow));

        var seenByReader = invoice.Approvals;
        invoice.AddApproval(new InvoiceApprovalRecord("second.user", "Director", DateTime.UtcNow));

        Assert.Single(seenByReader);
        Assert.Equal(2, invoice.Approvals.Count);
    }

    [Theory]
    [InlineData("50000.001", "Manager approval limit is 50,000; invoice amount is 50,000.001.")]
    [InlineData("50000.0001", "Manager approval limit is 50,000; invoice amount is 50,000.0001.")]
    public void Approve_ManagerJustAboveLimitByAFraction_ShowsTheRealAmountInTheReason(string amount, string expectedMessage)
    {
        var invoice = PendingInvoice(decimal.Parse(amount, CultureInfo.InvariantCulture));
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "demo.manager", "Manager"));

        Assert.Equal(expectedMessage, error.Message);
        before.AssertUnchanged(invoice);
    }

    [Fact]
    public void Approve_RecordsTheSameUtcTimestampInTheApprovalAndTheDecision()
    {
        var service = ServiceFor(PendingInvoice(50_000m));

        var result = service.Approve("INV-1", "demo.manager", "Manager");

        var approval = Assert.Single(result.Approvals);
        Assert.Equal(result.DecidedAt, approval.ApprovedAt);
        Assert.Equal(DateTimeKind.Utc, approval.ApprovedAt.Kind);
    }

    [Fact]
    public void Invoice_SerializesItsApprovalHistoryForTheApiResponse()
    {
        var service = ServiceFor(PendingInvoice(50_000m));
        var result = service.Approve("INV-1", "demo.manager", "Manager");

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var approvals = json.RootElement.GetProperty("approvals");
        Assert.Equal(1, approvals.GetArrayLength());
        Assert.Equal("demo.manager", approvals[0].GetProperty("approver").GetString());
        Assert.Equal("Manager", approvals[0].GetProperty("role").GetString());
        Assert.True(approvals[0].TryGetProperty("approvedAt", out _));
    }

    [Fact]
    public void Approve_Success_SavesTheInvoiceExactlyOnce()
    {
        var repository = new RecordingInvoiceRepository(PendingInvoice(50_000m));
        var service = new ApprovalService(repository);

        service.Approve("INV-1", "demo.manager", "Manager");

        Assert.Equal(new[] { "INV-1" }, repository.UpdatedInvoiceIds);
    }

    [Theory]
    [InlineData("Manager", 50_001)]
    [InlineData("Director", 500_001)]
    [InlineData("Intern", 100)]
    public void Approve_RejectedAttempt_NeverSavesTheInvoice(string role, int amount)
    {
        var repository = new RecordingInvoiceRepository(PendingInvoice(amount));
        var service = new ApprovalService(repository);

        Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "some.user", role));

        Assert.Empty(repository.UpdatedInvoiceIds);
    }

    [Fact]
    public void Reject_Success_SavesTheInvoiceExactlyOnce_AndFailureNeverSaves()
    {
        var repository = new RecordingInvoiceRepository(PendingInvoice(1_000_000m));
        var service = new ApprovalService(repository);

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", "demo.manager", ""));
        Assert.Empty(repository.UpdatedInvoiceIds);

        service.Reject("INV-1", "demo.manager", "Duplicate invoice");

        Assert.Equal(new[] { "INV-1" }, repository.UpdatedInvoiceIds);
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }
}