# Implementation Plan: Invoice Approval Workflow

**Branch**: `001-invoice-approval-workflow` | **Date**: 2026-10-30 | **Revised**: 2026-11-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-invoice-approval-workflow/spec.md`

## Summary

Extend the existing approval lifecycle (`ApprovalService`, `Invoice`, `ApprovalsController`)
with Manager (≤ ₹50,000) and Director (≤ ₹500,000) roles alongside Approver/Admin (≤ ₹500,000
standalone), plus mandatory dual approval above ₹500,000 for **every** role: first approval by
any eligible role, second by a different Director. All validation lives in Domain and finishes
before any mutation; controllers stay HTTP-only. Delivered as two separately tested, reviewed,
approved and committed stages (constitution VIII); the specification is its own earlier commit.

## Technical Context

**Language/Version**: C# / .NET 8 (`backend/InvoiceApp.sln`)

**Primary Dependencies**: ASP.NET Core 8, existing `SessionStore`/`SessionAuthMiddleware`,
xUnit + `FakeInvoiceRepository`, Stryker.NET 4.13.0, Playwright Test (existing `e2e/` has only
`playwright.config.ts`; page objects and specs are net-new). **No new NuGet/npm package**; the
tests project gains one `ProjectReference` to `InvoiceApp.Api` for controller/middleware tests.

**Storage**: N/A — existing singleton in-memory repository.

**Testing**: `dotnet test backend/InvoiceApp.sln`; `cd e2e && npm test`; Stryker.NET 4.13.0
against `InvoiceApp.Domain`.

**Target Platform**: existing Kestrel app at `http://localhost:5000`.

**Constraints**: validation completes before mutation; existing tests unchanged; no new
datastore/service/package; Approver/Admin behaviour at ≤ 500,000 preserved, above 500,000 they
join dual approval (D3, an approved behaviour change).

**Scale/Scope**: demo scale; no performance targets.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Domain ownership | Limits, roles, sequencing and identity checks in `ApprovalService`; controller passes session username and role only. | PASS |
| II. Single-approver limits | Manager 50,000; Director/Approver/Admin 500,000 standalone; above 500,000 only via the Principle III workflow; boundaries tested. | PASS |
| III. Dual approval | Two distinct users (case-insensitive), first by any eligible role, second a Director, no role bypass; matches constitution v1.1.0. | PASS |
| IV. Rejection & state integrity | Validate-then-mutate; value snapshots in tests; append-only `Approvals`. | PASS |
| V. Server-side authorization | Identity and role only from session; controller test proves it. | PASS |
| VI. Testing discipline | Per-rule tests; E2E rejected over-limit; existing tests untouched. | PASS |
| VII. Minimal change | No datastore/package; behaviour change for Approver/Admin >500,000 is approved (D3). | PASS |
| VIII. Staged delivery | Stage 1 / Stage 2 each with tests, review, approval, commit. | PASS |

**Constitution amendment (done 2026-11-01, v1.1.0, MINOR)**: Principle III now allows any
eligible role as first approver, forbids any bypass, fixes case-insensitive distinctness and
first-approval metadata; Principle II lists the Approver/Admin 500,000 standalone limit and
states that no approver may *independently* approve above their standalone limit, and that
above 500,000 approvals are recorded only through the Principle III workflow (the first
approval does not approve the invoice). Task T001 records the owner's approval of the amended text. No other conflicts.

## Project Structure

```text
specs/001-invoice-approval-workflow/
├── spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
├── contracts/approvals-api.md
├── mutation-scores.md        # NEW at execution: real Stryker output only
└── review-findings.md        # NEW at execution: Fixed / Dismissed / Deferred log
```

```text
backend/src/InvoiceApp.Domain/
├── ApprovalService.cs          # + Approve(id, approver, role); 2-arg delegates as "Approver"
├── Invoice.cs                  # + append-only Approvals (private list, AddApproval)
└── InvoiceApprovalRecord.cs    # NEW record
backend/src/InvoiceApp.Api/
├── Controllers/ApprovalsController.cs   # RequireRole list; passes session role
├── appsettings.json                     # demo.manager, demo.director, demo.director2
└── wwwroot/app.js, index.html           # reason display (Stage 1), pending progress (Stage 2)
backend/tests/InvoiceApp.Tests/
├── ApprovalServiceTests.cs     # extended; existing tests unmodified
├── InvoiceSnapshot.cs          # NEW test helper (value snapshot)
├── ApprovalsControllerTests.cs # NEW: session-derived identity/role
├── AuthPipelineTests.cs        # NEW: SessionAuthMiddleware 401, RequireRole 403
└── InvoiceApp.Tests.csproj     # + ProjectReference to InvoiceApp.Api (no packages)
e2e/pages/InvoiceApprovalPage.ts      # NEW page object
e2e/tests/rejected-over-limit-approval.spec.ts   # NEW (Stage 1)
e2e/tests/dual-approval.spec.ts       # NEW (Stage 2)
ASSIGNMENT.md                          # NEW root deliverable
logs/                                  # AI interaction records
```

**Structure Decision**: three existing projects plus `e2e/`; no new project, no new controller,
no `ApprovalRejectedException` (D5).

## Staged Delivery (constitution Principle VIII)

### Gate 0 — Specification commit
Constitution amendment approved, spec/plan/tasks analysed clean, human approves; commit
**"specification"** (separate commit). Then baseline: full test run and Stryker baseline.

### Stage 1 — Single-approver limits (self-contained, independently shippable)

Behaviour after Stage 1:
- ≤ 500,000: Manager ≤ 50,000; Director/Approver/Admin ≤ 500,000 → Approved with one record.
  Manager above 50,000 → `ArgumentException` (Manager limit message).
- > 500,000: **fail-closed** — every role is rejected with the "standalone limit" message.
  No dual-approval wording or logic exists in Stage 1.
- Reject open to all four roles at any amount. Existing tests unchanged and green.
- UI shows the rejection reason; E2E "rejected over-limit approval" passes (Manager, 50,001+).

Contents: `Invoice.Approvals`/`InvoiceApprovalRecord`, `Approve` overload and limit lookup,
controller role list and session role, demo accounts (added *after* the domain rules so no
account can approve before limits exist), UI error display, unit/controller/auth tests, E2E.

**Stage 1 gate** (all required, in order): full `dotnet test` green → Stage 1 mutation score
recorded → `npm test` green → test review (assertions check the rule, not coverage) →
independent `review-agent` review with every finding logged → **human approval** → commit
"Stage 1".

### Stage 2 — Dual approval above 500,000

Replaces the fail-closed branch with the dual flow of the Decision Table. The Stage 1 interim
test `Approve_AboveDualThreshold_IsRejectedForEveryRole_Stage1Interim` is replaced on purpose by
the Stage 2 tests (it was written for this feature and records interim behaviour; no pre-feature
test changes). UI shows "second Director approval required". One extra E2E for dual progress.

**Stage 2 gate**: full `dotnet test` → final mutation score vs baseline and Stage 1, survivors
reviewed (boundaries, duplicate-approver, second-role) → `npm test` → test review → independent
review (new findings logged) → **human approval** → commit "Stage 2".

## Testing Strategy

- **Snapshot rule**: every "unchanged" assertion compares to an `InvoiceSnapshot` taken before
  the call (Status, DecidedBy, DecidedAt, DecisionNote, value copy of Approvals).
- **Unit** (`ApprovalServiceTests`): boundaries 50,000/50,001, 500,000/500,000.01 for each of
  the four roles; unknown role; blank approver; non-pending 409 precedence; legacy two-arg form
  behaves as `Approver`; Reject at huge amounts by Manager/Director; Stage 2 sequences for all
  first-approver roles, Director+Director, same user (incl. case difference), non-Director
  second roles, reject after first approval, third approval on Approved.
- **Controller/auth**: session-derived username and role passed to the domain; forged body ignored;
  `SessionAuthMiddleware` 401 before any controller; `RequireRoleAttribute` 403 for other roles.
- **E2E** (role-based locators, page object; data arranged via API with `demo.admin`).
- **Mutation**: baseline (before production code), Stage 1 score, final score — real output only,
  recorded in `mutation-scores.md`; report explicitly if Stryker cannot run.

## Risks, Dependencies, Assumptions, Trade-offs

**Risks**: `Invoice` is shared by tests/UI — `Approvals` is additive. D3 changes Approver/Admin
above 500,000; existing tests (amount 100) and seed data (<13,000) are unaffected. Case-insensitive
username comparison is a design choice stated in the spec.

**Dependencies**: owner sign-off on constitution v1.1.0 (T001); Stryker.NET 4.13.0 installable; `demo.director2`
account for dual-approval tests.

**Trade-offs**: no new status value (use `Approvals.Count`); plain-string reasons; controller
tests via direct construction rather than `WebApplicationFactory` to avoid a new package.
