# API Contract: Invoice Approval Endpoints

Routes and verbs are unchanged. Every route requires a valid bearer session
(`SessionAuthMiddleware`) and `[RequireRole("Approver", "Admin", "Manager", "Director")]`.
Amounts are ₹. Rules: `spec.md` → Approval Decision Table.

## `POST /api/approvals/{invoiceId}/approve`

**Request**: no body. Identity and role are read only from the session; any body, header or
query value claiming a user or role is ignored.

**Response `200 OK`** — the `Invoice` with a new additive `approvals` array:

```json
{
  "id": "INV-2001", "customerName": "Example School District", "amount": 600000.00,
  "status": "PendingApproval", "createdAt": "2026-11-01T12:00:00Z",
  "decidedBy": null, "decidedAt": null, "decisionNote": null,
  "approvals": [ { "approver": "demo.manager", "role": "Manager", "approvedAt": "2026-11-01T12:05:00Z" } ]
}
```

- Amount > 500,000, first approval: `status` stays `"PendingApproval"`, `decidedBy`/`decidedAt`
  are `null`, `approvals` has one entry.
- Final approval: `status` is `"Approved"`; `decidedBy`/`decidedAt` are the **last** recorded
  approval's approver and time.

**Response `400 Bad Request`** (`{ "error": "<message>" }`, from `ArgumentException`). Fixed
message templates (tests assert these exact strings). Amount `{n}` is formatted with
InvariantCulture and prints every significant decimal digit (for example 50,001 and 500,000.01
print exactly, and 50000.001 prints as 50,000.001):

| Rule | Message |
|---|---|
| Manager over limit | `Manager approval limit is 50,000; invoice amount is {n}.` |
| Director/Approver/Admin over standalone limit (Stage 1 interim only) | `{Role} approval limit is 500,000; invoice amount is {n}.` |
| Same user twice | `{username} has already approved this invoice; a different approver is required.` |
| Second approver not a Director | `The second approver for this invoice must be a Director.` |
| Role not eligible / unknown | `Role '{role}' is not eligible to approve invoices.` |
| Blank approver | `An approver name is required.` |

**`403`** role not allowed or **`401`** no valid session: unchanged. **`404`** invoice not
found: unchanged. **`409`** invoice not `PendingApproval` (includes a further approval of an
already-Approved invoice): unchanged.

**Stage 1 vs Stage 2**: after Stage 1, an approval attempt on an invoice above 500,000 is
rejected for every role with the "standalone limit" message (fail-closed interim behaviour).
Stage 2 replaces that with the dual-approval flow above. That interim test is replaced
deliberately in Stage 2 (it is a test written for this feature, not an existing one).

## `POST /api/approvals/{invoiceId}/reject`

`{ "reason": "<string>" }` in; `Invoice` out, same additive `approvals` field. Any of
Manager/Director/Approver/Admin may reject at any amount and at any `approvals` count (D4).
Blank reason → `400` (unchanged). Prior approvals are preserved in `approvals` as history;
`decidedBy`/`decidedAt`/`decisionNote` are the rejecter's values. Not-pending → `409`.

## Compatibility notes

- No new endpoint, no field removed, renamed or retyped. Existing 2xx/4xx mappings are kept.
- Behaviour change (approved decision D3): Approver/Admin approvals above 500,000 now need
  dual approval. All existing tests (amount 100) are unaffected.
