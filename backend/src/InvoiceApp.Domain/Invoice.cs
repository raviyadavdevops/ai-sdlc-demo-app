namespace InvoiceApp.Domain;

public class Invoice
{
    private readonly List<InvoiceApprovalRecord> _approvals = new();

    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>
    /// Approval history, oldest first. Each read returns a copy, so a request listing invoices
    /// never enumerates the live list while another request appends to it. Only
    /// <see cref="AddApproval"/> can change the history; it can never be replaced or cleared.
    /// </summary>
    public IReadOnlyList<InvoiceApprovalRecord> Approvals
    {
        get
        {
            lock (_approvals) return _approvals.ToArray();
        }
    }

    public void AddApproval(InvoiceApprovalRecord approval)
    {
        lock (_approvals) _approvals.Add(approval);
    }
}
