using System.Globalization;

namespace InvoiceApp.Domain;

/// <summary>
/// Moves an invoice through its lifecycle: Draft -> PendingApproval -> Approved or Rejected.
/// One invoice at a time: up to 500,000 one approver within that role's limit decides it; above 500,000 two distinct approvers are needed (the second a Director).
/// </summary>
public class ApprovalService
{
    /// <summary>Largest invoice a Manager may approve (inclusive).</summary>
    public const decimal ManagerLimit = 50_000m;

    /// <summary>Largest invoice a Director, Approver or Admin may approve alone (inclusive).</summary>
    public const decimal StandaloneCeiling = 500_000m;

    private const string ManagerRole = "Manager";
    private const string DirectorRole = "Director";
    private const string ApproverRole = "Approver";
    private const string AdminRole = "Admin";

    /// <summary>
    /// The repository returns the same invoice instance to every request, and the service is
    /// created per request, so deciding an invoice (check it is pending, then change it) is made
    /// atomic with one process-wide lock. Without it two requests could both see "pending".
    /// Fine for the single-process in-memory demo; a shared store would need its own concurrency control.
    /// It serialises decisions with each other, not with Submit, invoice deletion or unlocked reads:
    /// a concurrent GET can briefly show a half-applied decision, and an invoice deleted mid-decision
    /// is simply not saved (both pre-existing characteristics of the in-memory repository).
    /// </summary>
    private static readonly object DecisionLock = new();

    private readonly IInvoiceRepository _repository;

    public ApprovalService(IInvoiceRepository repository)
    {
        _repository = repository;
    }

    public Invoice Submit(string invoiceId)
    {
        var invoice = Find(invoiceId);

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Invoice {invoiceId} is {invoice.Status}; only drafts can be submitted.");
        }

        invoice.Status = InvoiceStatus.PendingApproval;
        _repository.Update(invoice);
        return invoice;
    }

    /// <summary>
    /// Kept for backward compatibility. Behaves exactly like the role overload with role
    /// "Approver", so it cannot be used to get around an approval limit.
    /// </summary>
    public Invoice Approve(string invoiceId, string approver) =>
        Approve(invoiceId, approver, ApproverRole);

    /// <summary>
    /// Approves a pending invoice. Up to the standalone ceiling one approval within the role's limit
    /// decides it. Above the ceiling two approvals from two distinct users are required: the first
    /// by any eligible role (the invoice stays pending), the second by a different Director.
    /// Every check runs before the invoice is touched, so a rejected attempt changes nothing.
    /// </summary>
    public Invoice Approve(string invoiceId, string approver, string role)
    {
        lock (DecisionLock)
        {
            var invoice = Find(invoiceId);
            EnsurePending(invoice);

            if (string.IsNullOrWhiteSpace(approver))
            {
                throw new ArgumentException("An approver name is required.");
            }

            var limit = LimitFor(role);
            var requiresDualApproval = invoice.Amount > StandaloneCeiling;
            var isSecondApproval = false;

            if (requiresDualApproval)
            {
                isSecondApproval = invoice.Approvals.Count > 0;
                if (isSecondApproval)
                {
                    if (invoice.Approvals.Any(existing =>
                            string.Equals(existing.Approver, approver, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ArgumentException(
                            $"{approver} has already approved this invoice; a different approver is required.");
                    }

                    if (role != DirectorRole)
                    {
                        throw new ArgumentException("The second approver for this invoice must be a Director.");
                    }
                }
            }
            else if (invoice.Amount > limit)
            {
                throw new ArgumentException(
                    $"{role} approval limit is {FormatAmount(limit)}; invoice amount is {FormatAmount(invoice.Amount)}.");
            }

            var decidedAt = DateTime.UtcNow;
            invoice.AddApproval(new InvoiceApprovalRecord(approver, role, decidedAt));

            if (requiresDualApproval && !isSecondApproval)
            {
                // First of two approvals: recorded, but the invoice stays pending and undecided.
                _repository.Update(invoice);
                return invoice;
            }

            invoice.Status = InvoiceStatus.Approved;
            invoice.DecidedBy = approver;
            invoice.DecidedAt = decidedAt;
            _repository.Update(invoice);
            return invoice;
        }
    }

    public Invoice Reject(string invoiceId, string approver, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to reject an invoice.", nameof(reason));
        }

        lock (DecisionLock)
        {
            var invoice = Find(invoiceId);
            EnsurePending(invoice);

            invoice.Status = InvoiceStatus.Rejected;
            invoice.DecidedBy = approver;
            invoice.DecidedAt = DateTime.UtcNow;
            invoice.DecisionNote = reason;
            _repository.Update(invoice);
            return invoice;
        }
    }

    private static decimal LimitFor(string role) => role switch
    {
        ManagerRole => ManagerLimit,
        DirectorRole or ApproverRole or AdminRole => StandaloneCeiling,
        _ => throw new ArgumentException($"Role '{role}' is not eligible to approve invoices."),
    };

    // Same text as before for every amount with at most two decimals (50,001; 500,000.01), but
    // never rounds away real digits: 50000.001 must not read "50,000" in a limit message.
    private static string FormatAmount(decimal amount) =>
        amount.ToString("#,##0.############################", CultureInfo.InvariantCulture);

    private Invoice Find(string invoiceId) =>
        _repository.GetById(invoiceId)
        ?? throw new KeyNotFoundException($"Invoice {invoiceId} was not found.");

    private static void EnsurePending(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} is {invoice.Status}; only pending invoices can be decided.");
        }
    }
}
