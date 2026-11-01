using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

/// <summary>
/// Value copy of an invoice's decision state, taken before an action. The fake repository hands
/// back the same instance, so comparing an invoice to itself would prove nothing.
/// </summary>
internal sealed record InvoiceSnapshot(
    InvoiceStatus Status,
    string? DecidedBy,
    DateTime? DecidedAt,
    string? DecisionNote,
    InvoiceApprovalRecord[] Approvals)
{
    public static InvoiceSnapshot Take(Invoice invoice) => new(
        invoice.Status,
        invoice.DecidedBy,
        invoice.DecidedAt,
        invoice.DecisionNote,
        invoice.Approvals.ToArray());

    public void AssertUnchanged(Invoice invoice)
    {
        Assert.Equal(Status, invoice.Status);
        Assert.Equal(DecidedBy, invoice.DecidedBy);
        Assert.Equal(DecidedAt, invoice.DecidedAt);
        Assert.Equal(DecisionNote, invoice.DecisionNote);
        Assert.Equal(Approvals, invoice.Approvals);
    }
}
