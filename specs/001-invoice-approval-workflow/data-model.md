# Phase 1 Data Model: Invoice Approval Workflow

Amounts are ₹ (INR) held in `decimal`. Authoritative rules: the *Approval Decision Table* in
`spec.md`.

## Entities

### Invoice (extends existing `InvoiceApp.Domain.Invoice`)

| Field | Type | Notes |
|---|---|---|
| `Id`, `CustomerName`, `Amount`, `CreatedAt` | unchanged | `Amount` drives limit and dual-approval checks. |
| `Status` | `InvoiceStatus` | Unchanged enum. No new value (see research.md §3). |
| `DecidedBy` / `DecidedAt` | `string?` / `DateTime?` | Set only on the final Approved/Rejected transition. For a dual-approved invoice they hold the **second** (last) approver. Remain `null` after a first approval. |
| `DecisionNote` | `string?` | Rejection reason; `null` on approval. |
| `Approvals` | `IReadOnlyList<InvoiceApprovalRecord>` *(new)* | Getter-only view over a private list; only `AddApproval(record)` can append; it can never be replaced, reordered or cleared. Empty until the first successful approval. |

**Invariants** (enforced in `ApprovalService`, plus the append-only shape of `Approvals`):

1. No two `Approvals` entries share an `Approver` (compared ordinal-ignore-case).
2. `Amount` ≤ 500,000: exactly one approval, and the approver's role limit ≥ `Amount`, before
   `Status` becomes Approved.
3. `Amount` > 500,000: `Status` becomes Approved only when `Approvals.Count == 2`, entry 0 by
   any eligible role, entry 1 with `Role == "Director"` and a different `Approver` than entry 0.
4. A rejected attempt appends nothing and changes no field (FR-006).
5. Rejection (any amount, any `Approvals.Count`) sets Status = Rejected, `DecidedBy`,
   `DecidedAt`, `DecisionNote`; `Approvals` is preserved as history.

**State transitions**:

```text
Draft --Submit--> PendingApproval
PendingApproval --Approve (Amount <= 500,000, role limit OK)--> Approved        [1 record]
PendingApproval --Approve (Amount > 500,000, Approvals.Count == 0, eligible role)--> PendingApproval  [1 record]
PendingApproval --Approve (Amount > 500,000, Count == 1, different user, Director)--> Approved   [2 records]
PendingApproval --Reject (any amount, any Count)--> Rejected
any state --invalid attempt--> unchanged (exception thrown, no field touched)
```

### InvoiceApprovalRecord (new, `InvoiceApp.Domain`)

`public record InvoiceApprovalRecord(string Approver, string Role, DateTime ApprovedAt);`

`Approver` is the session username; `Role` is the session role at the time (used to check
"second approver is a Director" without re-deriving it); `ApprovedAt` is UTC.

### Role → Limit mapping (private to `ApprovalService`, named constants)

| Role | Standalone limit | May be 1st approver above 500,000 | May be 2nd approver above 500,000 |
|---|---|---|---|
| Manager | 50,000 | Yes | No |
| Director | 500,000 | Yes | Yes (if a different user) |
| Approver | 500,000 | Yes | No |
| Admin | 500,000 | Yes | No |
| any other / blank | — | No (`ArgumentException`: role not eligible) | No |

Constants: `ManagerLimit = 50_000m`, `StandaloneCeiling = DualApprovalThreshold = 500_000m`.
A single lookup returns the role's standalone limit; "may be second approver" is
`role == "Director"`. Roles are matched ordinal (case-sensitive) because they come from
server-controlled configuration.

## Repository Interface

`IInvoiceRepository` is unchanged. `Invoice.Approvals` is persisted with the aggregate via the
existing `Update(Invoice)`; `InMemoryInvoiceRepository` already replaces by `Id`.

Note for tests: `FakeInvoiceRepository` and `InMemoryInvoiceRepository` hand back the same
instance, so integrity assertions must compare against a **value snapshot taken before the call**
(`InvoiceSnapshot`: Status, DecidedBy, DecidedAt, DecisionNote, copy of Approvals), not the
object itself.
