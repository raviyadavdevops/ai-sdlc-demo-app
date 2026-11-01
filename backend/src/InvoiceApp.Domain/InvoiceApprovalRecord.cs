namespace InvoiceApp.Domain;

/// <summary>One recorded approval of an invoice: who approved it, in which role, and when.</summary>
public record InvoiceApprovalRecord(string Approver, string Role, DateTime ApprovedAt);
