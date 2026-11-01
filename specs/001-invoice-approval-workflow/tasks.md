---

description: "Task list for the Invoice Approval Workflow feature (revised 2026-11-01)"
---

# Tasks: Invoice Approval Workflow

**Input**: `specs/001-invoice-approval-workflow/` — spec.md (Approval Decision Table), plan.md,
research.md, data-model.md, contracts/approvals-api.md, quickstart.md.

**Tests**: mandatory (constitution VI). Write each test task first and confirm it fails before
the matching implementation task.

**Format**: `[ID] [P?] [Story] Description` — `[P]` = different files/independent methods.
Paths are repo-relative. `⛔ GATE` = stop for explicit human approval; never commit or push
without it (AGENTS.md §7–8).

**Agent ownership** (use an agent only if actually invoked; record it in `logs/`):
specification-agent → Gate 0; implementation-agent → implementation tasks; testing-agent →
test tasks and test review; review-agent → independent reviews. The main session coordinates;
no two agents edit the same file concurrently.

**Constants** (use named constants, never literals, in code and tests): `ManagerLimit =
50_000m`, `StandaloneCeiling = DualApprovalThreshold = 500_000m`.

---

## Phase 0: Gate 0 — Specification (commit 1)

- [x] T001 Constitution amended to v1.1.0 (MINOR) via `/speckit-constitution` on 2026-11-01:
      Principle III (any eligible first approver, no bypass, case-insensitive distinctness,
      first-approval metadata) and Principle II (Approver/Admin 500,000). Remaining: owner
      reviews the amended text and removes the temporary Sync Impact Report comment before the
      Gate 0 commit.
- [x] T002 `/speckit-analyze` re-run on the revised artifacts (2026-11-01); no CRITICAL/HIGH
      findings remain. Finding F1 (Principle VI does not name Approver/Admin) is **Deferred** by
      owner decision — no amendment now; revisit in a later PATCH.
- [ ] T003 [P] Keep the specification-session entries in `logs/ai-interactions.jsonl` (no
      secrets). The log is **not** part of Gate 0; it is preserved for the assignment deliverables.
- [ ] T004 ⛔ GATE 0: human approves spec, plan, tasks. Commit (commit 1 — "specification")
      **only** `.specify/memory/constitution.md` and the eight feature artifacts under
      `specs/001-invoice-approval-workflow/` (spec, plan, research, data-model, quickstart,
      tasks, `contracts/approvals-api.md`, `checklists/requirements.md`). `AGENTS.md`,
      `.claude/`, other `.specify/` tooling and `logs/` are excluded and handled separately.
      Do not start Stage 1 before this approval.

## Phase 1: Baseline (before any production change)

- [ ] T005 Run `dotnet test backend/InvoiceApp.sln`; record the real pass count in
      `specs/001-invoice-approval-workflow/mutation-scores.md` as the regression baseline.
- [ ] T006 Install/confirm Stryker.NET **4.13.0** and run it on `backend/src/InvoiceApp.Domain`;
      record the real **baseline** score (command and date) in `mutation-scores.md`. If it
      cannot run, record that and why — never estimate.
- [ ] T007 [P] Create empty `specs/001-invoice-approval-workflow/review-findings.md` with
      columns: ID, Stage, Finding, Severity, Disposition (Fixed / Dismissed+reason / Deferred+reason).

---

## STAGE 1 — Single-approver limits  (Approval Decision Table, standalone range)

**Behaviour**: ≤ 500,000 per role limit; > 500,000 rejected for every role (fail-closed, no
dual-approval wording); Reject open to all four roles at any amount.

### S1-A Domain foundation

- [x] T008 [P] [US1] Create `backend/src/InvoiceApp.Domain/InvoiceApprovalRecord.cs`:
      `public record InvoiceApprovalRecord(string Approver, string Role, DateTime ApprovedAt);`
- [x] T009 [P] [US1] In `Invoice.cs` add `Approvals` as a getter-only `IReadOnlyList<InvoiceApprovalRecord>`
      over a private list with `AddApproval(InvoiceApprovalRecord)`; no setter, no clear/replace
      (data-model invariant). Existing fields untouched.
- [x] T010 [P] [US4] Create `backend/tests/InvoiceApp.Tests/InvoiceSnapshot.cs`: a value
      snapshot (Status, DecidedBy, DecidedAt, DecisionNote, copy of Approvals) with an
      `AssertUnchanged(Invoice)` helper (needed because the fake repository shares references).

### S1-B Tests first (expected to fail until S1-C)

All in `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`, using `InvoiceSnapshot` for
every rejected-attempt case. Existing tests are not edited.

- [x] T011 [P] [US1] Manager approves exactly 50,000 → Approved, one `Approvals` record with
      approver and role "Manager".
- [x] T012 [P] [US1] Manager at 50,001 and at 500,000 → `ArgumentException` with the exact
      contract text `Manager approval limit is 50,000; invoice amount is {n}.`; snapshot unchanged.
- [x] T013 [P] [US2] Director, Approver and Admin each approve exactly 500,000 → Approved with
      one record (theory/data-driven).
- [x] T014 [P] [US2] Stage 1 interim:
      `Approve_AboveStandaloneCeiling_IsRejectedForEveryRole_Stage1Interim` — 500,000.01 for Manager,
      Director, Approver, Admin → `ArgumentException` (`{Role} approval limit is 500,000; …`),
      snapshot unchanged. (Replaced in Stage 2, see T045.)
- [x] T015 [P] [US4] Validation order and edge cases: non-pending invoice → `InvalidOperationException`
      (precedence over limits); unknown role → `ArgumentException` role-not-eligible; blank
      approver → `ArgumentException`; missing invoice → `KeyNotFoundException`; each leaves
      snapshot unchanged.
- [x] T016 [P] [US4] Legacy two-argument `Approve("INV-1", "demo.approver")` still passes the
      existing tests unmodified, and on a 500,000.01 invoice it is rejected (acts as role Approver).
- [x] T017 [P] [US4] `Reject` by Manager and Director on a 1,000,000 invoice succeeds (D4),
      records the rejecter and reason; blank reason still `ArgumentException` with snapshot unchanged.
- [x] T018 [P] [US4] Add `ProjectReference` to `InvoiceApp.Api` in
      `backend/tests/InvoiceApp.Tests/InvoiceApp.Tests.csproj` (no package). Verify the build.
- [x] T019 [P] [US4] `ApprovalsControllerTests.cs`: with a `DefaultHttpContext` session
      (username, role), `Approve` passes the **session** username and role to the domain; a
      forged `X-User`/body value is ignored (FR-007); the controller calls only the
      three-argument overload.
- [x] T020 [P] [US4] `AuthPipelineTests.cs`: `SessionAuthMiddleware` returns 401 without a valid
      token and does not call the next delegate; `RequireRoleAttribute` returns 403 for a role
      outside the list (e.g. a user with a non-listed role) and allows the four roles.

### S1-C Implementation (implementation-agent, after Gate 0)

- [x] T021 [US1] `ApprovalService.cs`: add `Approve(id, approver, role)`; make
      `Approve(id, approver)` delegate with `"Approver"`. Order: find (404) → `EnsurePending`
      (409) → approver/role present and eligible (400) → limit check (Manager 50,000; others
      500,000; > 500,000 rejected for all) → **only then** `AddApproval`, set Status, DecidedBy,
      DecidedAt, `Update`. Use named constants and the exact contract messages.
- [x] T022 [US1] `ApprovalsController.cs`: change the attribute to
      `[RequireRole("Approver", "Admin", "Manager", "Director")]` and pass
      `HttpContext.GetSession().Role` to the three-argument `Approve`. Registered in
      `Program.cs` unchanged. **After T021** so no account can approve before limits exist.
- [x] T023 [US1] `appsettings.json` `Users`: add `demo.manager`, `demo.director`,
      `demo.director2`. Demo-only credentials. `demo.director2` MUST NOT use `ChangeMe123!`:
      give it its own unique, clearly fake demo password (not a real, personal or reused
      password), keep it out of logs. Listing the new accounts in the docs that name the existing
      demo accounts (AGENTS.md) is **deferred**: AGENTS.md is handled separately by the owner.
- [x] T024 [US4] UI: in `wwwroot/app.js` / `index.html` show the `error` text from a failed
      approve/reject and refresh the invoice view without altering it. **Satisfied by the existing
      UI, no change made**: the `decision-error` alert already shows the server's `error` text and
      the E2E test relies on it.

### S1-D End-to-end (rejected over-limit approval)

- [x] T025 [US4] Create `e2e/pages/InvoiceApprovalPage.ts` (page object; `getByRole`/`getByLabel`
      only): sign in, open an invoice, approve, read status and rejection text.
- [x] T026 [US4] Create `e2e/tests/rejected-over-limit-approval.spec.ts`: arrange via API as
      `demo.admin` (create 75,000 invoice and submit), then as `demo.manager` through the UI
      approve it; assert the contract message is visible and the displayed status is still
      Pending. Confirm no seeded data is relied on.

### ⛔ Stage 1 gate (all in order, record each result truthfully)

- [x] T027 Run `dotnet test backend/InvoiceApp.sln`: full suite green, count ≥ T005 baseline.
- [x] T028 Run Stryker.NET 4.13.0 on Domain; record the real **Stage 1** score and surviving
      mutants (limit comparisons `<=`/`<`, role lookup) in `mutation-scores.md`; add tests for
      survivors that matter.
- [x] T029 Run `cd e2e && npm install && npx playwright install chromium && npm test`: green.
- [x] T030 testing-agent (or human) reviews Stage 1 tests: every test asserts the business rule
      (boundaries, messages, snapshot), not just coverage.
- [x] T031 review-agent independent review of the Stage 1 diff (correctness, security, regression);
      log every finding in `review-findings.md` as Fixed / Dismissed (reason) / Deferred (reason).
- [x] T032 [P] Update `logs/` with Stage 1 interactions (no secrets).
- [x] T033 ⛔ **Human approval**, then commit Stage 1 ("Stage 1: single-approver limits").

---

## STAGE 2 — Dual approval above 500,000  (start only after T033 and explicit approval)

- [x] T034 ⛔ Human approves starting Stage 2.

### S2-A Tests first (expected to fail until S2-B)

- [x] T035 [P] [US3] First approval on a 600,000 invoice by Manager, Director, Approver and Admin
      (each) → one record, `Status == PendingApproval`, `DecidedBy`/`DecidedAt` null.
- [x] T036 [P] [US3] Second approval by a different Director after a Manager first → Approved,
      two records, `DecidedBy` = second approver, `Approvals[1].Role == "Director"`.
- [x] T037 [P] [US3] Director then a different Director → Approved (D2).
- [x] T038 [P] [US3] Same user twice (also with different case, and with role Manager) →
      `ArgumentException` `{username} has already approved…`, snapshot unchanged (checked before
      the role rule).
- [x] T039 [P] [US3] Different Manager, Approver or Admin as second approver →
      `ArgumentException` `The second approver … must be a Director.`, snapshot unchanged.
- [x] T040 [P] [US3] Exactly 500,000 is still standalone for Director/Approver/Admin (one
      approval) and 500,000.01 enters dual approval (boundary pair).
- [x] T041 [P] [US4] After a first approval, Manager rejects with reason → Rejected, `Approvals`
      keeps the first record; third approval attempt on an Approved dual invoice →
      `InvalidOperationException`, snapshot unchanged.
- [x] T042 [P] [US4] `RejectedApprovalAttempt_NeverMutatesInvoiceState`: data-driven over every
      rejection category (over-limit, wrong second role, same user, non-pending, unknown role,
      blank approver) using snapshots.

### S2-B Implementation

- [x] T043 [US3] `ApprovalService.Approve(id, approver, role)`: replace the fail-closed branch
      with the Decision Table dual flow (`Approvals.Count` 0 → first approval, stays pending;
      1 → same-user check first, then Director check, then second record and Approved;
      DecidedBy/At = second approver). All checks before any mutation.
- [x] T044 [US3] UI: show "Second approval by a Director required" and the recorded approvers for
      a pending invoice with one approval (`wwwroot/app.js`, `index.html`).
- [x] T045 [US3] Replace the two Stage 1 interim tests
      `Approve_AboveStandaloneCeiling_IsRejectedForEveryRole_Stage1Interim` (T014) and
      `Approve_LegacyTwoArgumentForm_CannotBypassTheLimit_Stage1Interim` (T016) — they document
      fail-closed behaviour that this stage deliberately replaces. The first becomes
      `Approve_AboveStandaloneCeiling_NoRoleCanApproveAloneAndFirstApprovalIsOnlyRecorded`; the
      second is rewritten as `Approve_LegacyTwoArgumentForm_AboveThreshold_RecordsFirstApprovalAndStaysPending`
      (one legacy Approver call on 600,000 records the first approval, stays pending). Also remove
      the `Director`/500,001 row of `Approve_RejectedAttempt_NeverSavesTheInvoice` (now a valid
      first approval, not a rejected attempt). Note the replacements in `review-findings.md`. No
      pre-feature test is changed.
- [x] T046 [US3] `e2e/tests/dual-approval.spec.ts`: arrange a 600,000 pending invoice via API;
      `demo.manager` approves → pending notice shown; same user cannot approve again (reason
      shown); `demo.director` completes → Approved displayed. Extend the page object as needed.

### ⛔ Stage 2 gate

- [x] T047 Run `dotnet test backend/InvoiceApp.sln`: green, count ≥ Stage 1.
- [x] T048 Run Stryker.NET 4.13.0; record the real **final** score next to baseline and Stage 1;
      review survivors in duplicate-approver, second-role and boundary mutants; document any left.
- [x] T049 Run `cd e2e && npm test`: both specs green.
- [ ] T050 Test review of Stage 2 tests (meaningful assertions, no weakened tests).
- [x] T051 review-agent independent review of the Stage 2 diff; log all findings with
      dispositions; re-check constitution principles II–V.
- [ ] T052 Manually follow `quickstart.md` Stage 1 and Stage 2 sections (live run).
- [x] T053 [P] Update `logs/` with Stage 2 interactions (no secrets). (Recorded by the logging hook; see ASSIGNMENT.md.)
- [x] T054 ⛔ **Human approval**, then commit Stage 2 ("Stage 2: dual approval").

---

## Phase Final: Deliverables

- [x] T055 Write root `ASSIGNMENT.md`: rules implemented, decisions D1–D5, test and E2E results,
      baseline / Stage 1 / final mutation scores (copied from real output), review findings with
      dispositions, agents actually used, how to run the demo.
- [ ] T056 Live demonstration: run tests, Stryker or Playwright live (assignment §7).
- [ ] T057 ⛔ Human approval before committing `ASSIGNMENT.md`/logs, and before any push or PR.

---

## Dependencies & Order

- Gate 0 (T001–T004) → Baseline (T005–T007) → Stage 1 → Stage 1 gate → T034 → Stage 2 → Stage 2
  gate → Final.
- Within a stage: tests (S*-A/B) before implementation; T008/T009 before T021; T021 before
  T022/T023; T024 before T026; T025 before T026; T043 before T044/T046.
- Parallel: tasks marked `[P]` in different files or independent test methods; edits to
  `ApprovalServiceTests.cs` are done by one agent at a time.
- Stage 1 contains no dual-approval logic or wording; Stage 2 changes only `ApprovalService`
  dual branch, UI progress text, tests and a second E2E.

## Requirement → task coverage

| Requirement | Tasks |
|---|---|
| FR-001 | T011, T012, T021 |
| FR-002 | T013, T014, T021, T037, T040 |
| FR-003 | T035–T040, T043 |
| FR-004 | T015, T021, T043 |
| FR-005 | T012, T014, T038, T039 (exact messages) |
| FR-006 | T010, T012, T014, T015, T038, T039, T042 |
| FR-007 | T019, T020, T022 |
| FR-008 | T016, T019, T021 |
| FR-009 | T017, T041 |
| SC-001–SC-003 | T011–T017, T035–T042 |
| SC-004 | T019, T020 |
| SC-005 | T026, T046 |
| SC-006 | T005, T027, T047 |
| Assignment: E2E, Stryker, review, logs, ASSIGNMENT.md | T006, T026, T028, T031, T032, T048, T051, T053, T055 |
