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

    public static TheoryData<string, decimal> AboveStandaloneCeiling => new()
    {
        { "Manager", 500_000.01m },
        { "Director", 500_000.01m },
        { "Approver", 500_000.01m },
        { "Admin", 500_000.01m },
        { "Director", 500_001m },
        { "Admin", 1_000_000m },
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
    public void Approve_AboveStandaloneCeiling_NoRoleCanApproveAloneAndFirstApprovalIsOnlyRecorded(string role, decimal amount)
    {
        var service = ServiceFor(PendingInvoice(amount));

        var result = service.Approve("INV-1", "some.user", role);

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Null(result.DecidedBy);
        Assert.Null(result.DecidedAt);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal("some.user", approval.Approver);
        Assert.Equal(role, approval.Role);
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
    public void Approve_LegacyTwoArgumentForm_AboveThreshold_RecordsFirstApprovalAndStaysPending()
    {
        var service = ServiceFor(PendingInvoice(DualApprovalAmount));

        var result = service.Approve("INV-1", "demo.approver");

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Null(result.DecidedBy);
        Assert.Null(result.DecidedAt);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal("demo.approver", approval.Approver);
        Assert.Equal("Approver", approval.Role);
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

    // ---- Stage 2: dual approval above 500,000 ----

    private const decimal DualApprovalAmount = 600_000m;
    private const string SeededFirstApprover = "first.user";

    private static decimal JustAboveStandaloneCeiling => ApprovalService.StandaloneCeiling + 0.01m;

    /// <summary>A pending invoice that already carries the given number of earlier approvals (setup only).</summary>
    private static Invoice PendingInvoiceWithApprovals(decimal amount, int priorApprovals, InvoiceStatus status = InvoiceStatus.PendingApproval)
    {
        var invoice = PendingInvoice(amount, status);
        for (var index = 0; index < priorApprovals; index++)
        {
            var approver = index == 0 ? SeededFirstApprover : $"earlier.user{index}";
            invoice.AddApproval(new InvoiceApprovalRecord(approver, index == 0 ? "Manager" : "Director", DateTime.UtcNow));
        }

        return invoice;
    }

    public static TheoryData<string> EveryEligibleRole => new() { "Manager", "Director", "Approver", "Admin" };

    public static TheoryData<string> NonDirectorStandaloneRoles => new() { "Director", "Approver", "Admin" };

    public static TheoryData<string, string, string> SameUserSecondAttempts => new()
    {
        // first approver, second attempt username, second attempt role
        { "demo.director", "demo.director", "Director" },
        { "demo.director", "Demo.Director", "Director" },
        { "demo.manager", "demo.manager", "Manager" },
        { "demo.manager", "DEMO.MANAGER", "Director" },
        { SeededFirstApprover, "FIRST.user", "Admin" },
    };

    public static TheoryData<string> WrongSecondApproverRoles => new() { "Manager", "Approver", "Admin" };

    public static TheoryData<InvoiceStatus, int, decimal, string, string, Type, string> RejectedAttemptCategories => new()
    {
        // status, prior approvals, amount, approver, role, expected exception, expected message
        { InvoiceStatus.PendingApproval, 1, DualApprovalAmount, "other.user", "Manager", typeof(ArgumentException), "The second approver for this invoice must be a Director." },
        { InvoiceStatus.PendingApproval, 1, DualApprovalAmount, "other.user", "Approver", typeof(ArgumentException), "The second approver for this invoice must be a Director." },
        { InvoiceStatus.PendingApproval, 1, DualApprovalAmount, "other.user", "Admin", typeof(ArgumentException), "The second approver for this invoice must be a Director." },
        { InvoiceStatus.PendingApproval, 1, DualApprovalAmount, SeededFirstApprover, "Director", typeof(ArgumentException), $"{SeededFirstApprover} has already approved this invoice; a different approver is required." },
        { InvoiceStatus.PendingApproval, 1, DualApprovalAmount, "FIRST.USER", "Manager", typeof(ArgumentException), "FIRST.USER has already approved this invoice; a different approver is required." },
        { InvoiceStatus.Draft, 0, DualApprovalAmount, "demo.director", "Director", typeof(InvalidOperationException), "Invoice INV-1 is Draft; only pending invoices can be decided." },
        { InvoiceStatus.Approved, 2, DualApprovalAmount, "demo.director3", "Director", typeof(InvalidOperationException), "Invoice INV-1 is Approved; only pending invoices can be decided." },
        { InvoiceStatus.Rejected, 1, DualApprovalAmount, "demo.director", "Director", typeof(InvalidOperationException), "Invoice INV-1 is Rejected; only pending invoices can be decided." },
        { InvoiceStatus.PendingApproval, 0, DualApprovalAmount, "demo.user", "Intern", typeof(ArgumentException), "Role 'Intern' is not eligible to approve invoices." },
        { InvoiceStatus.PendingApproval, 1, DualApprovalAmount, "other.user", "Intern", typeof(ArgumentException), "Role 'Intern' is not eligible to approve invoices." },
        { InvoiceStatus.PendingApproval, 0, DualApprovalAmount, "  ", "Director", typeof(ArgumentException), "An approver name is required." },
        { InvoiceStatus.PendingApproval, 1, DualApprovalAmount, "", "Director", typeof(ArgumentException), "An approver name is required." },
        { InvoiceStatus.PendingApproval, 0, ApprovalService.StandaloneCeiling, "demo.manager", "Manager", typeof(ArgumentException), "Manager approval limit is 50,000; invoice amount is 500,000." },
    };

    // T035
    [Theory]
    [MemberData(nameof(EveryEligibleRole))]
    public void Approve_FirstApprovalAboveStandaloneCeiling_RecordsItAndStaysPending(string role)
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        var service = ServiceFor(invoice);

        var result = service.Approve("INV-1", "first.approver", role);

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Null(result.DecidedBy);
        Assert.Null(result.DecidedAt);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal("first.approver", approval.Approver);
        Assert.Equal(role, approval.Role);
    }

    // T036
    [Fact]
    public void Approve_DirectorAfterAManagerFirstApproval_ApprovesWithTwoRecords()
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        var service = ServiceFor(invoice);
        var afterFirst = service.Approve("INV-1", "demo.manager", "Manager");
        var firstRecord = Assert.Single(afterFirst.Approvals);

        var result = service.Approve("INV-1", "demo.director", "Director");

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal(2, result.Approvals.Count);
        Assert.Equal(firstRecord, result.Approvals[0]);
        Assert.Equal("demo.manager", result.Approvals[0].Approver);
        Assert.Equal("Manager", result.Approvals[0].Role);
        Assert.Equal("demo.director", result.Approvals[1].Approver);
        Assert.Equal("Director", result.Approvals[1].Role);
        Assert.Equal("demo.director", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
        Assert.Equal(result.Approvals[1].ApprovedAt, result.DecidedAt);
    }

    // T037
    [Fact]
    public void Approve_DifferentDirectorAfterADirectorFirstApproval_ApprovesWithTwoRecords()
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        var service = ServiceFor(invoice);
        service.Approve("INV-1", "demo.director", "Director");

        var result = service.Approve("INV-1", "demo.director2", "Director");

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal(2, result.Approvals.Count);
        Assert.Equal("demo.director", result.Approvals[0].Approver);
        Assert.Equal("demo.director2", result.Approvals[1].Approver);
        Assert.Equal("Director", result.Approvals[1].Role);
        Assert.Equal("demo.director2", result.DecidedBy);
        Assert.Equal(result.Approvals[1].ApprovedAt, result.DecidedAt);
    }

    // T038
    [Theory]
    [MemberData(nameof(SameUserSecondAttempts))]
    public void Approve_SameUserApprovingTwice_IsRejectedWithAlreadyApprovedAndInvoiceUnchanged(
        string firstApprover, string secondAttemptUsername, string secondAttemptRole)
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        invoice.AddApproval(new InvoiceApprovalRecord(firstApprover, "Director", DateTime.UtcNow));
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", secondAttemptUsername, secondAttemptRole));

        Assert.Equal($"{secondAttemptUsername} has already approved this invoice; a different approver is required.", error.Message);
        before.AssertUnchanged(invoice);
    }

    [Fact]
    public void Approve_SameUserRetryingAsManager_GetsAlreadyApprovedNotTheDirectorMessage()
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        var service = ServiceFor(invoice);
        service.Approve("INV-1", "demo.manager", "Manager");
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "demo.manager", "Manager"));

        Assert.Equal("demo.manager has already approved this invoice; a different approver is required.", error.Message);
        before.AssertUnchanged(invoice);
    }

    // T039
    [Theory]
    [MemberData(nameof(WrongSecondApproverRoles))]
    public void Approve_DifferentNonDirectorAsSecondApprover_IsRejectedAndInvoiceUnchanged(string secondRole)
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        invoice.AddApproval(new InvoiceApprovalRecord(SeededFirstApprover, "Director", DateTime.UtcNow));
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", "second.user", secondRole));

        Assert.Equal("The second approver for this invoice must be a Director.", error.Message);
        before.AssertUnchanged(invoice);
    }

    // T040
    [Theory]
    [MemberData(nameof(NonDirectorStandaloneRoles))]
    public void Approve_ExactlyAtStandaloneCeiling_StaysStandaloneWithOneApproval(string role)
    {
        var service = ServiceFor(PendingInvoice(ApprovalService.StandaloneCeiling));

        var result = service.Approve("INV-1", "some.user", role);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("some.user", result.DecidedBy);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal(role, approval.Role);
    }

    [Theory]
    [MemberData(nameof(NonDirectorStandaloneRoles))]
    public void Approve_OneCentAboveStandaloneCeiling_EntersDualApprovalAndStaysPending(string role)
    {
        var service = ServiceFor(PendingInvoice(JustAboveStandaloneCeiling));

        var result = service.Approve("INV-1", "some.user", role);

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Null(result.DecidedBy);
        Assert.Null(result.DecidedAt);
        Assert.Single(result.Approvals);
    }

    // T041
    [Fact]
    public void Reject_ByManagerAfterAFirstApproval_KeepsTheFirstApprovalAsHistory()
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        var service = ServiceFor(invoice);
        var afterFirst = service.Approve("INV-1", "demo.director", "Director");
        var firstRecord = Assert.Single(afterFirst.Approvals);

        var result = service.Reject("INV-1", "demo.manager", "Amount not justified");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Equal("demo.manager", result.DecidedBy);
        Assert.Equal("Amount not justified", result.DecisionNote);
        Assert.NotNull(result.DecidedAt);
        Assert.Equal(firstRecord, Assert.Single(result.Approvals));
    }

    [Fact]
    public void Approve_ThirdApprovalOnAnApprovedDualInvoice_IsAConflictAndInvoiceUnchanged()
    {
        var invoice = PendingInvoice(DualApprovalAmount);
        var service = ServiceFor(invoice);
        service.Approve("INV-1", "demo.manager", "Manager");
        service.Approve("INV-1", "demo.director", "Director");
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", "demo.director3", "Director"));

        Assert.Equal("Invoice INV-1 is Approved; only pending invoices can be decided.", error.Message);
        before.AssertUnchanged(invoice);
    }

    // T042
    [Theory]
    [MemberData(nameof(RejectedAttemptCategories))]
    public void RejectedApprovalAttempt_NeverMutatesInvoiceState(
        InvoiceStatus status, int priorApprovals, decimal amount, string approver, string role,
        Type expectedException, string expectedMessage)
    {
        var invoice = PendingInvoiceWithApprovals(amount, priorApprovals, status);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.ThrowsAny<Exception>(() => service.Approve("INV-1", approver, role));

        Assert.IsType(expectedException, error);
        Assert.Equal(expectedMessage, error.Message);
        before.AssertUnchanged(invoice);
    }

    [Fact]
    public void Approve_FirstApprovalAboveStandaloneCeiling_IsSavedToTheRepository()
    {
        var repository = new RecordingInvoiceRepository(PendingInvoice(DualApprovalAmount));
        var service = new ApprovalService(repository);

        service.Approve("INV-1", "demo.manager", "Manager");

        Assert.Equal(new[] { "INV-1" }, repository.UpdatedInvoiceIds);
    }

    [Fact]
    public void Approve_UserMatchingAnyRecordedApprover_IsRejectedEvenWithMoreThanOneRecord()
    {
        // Inconsistent hand-built state (the service never leaves a pending invoice with two
        // approvals): the duplicate check must still compare against every recorded approver.
        var invoice = PendingInvoiceWithApprovals(DualApprovalAmount, priorApprovals: 2);
        var service = ServiceFor(invoice);
        var before = InvoiceSnapshot.Take(invoice);

        var error = Assert.Throws<ArgumentException>(() => service.Approve("INV-1", SeededFirstApprover, "Director"));

        Assert.Equal($"{SeededFirstApprover} has already approved this invoice; a different approver is required.", error.Message);
        before.AssertUnchanged(invoice);
    }
}
