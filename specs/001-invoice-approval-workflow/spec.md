# Feature Specification: Invoice Approval Workflow

**Feature Branch**: `001-invoice-approval-workflow`

**Created**: 2026-10-30

**Last Updated**: 2026-11-01 (business-rule decisions D1–D5 incorporated)

**Status**: Draft — awaiting human approval

**Input**: User description: "Implement a role-based invoice approval workflow with individual
approval limits and a dual-approval requirement for invoices exceeding 500,000, enforced
server-side, maintaining invoice state integrity."

**Currency**: All amounts are in ₹ (INR). The codebase stores `Invoice.Amount` as a plain
`decimal`; no currency conversion or symbol handling is introduced.

## Verified Existing Behavior

<!-- Confirmed by reading backend/src/InvoiceApp.Domain and backend/src/InvoiceApp.Api. -->

- Invoices move Draft → PendingApproval → Approved or Rejected via `ApprovalService`
  (`Submit`, `Approve(id, approver)`, `Reject(id, approver, reason)`). A pending invoice is
  decided as a whole, by a single approver — there is currently no concept of a second approval.
- An `Invoice` carries one `DecidedBy`, one `DecidedAt`, and one `DecisionNote`; there is no
  field recording multiple approvers or a partial approval history.
- Authentication is a bearer-token session (`SessionAuthMiddleware`, `SessionStore`), set up
  from demo accounts (`DemoUsersOptions`) with only two existing roles: **Approver** and
  **Admin**. `ApprovalsController` restricts approve/reject to `[RequireRole("Approver",
  "Admin")]`, and the approver identity is read from the session
  (`HttpContext.GetSession().Username`), never from the request body.
- There are currently **no amount-based approval limits** and **no Manager/Director roles**.
- `Reject` already requires a non-empty reason and validates state (`EnsurePending`) before
  mutating. `Approve` also validates before mutating. Failures are `KeyNotFoundException`
  (404), `InvalidOperationException` (409) and `ArgumentException` (400), mapped by
  `ApiControllerBase.Handle`.
- `FakeInvoiceRepository` returns the **same object reference** from `GetById` and its `Update`
  is a no-op; `InMemoryInvoiceRepository` stores the same instances. Mutation of a fetched
  invoice is therefore immediately visible, so state-integrity tests must snapshot values
  *before* the call (see FR-006).
- Existing `ApprovalServiceTests` use invoices with `Amount = 100m` and call
  `Approve("INV-1", "demo.approver")`; all seeded demo invoices are below 13,000.
- `e2e/` contains only `playwright.config.ts` — no page objects or specs exist yet.
- `InvoicesController` has endpoints to create (`POST api/invoices`) and submit
  (`POST api/invoices/{id}/submit`); no seeded invoice exceeds 50,000.

## Clarifications

### Session 2026-10-30

- Q: A Director's own 500,000 limit when acting as the mandatory second approver on an invoice
  above 500,000? → A: The 500,000 limit applies only to independent single-approver decisions;
  it does not constrain a Director acting as the second approver in dual approval.
- Q: Should Manager/Director replace or be added alongside the existing Approver role? → A:
  Added alongside. Existing demo accounts keep working. *(The "unrestricted approval" part of
  this answer is superseded by D3 below.)*

### Session 2026-11-01 — approved decisions (do not re-ask)

- **D1 – Manager first approval**: A Manager may give the first approval on an invoice above
  500,000 only as part of the mandatory dual-approval workflow. This grants no standalone
  approval authority above 50,000. A second approval from a different Director is mandatory.
- **D2 – Sequence**: Above 500,000 the first approver may be a Manager or Director (or
  Approver/Admin, see D3). The second approver MUST be a different user whose role is
  Director. A Director may be both first and second approver only if they are two distinct
  users.
- **D3 – Approver/Admin**: The dual-approval requirement above 500,000 applies to **all**
  roles, including Approver and Admin; no role bypasses the two-distinct-approver rule.
  Approver and Admin keep their existing standalone permission for invoices up to and
  including 500,000. This **supersedes** the earlier "unrestricted" wording.
- **D4 – Rejection**: Manager and Director may reject an invoice irrespective of its amount.
  Rejection is not subject to approval limits and follows existing authorization and
  state-transition rules.
- **R1–R5 (approved 2026-11-01)**: constitution amended to v1.1.0 (Principle III); the legacy
  `Approve(id, approver)` maps explicitly to role `Approver` and the controller uses only the
  session-role overload; Approver/Admin stand alone up to 500,000 and dual-approve above;
  Stage 1 rejects every role above 500,000 with no dual-approval logic or wording; duplicate
  approvers are compared case-insensitively; after a first approval the invoice stays pending,
  the approval is recorded, and `DecidedBy`/`DecidedAt` stay unset until the second approval.
- **D5 – API shape**: Add an `Approve(id, approver, role)` overload; keep the existing
  `Approve(id, approver)` signature. Use `ArgumentException` for amount-limit, role-eligibility
  and duplicate-approver failures. No `ApprovalRejectedException`.

## Approval Decision Table (authoritative)

`Amount` ≤ 500,000 ("standalone range"):

| Role | Standalone limit | Result when `Amount` ≤ limit | Result when `Amount` > limit |
|---|---|---|---|
| Manager | 50,000 | Approved (1 approval recorded) | Rejected attempt (`ArgumentException`, limit exceeded) — no dual path in the standalone range |
| Director | 500,000 | Approved | n/a (limit equals threshold) |
| Approver | 500,000 | Approved | n/a |
| Admin | 500,000 | Approved | n/a |

`Amount` > 500,000 ("dual-approval range", every role):

| Approvals already recorded | Acting role | Result |
|---|---|---|
| 0 | Manager, Director, Approver, Admin | First approval recorded; invoice stays `PendingApproval`; `DecidedBy`/`DecidedAt` stay unset |
| 1 | Director, different user from the first approver | Second approval recorded; invoice becomes `Approved`; `DecidedBy`/`DecidedAt` = second approver |
| 1 | Same user as the first approver (any role) | Rejected attempt: same user cannot approve twice |
| 1 | Manager, Approver or Admin, different user | Rejected attempt: second approver must be a Director |

Check order inside `Approve`: invoice exists (404) → invoice is `PendingApproval` (409) →
approver/role valid and role eligible (400) → limit / sequence rules (400) → only then mutate.
When more than one rule is violated, the first failing check in this order is reported (e.g. the
same-user check precedes the second-approver-role check).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Manager approves within limit (Priority: P1)

A Manager approves a pending invoice at or below 50,000 in one step.

**Why this priority**: Most common case and the foundation the rest builds on.

**Independent Test**: Sign in as a Manager, approve an invoice of exactly 50,000, confirm it
reaches Approved with the Manager recorded.

**Acceptance Scenarios**:

1. **Given** a pending invoice of exactly 50,000 and an authenticated Manager, **When** the
   Manager approves it, **Then** it becomes Approved and the Manager is recorded as the approver.
2. **Given** a pending invoice of 50,001 (up to 500,000), **When** a Manager attempts to
   approve it, **Then** the attempt is rejected with a reason naming the Manager limit, and the
   invoice is unchanged.

---

### User Story 2 - Director, Approver and Admin approve within the standalone range (Priority: P1)

A Director, Approver or Admin approves a pending invoice at or below 500,000 in one step.

**Independent Test**: Approve an invoice of exactly 500,000 as each of the three roles; each
reaches Approved with that user recorded.

**Acceptance Scenarios**:

1. **Given** a pending invoice of exactly 500,000, **When** a Director, Approver or Admin
   approves it, **Then** it becomes Approved with that user recorded as the sole approver.
2. **Given** a pending invoice of exactly 500,000, **When** a Manager attempts to approve it,
   **Then** the attempt is rejected (Manager limit) and the invoice is unchanged.

---

### User Story 3 - Dual approval above 500,000 (Priority: P2)

Above 500,000 two distinct users must approve, the second being a Director, regardless of role.

**Why this priority**: Depends on Stories 1–2; it is the more complex control layered on top.

**Independent Test**: Submit a 600,000 invoice; a Manager gives the first approval (invoice
stays pending); a different Director gives the second (invoice Approved, two records).

**Acceptance Scenarios**:

1. **Given** a pending 600,000 invoice, **When** a Manager then a different Director approve,
   **Then** it is Approved only after the second approval and both approvers are recorded.
2. **Given** a pending 600,000 invoice, **When** a Director then a different Director approve,
   **Then** it is Approved after the second approval (D2).
3. **Given** a pending 600,000 invoice, **When** an Approver or Admin gives the first approval,
   **Then** it is recorded and the invoice stays pending (D3).
4. **Given** a pending 600,000 invoice, **When** any single user of any role approves,
   **Then** the invoice is **not** Approved after that one approval.
5. **Given** one approval recorded, **When** the same user approves again, **Then** the attempt
   is rejected ("same user cannot approve twice") and the invoice is unchanged.
6. **Given** one approval recorded, **When** a different Manager, Approver or Admin attempts
   the second approval, **Then** it is rejected ("second approver must be a Director") and the
   invoice is unchanged.

---

### User Story 4 - Rejection preserves invoice integrity (Priority: P1)

Any approver (Manager, Director, Approver, Admin) can reject a pending invoice with a reason at
any amount; any failed attempt leaves no trace on the invoice.

**Independent Test**: Attempt every category of invalid approval against a pending invoice and
confirm status, approvals, decision timestamp, decider and note are unchanged after each; a
valid rejection with a reason changes the invoice to Rejected.

**Acceptance Scenarios**:

1. **Given** a pending invoice of any amount, **When** a Manager or Director rejects it with a
   non-empty reason, **Then** it becomes Rejected, recording the rejecter, timestamp and
   reason (D4).
2. **Given** a pending invoice, **When** an approval attempt fails any validation (limit, role
   eligibility, duplicate approver, wrong second-approver role, non-pending status, blank
   approver), **Then** the response states the specific reason and the invoice is identical,
   field-for-field, to its state before the attempt.
3. **Given** a request without a valid session, **When** it targets an approve or reject
   endpoint, **Then** it is refused before any business rule runs and the invoice is unaffected.
4. **Given** a request whose body or headers carry a username or role differing from the
   session, **When** it is processed, **Then** only the session's username and role are used.

### Edge Cases

- Amount exactly 50,000 / 500,000 → inside the lower limit ("up to and including"). Amount
  500,000.01 or more → dual-approval range.
- Director first and a different Director second on an invoice above 500,000 → permitted (D2).
- Same user as first and second approver → rejected whatever their role; comparison of
  usernames is case-insensitive (ordinal-ignore-case) so `Demo.Director` equals `demo.director`.
- Admin or Approver as second approver → rejected (second must be a Director, D2/D3).
- Rejection when one approval is already recorded → invoice becomes Rejected; the recorded
  first approval stays in `Approvals` as history; `DecidedBy`/`DecidedAt`/`DecisionNote` are the
  rejecter's values. Rejection never leaves a PendingApproval invoice with a dropped approval.
- Approval attempt on a Draft/Approved/Rejected invoice → `InvalidOperationException` (409) with
  no change, including a third approval on an already-Approved dual-approval invoice.
- Unknown role string or blank approver name passed to the domain → `ArgumentException` with no
  change.
- Client-supplied identity or role in the request body/headers → ignored (FR-007).
- Legacy two-argument `Approve(id, approver)` → treated as role `Approver`, so it can never
  bypass the limits (FR-008). The controller never uses it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A Manager MUST be able to approve a pending invoice of amount ≤ 50,000, and an
  approval attempt by a Manager on an invoice of 50,000 < amount ≤ 500,000 MUST be rejected with
  a reason naming the Manager limit. A Manager has no standalone authority above 50,000 (D1).
- **FR-002**: A Director, Approver or Admin MUST be able to approve a pending invoice of
  amount ≤ 500,000 as the sole approver. The 500,000 limit does not apply to a Director acting
  as the second approver in the dual-approval path (Clarification 2026-10-30; D3).
- **FR-003**: For a pending invoice of amount > 500,000 the system MUST require two distinct,
  successfully recorded approvals before it becomes Approved, for every role (D3). The first
  approval MAY be by a Manager, Director, Approver or Admin. The second approval MUST be by a
  different user whose role is Director (D2). The same authenticated user MUST NOT provide both
  approvals. After the first approval the invoice MUST remain PendingApproval with the first
  approver recorded in its approval history.
- **FR-004**: For every approval attempt the system MUST validate invoice existence and status,
  approver identity, role eligibility, role limit, and (dual approval) the existing approval(s)
  and their approvers — server-side, independent of any UI check, in the order given by the
  Approval Decision Table.
- **FR-005**: A failed approval or rejection MUST return a clear reason naming the rule that
  was not satisfied. Domain failures for limits, role eligibility and duplicate approver MUST
  use `ArgumentException` (HTTP 400); non-pending status keeps `InvalidOperationException`
  (409); missing invoice keeps `KeyNotFoundException` (404). Exact messages are fixed in
  `contracts/approvals-api.md` (no `ApprovalRejectedException`, D5).
- **FR-006**: All validation MUST complete before any change to an invoice. A rejected approval
  or rejection MUST leave `Status`, `Approvals`, `DecidedBy`, `DecidedAt` and `DecisionNote`
  exactly as before the attempt — including that no approval is appended. Tests MUST capture a
  value snapshot before the call and compare it afterwards (the shared-reference fake would
  otherwise make a same-object comparison meaningless).
- **FR-007**: Only an authenticated user whose session carries an eligible role
  (`Manager`, `Director`, `Approver`, `Admin`) may approve or reject. The approver's username
  and role MUST come solely from the authenticated session, never from the request body or any
  client-controlled input.
- **FR-008**: `ApprovalService` MUST expose `Approve(id, approver, role)` and keep
  `Approve(id, approver)` for backward compatibility. The two-argument form MUST behave as the
  three-argument form with role `Approver`, so it cannot be used to bypass limits or dual
  approval. `ApprovalsController` MUST call only the three-argument form.
- **FR-009**: Manager and Director MAY reject a pending invoice of any amount; rejection is not
  subject to approval limits, still requires a non-empty reason, and follows existing
  authorization and state rules (D4). `Reject`'s signature is unchanged.

### Key Entities

- **Invoice** (existing, extended): gains an append-only `Approvals` history of
  `InvoiceApprovalRecord`. Existing `DecidedBy`/`DecidedAt`/`DecisionNote` keep their meaning
  for the final outcome. Shape detail in `data-model.md`.
- **InvoiceApprovalRecord** (new): approver username, role at the time, UTC timestamp.
- **Approval roles**: Manager (50,000), Director (500,000), Approver (500,000), Admin
  (500,000), each limited as in the Approval Decision Table.
- **Approval attempt**: one request to approve or reject; validated in full before any change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Automated tests cover the exact boundary and the first value above it for Manager
  (50,000 / 50,001) and for the 500,000 / 500,000.01 boundary (Director, Approver, Admin and
  Manager), with 100% of over-limit attempts rejected with a clear reason.
- **SC-002**: 100% of invoices above 500,000 need exactly two distinct approvals, the second by
  a Director, for every role combination in the table; tests assert the invoice is still
  PendingApproval after one approval.
- **SC-003**: 100% of rejected attempts (every category) leave a value snapshot of the invoice
  identical before and after.
- **SC-004**: 0 approvals or rejections succeed without a valid session and eligible role,
  verified by tests of `SessionAuthMiddleware` and `RequireRoleAttribute` and by a controller
  test proving identity/role come from the session.
- **SC-005**: Every rejection reply states the specific rule broken; at least one Playwright
  test asserts the displayed reason text for a rejected over-limit approval.
- **SC-006**: No new datastore, external service, new NuGet/npm dependency or change to
  invoice behaviour outside approval; the unmodified existing tests still pass.

## Assumptions

- Manager/Director demo accounts (e.g. `demo.manager`, `demo.director`, plus a second
  Director such as `demo.director2` for dual-approval tests and E2E) are added to
  `appsettings.json` with demo-only credentials. `demo.director2` uses its own unique, clearly
  fake password, never `ChangeMe123!` and never a real or reused credential.
- Limits (50,000 / 500,000) are fixed constants in the Domain layer, not configuration.
- UI changes are limited to showing the rejection reason and dual-approval progress.
- Existing in-memory repository suffices; no persistence added.

## Out of Scope

- A new database, external approval service, or change to Draft/Submit behaviour.
- Unrelated refactoring of controllers, services or UI.
- Bulk-approval and partial-approval workflows (`docs/use-cases/UC-1`, `UC-2`).
