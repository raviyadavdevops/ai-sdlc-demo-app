# Phase 0 Research: Invoice Approval Workflow

No `[NEEDS CLARIFICATION]` markers remain in the Technical Context (all were resolved by
inspecting the existing codebase, which is the authoritative source for this extension). The
items below record the concrete decisions and their rationale.

## 1. How are Manager/Director represented without assuming Approver/Admin equivalence?

- **Decision**: Add `"Manager"` and `"Director"` as two new literal role strings, exactly like
  `"Approver"` and `"Admin"` are today — no enum, no role hierarchy object. Each approval/
  rejection endpoint keeps using `[RequireRole(...)]` but with an expanded role list that
  includes Manager and Director alongside the existing Approver/Admin (resolved by
  `/speckit-clarify`: added alongside, not replacing). The amount-limit and dual-approval
  rules are then enforced *inside* `ApprovalService`, keyed off the role string carried in the
  authenticated `UserSession`, not by `RequireRoleAttribute`.
- **Rationale**: `RequireRoleAttribute` already only checks "is this role in the allowed
  list" (`backend/src/InvoiceApp.Api/Auth/RequireRoleAttribute.cs:17-25`) — it has no concept
  of amount limits, so it cannot and should not try to enforce them. Keeping roles as plain
  strings matches the existing `UserSession(string Username, string Role)` record exactly,
  requiring zero changes to the session/auth plumbing.
- **Alternatives considered**:
  - *Role enum with a hierarchy (Approver < Manager < Director < Admin)*: rejected — the
    clarified spec explicitly treats Approver/Admin as independent of Manager/Director, not
    part of the same ladder; a hierarchy would silently reintroduce the equivalence the
    clarification rejected.
  - *New `IApprovalLimitPolicy` abstraction injected via DI*: rejected for this scope — only
    two fixed limits (50,000 / 500,000) exist; an abstraction layer adds indirection the
    constitution's "avoid unnecessary abstractions" principle (VII) argues against for a
    two-value lookup. A plain method/constant map inside `ApprovalService` is sufficient and
    easier to unit test directly.

## 2. How is approval limit data modeled?

- **Decision** (updated 2026-11-01, D3): A small, private static lookup inside the Domain
  layer (switch expression mapping role → standalone limit): Manager = 50,000; Director,
  Approver and Admin = 500,000; any other role → `ArgumentException`. There is no "unlimited"
  role any more; above 500,000 every role goes through dual approval.
- **Rationale**: These limits are fixed business constants per the constitution (Principle II),
  not configuration that should vary at runtime; hardcoding them in the Domain layer next to
  the rules that use them keeps the rule and its threshold co-located and testable without
  extra configuration plumbing.
- **Alternatives considered**: `appsettings.json`-driven limits — rejected; the constitution
  treats the limit values as non-negotiable business rules, not deployment configuration, and
  externalizing them would let a config change silently alter a financial control without
  code review.

## 3. How is the dual-approval sequence represented and validated?

- **Decision**: `Invoice` gains an ordered, append-only collection of recorded approvals
  (`IReadOnlyList<InvoiceApprovalRecord>`), each holding `Approver` (username), `Role`, and
  `ApprovedAt`. `ApprovalService.Approve` becomes sequence-aware:
  1. Validate the invoice is pending, and the approver and role are present and eligible.
  2. If the amount is ≤500,000: the role's standalone limit must cover it (Manager 50,000;
     Director/Approver/Admin 500,000); one approval is recorded and the invoice becomes
     Approved. A Manager above 50,000 is rejected (D1: no standalone authority).
  3. If the amount exceeds 500,000 (every role, D3): with no recorded approval, any eligible
     role may give the first approval — it is appended and the invoice **stays**
     `PendingApproval` with `DecidedBy`/`DecidedAt` unset.
  4. With one recorded approval: the new approver must not be the same user (checked first),
     and must be a Director (D2). Then the second record is appended and the invoice becomes
     Approved with `DecidedBy`/`DecidedAt` of the second approver.
  5. All checks run before any field of the invoice is touched; a new record is appended only
     in the same block as the status change.
- **Rationale**: This keeps `InvoiceStatus` unchanged (still Draft/PendingApproval/Approved/
  Rejected — constitution Principle VII, "avoid unnecessary architectural redesign") by using
  the approval-record collection's length, not a new status value, to distinguish "no
  approvals yet" from "one of two approvals recorded." It also directly satisfies spec FR-003's
  "must not transition to Approved until all required approvals have been successfully
  recorded."
- **Alternatives considered**: Adding a new `InvoiceStatus.PartiallyApproved` value — rejected;
  it would ripple into every existing `switch`/comparison on `InvoiceStatus` across the
  codebase (UI, controller, other tests) for no behavioral gain, since "has one approval
  recorded but still pending" is already fully expressible via the approval-record collection's
  count while the invoice remains `PendingApproval`.

## 4. How is "same user cannot approve twice" enforced?

- **Decision**: `ApprovalService` checks the authenticated username (from
  `HttpContext.GetSession().Username`, passed in as today) against every `Approver` value
  already present in the invoice's approval-record collection before accepting a new approval.
- **Rationale**: Directly satisfies FR-003 using data already being threaded through the
  existing `Approve(string invoiceId, string approver)` call shape; no new identity source is
  needed.
- **Alternatives considered**: Session-level "has this user already acted on this invoice in
  this session" tracking — rejected; this would miss the case of the same user approving via
  two different sessions/tokens, which the business rule does not permit.

## 5. How do rejected attempts guarantee zero invoice-state mutation?

- **Decision** (updated, D5): Keep the "find → validate → mutate → persist" pattern: all
  checks run before the first assignment to the invoice, and `_repository.Update` is called
  last. Failures for limit, role eligibility and duplicate approver throw the existing
  `ArgumentException` (→ 400 via `ApiControllerBase.Handle`); no new exception type is added.
  Because the fake and in-memory repositories return the same instance, tests prove
  "no mutation" with a value snapshot taken before the call, and `Invoice.Approvals` is
  append-only so history cannot be replaced.
- **Rationale**: `ApprovalService.Approve`/`Reject` already follow "find → validate → mutate →
  persist" (`backend/src/InvoiceApp.Domain/ApprovalService.cs:31-41`); no validation currently
  happens after `_repository.Update` is called. Extending the same ordering for the new checks
  requires no structural change, only additional validation steps before the existing mutation
  line, which is the simplest way to satisfy constitution Principle IV and spec FR-006.
- **Alternatives considered**: Validate-then-clone-then-swap (build a new `Invoice` object and
  only ever write the finished object) — rejected as unnecessary; the existing code already
  never calls `Update` until all checks pass, and introducing cloning would be an unrelated
  refactor the constitution's "avoid unnecessary refactoring" principle argues against.

## 6. API contract and backward compatibility

- **Decision**: No breaking changes to existing routes or response shapes.
  `POST api/approvals/{invoiceId}/approve` and `.../reject` keep their current signatures;
  the `Invoice` DTO returned gains one new optional field exposing the current approval-record
  list for clients that want to render progress (e.g., `"Approvals": [...]`), which existing
  clients can ignore. Existing single-approval invoices after this feature ships will simply
  have a one-element (or zero-element, for pre-feature history) `Approvals` list.
- **Rationale**: Constitution Principle VII (backward compatibility) and spec NFR "backward
  compatibility." Additive JSON fields are non-breaking for any reasonable existing client
  (the only existing client is this repo's own `wwwroot` UI).
- **Alternatives considered**: A new `GET api/invoices/{id}/approvals` sub-resource endpoint —
  deferred as optional UI polish, not required to satisfy any FR; can be added later without
  conflicting with this plan if the UI work in Stage 2 finds it necessary.

## 7. Testing strategy sources

- **Decision**: Extend `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs` (same file,
  same `<ClassUnderTest>Tests` convention) using the existing `FakeInvoiceRepository` — no new
  test double needed since it already supports arbitrary seeded invoices and records `Update`
  calls. New Playwright spec/page-object pair created from scratch under `e2e/tests/` and
  `e2e/pages/` (neither directory currently has content) following the config already present
  in `e2e/playwright.config.ts`.
- **Rationale**: Matches `AGENTS.md`'s stated conventions even though the directories are
  currently empty — this feature is what populates them for the first time.
- **Alternatives considered**: None — this is a direct application of existing conventions.

## 8. Method signature and test seam (resolved, D5)

- **Decision**: add `Approve(string invoiceId, string approver, string role)`; keep
  `Approve(string invoiceId, string approver)` delegating with role `"Approver"` so existing
  tests and callers compile and behave as before (amount 100) while the legacy form can never
  bypass limits. The controller calls only the three-argument form.
- **API-level testing without new packages**: the test project references only Domain. Add a
  `ProjectReference` to `InvoiceApp.Api` (no NuGet package) and unit-test `ApprovalsController`
  with a `DefaultHttpContext` carrying a session, plus `RequireRoleAttribute` and
  `SessionAuthMiddleware` directly; Playwright covers the real HTTP path. A
  `Microsoft.AspNetCore.Mvc.Testing` package is deliberately not added (constitution VII).

## 9. Resolved decisions log

Open decisions from the first analysis are closed by the 2026-11-01 approvals D1–D5 recorded in
`spec.md` → Clarifications. The earlier "Approver/Admin remain unlimited" assumption in this
file and the plan is superseded.
