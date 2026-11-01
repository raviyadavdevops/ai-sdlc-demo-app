# Review Findings — Invoice Approval Workflow

Disposition values: **Fixed**, **Dismissed** (with reason), **Deferred** (with reason).

## Stage 1 — independent review (T031), 2026-11-01

Reviewer: the `engineering-toolkit:reviewer` agent, invoked read-only on the uncommitted Stage 1
diff. The project's own `review-agent` definition is not registered as an invocable agent type in
this session, so it was **not** used. The reviewer read the code and ran `dotnet test`
(58/58 at that time); it did **not** run Playwright, Stryker or the web server. No CRITICAL or HIGH findings.

| ID | Sev | Finding | Disposition |
|---|---|---|---|
| S1-F1 | MEDIUM | `Invoice.Approvals` is a plain `List<>` on instances shared by every request, and check-then-mutate in `Approve` is not atomic. Two simultaneous approvals could both pass `EnsurePending` and append two records; concurrent `Add` while `GET /api/invoices` serializes can throw. The race on `Status` pre-existed; the list adds a new hazard that Stage 2's same-user check relies on. | **Fixed** (owner decision 2026-11-01). Inspected the model first: `InMemoryInvoiceRepository` locks each call separately and returns the shared instance, and `ApprovalService` is registered Scoped, so a per-instance lock cannot work. Smallest safe change, Domain only: one process-wide `static` lock in `ApprovalService` around the whole find → check pending → validate → mutate → save section of `Approve` and `Reject` (so an approval and a rejection cannot both win), and `Invoice.Approvals` now returns a copy under a lock, so `GET /api/invoices` can never enumerate the live list while another request appends. No new persistence or services. Regression tests: `Approve_WhileAnotherApprovalIsBeingSaved_WaitsForItAndThenIsRefused` and `Reject_WhileAnApprovalIsBeingSaved_WaitsForItAndThenIsRefused` hold the first decision inside `Update` and require the second to wait — both **fail on the unfixed code in 3 of 3 runs** and pass after the fix (5 of 5 runs); `Invoice_Approvals_IsASnapshot…` guards the reader; `Approve_ByManyUsersAtOnce…_StressCheck` is a probabilistic smoke test only (it passed on unfixed code in some runs, so it is not relied on). Known limit: the lock covers one process, which matches the in-memory single-process design; a shared store would need its own concurrency control. |
| S1-F2 | LOW | `FormatAmount` (`#,##0.##`) drops digits past two decimals; `InvoicesController.Create` accepts e.g. 50000.001, so a Manager would see "limit is 50,000; invoice amount is 50,000." The comparison itself is exact. | **Fixed** (code and contract; wording approved by the owner 2026-11-01). Checked first: the spec, the contract and `InvoicesController.Create` do not prohibit more than two decimals (Create only requires `Amount > 0`; `step 0.01` is a client-side form hint), so the defect is reachable. `FormatAmount` now uses `#,##0.####…` (28 optional decimals): output is identical for every amount with at most two decimals (all documented examples: 50,000; 50,001; 500,000; 500,000.01; 1,000,000) and no longer rounds away real digits (50000.001 → `50,000.001`). The contract sentence now reads: "Amount `{n}` is formatted with InvariantCulture and prints every significant decimal digit" (with examples), replacing the old `#,##0.##` wording. Tests: `Approve_ManagerJustAboveLimitByAFraction_ShowsTheRealAmountInTheReason` (50000.001 and 50000.0001); both failed before the fix. |
| S1-F3 | LOW | `AddApproval` and status writes occur before `_repository.Update`; a repository that throws would leave the shared instance mutated. | **Dismissed** for the demo: the in-memory repository's `Update` cannot fail, and all validation precedes any write (FR-006 holds). Revisit if a persistent store is ever added. |
| S1-F4 | LOW | `Invoice.AddApproval` is public, so other code could append without limit checks. | **Dismissed**: only `ApprovalService` calls it; making it `internal` needs `InternalsVisibleTo` for tests, which adds coupling for little gain. |
| S1-F5 | LOW | No unit test proved `approvals` appears in the API JSON. | **Fixed**: `Invoice_SerializesItsApprovalHistoryForTheApiResponse` added (also asserted end-to-end by the Playwright test, which passed). |
| S1-F6 | LOW | (a) `DecidedAt` vs approval timestamp / UTC never asserted; (b) redundant `role` column in `ManagerOverLimit`; (c) the forged-identity controller test is close to vacuous. | (a) **Fixed**: `Approve_RecordsTheSameUtcTimestampInTheApprovalAndTheDecision`. (b) **Dismissed**: harmless, keeps the data table self-describing. (c) **Dismissed**: kept as executable documentation of FR-007; the 75,000 test supplies the mutation-killing value. |
| S1-F7 | LOW | Seed invoice `INV-1003` is Approved with `DecidedBy` set but empty `Approvals`. | **Dismissed**: pre-existing seed data; no Stage 1 rule reads it. Note for Stage 2 if "last approval" is ever derived from `Approvals`. |
| S1-F8 | LOW | `demo.director2` uses its own fake demo password; `demo.manager`/`demo.director` reuse `ChangeMe123!`; `director2` is unused until Stage 2. | **Dismissed**: matches the approved decision (director2 has its own unique fake credential; others follow the documented demo pattern). Reviewer found no real or reused secrets. |

### Interim-test replacements planned for Stage 2 (T045)
`Approve_AboveStandaloneCeiling_IsRejectedForEveryRole_Stage1Interim` and
`Approve_LegacyTwoArgumentForm_CannotBypassTheLimit_Stage1Interim` document Stage 1's fail-closed
behaviour above 500,000 and are replaced on purpose in Stage 2.

### Interim-test replacements carried out in Stage 2 (T045)
Test maintenance after T043 (dual approval). These are tests written for this feature, not
pre-feature tests; none was weakened to pass.

| Test (before) | Replaced by | Why |
|---|---|---|
| `Approve_AboveStandaloneCeiling_IsRejectedForEveryRole_Stage1Interim` (6 rows: every role above 500,000 rejected with the limit message) | `Approve_AboveStandaloneCeiling_NoRoleCanApproveAloneAndFirstApprovalIsOnlyRecorded` (same 6 role/amount rows) | The fail-closed branch no longer exists. The same rows now assert the Stage 2 rule: a single approval by any role records one approval and the invoice stays `PendingApproval` with `DecidedBy`/`DecidedAt` unset. Rejection messages and snapshot integrity are covered by T038, T039 and `RejectedApprovalAttempt_NeverMutatesInvoiceState`. |
| `Approve_LegacyTwoArgumentForm_CannotBypassTheLimit_Stage1Interim` | Rewritten in place as `Approve_LegacyTwoArgumentForm_AboveThreshold_RecordsFirstApprovalAndStaysPending` (the duplicate added in T035–T042 was removed, so only one copy exists) | The security property is kept: the two-argument form acts as role `Approver`, so one call on a 600,000 invoice records the first approval and cannot approve the invoice. |
| `Approve_RejectedAttempt_NeverSavesTheInvoice` row `Director`/500,001 | Row removed; `Manager`/50,001 and `Intern`/100 kept | A Director approving 500,001 is now a valid first approval (and is saved), not a rejected attempt. Never-saves-on-rejection stays covered by the two remaining rows and T042. |

Not changed by T045: all Stage 1 tests at and below 500,000 (boundaries 50,000 / 50,001 /
500,000, role limits, legacy form at the ceiling, `Reject` at any amount).

## Stage 1 — independent review, round 2 (T030), 2026-11-01

Reviewer: a fresh `engineering-toolkit:reviewer` agent (read-only), run on the complete latest
Stage 1 diff including the decision lock, history snapshot, deterministic concurrency tests,
decimal formatting and contract sentence. The project's `review-agent` is still not an invocable
agent type here, so it was not used. The reviewer read the code and ran `dotnet test` once
(66/66); it did **not** run Stryker, Playwright or the server, and it did not run the suite with
the lock removed. No CRITICAL or HIGH findings.

| ID | Sev | Finding | Disposition |
|---|---|---|---|
| S1R2-1 | MEDIUM (docs) | The S1-F2 row said the contract was not edited and was awaiting approval, but the contract had already been updated. | **Fixed**: row S1-F2 corrected above. The owner approved the clarification on 2026-11-01. |
| S1R2-2 | LOW | The two `*_WhileAn…IsBeingSaved_…` tests released the blocked repository only on the success path; a failing assertion would leave the first decision parked in `Update` for up to 10 s, holding the static lock and slowing other tests. | **Fixed**: bodies wrapped in `try/finally` that always sets `Release`. |
| S1R2-3 | LOW | Test hygiene: `Capture` helper sat between tests with no blank line, the stress test duplicated its try/catch, and fully-qualified `System.*` names were used inline. | **Fixed**: helper moved to the end of the class and reused by the stress test; `using System.Globalization;` and `using System.Text.Json;` added. |
| S1R2-4 | LOW | `RequireRoleAttribute`'s no-session 403 branch had no test. | **Fixed**: `RequireRole_OnApprovalsController_ForbidsARequestWithNoSession` added. |
| S1R2-5 | LOW | `GET /api/invoices` reads the shared invoice without the lock, so a concurrent GET can serialize a half-applied decision for a moment (e.g. one approval recorded while status is still Pending). | **Dismissed** with a code comment: nothing wrong is persisted, state settles when `Approve` returns, and snapshotting inside the lock is out of proportion for the demo. |
| S1R2-6 | LOW | `DELETE /api/invoices/{id}` is not under the lock; an Admin delete between find and save makes `Approve` return 200 for an invoice no longer stored. | **Dismissed** (pre-existing repository behaviour, very narrow window); the lock comment now says decisions are serialised with each other, not with Submit, delete or unlocked reads. |
| S1R2-7 | LOW | `AGENTS.md` is modified wholesale and does not list the new demo accounts; T023 asked for them to be listed in the docs. | **Deferred**: `AGENTS.md` is excluded from this work and handled separately by the owner; T023 now says so. The new accounts exist only in `appsettings.json`. |
| S1R2-8 | LOW | T024 was ticked although `wwwroot` is unchanged. | **Fixed**: T024 now says it was satisfied by the existing UI, no change made. |

Reviewer verdicts (all OK by reading code and tests): decision lock and lock ordering (no
deadlock: `DecisionLock` → repository lock → `Invoice._approvals`); approval-history snapshot;
the two deterministic tests fail without the lock and cannot fail from timing with it;
`FormatAmount` (500000.00m → "500,000", 50000.10m → "50,000.1"); role and session handling;
no mutation before any throw; fail-closed above 500,000 with no Stage 2 logic. The previous
round's claim that the new tests fail on the unfixed code (3 of 3 runs) was observed by me when
the tests were written; the round-2 reviewer confirmed it only by reasoning.

## Stage 2 — independent review (T051), 2026-11-01

Reviewer: the `engineering-toolkit:reviewer` agent (read-only), run on the uncommitted Stage 2
diff. The project's own `review-agent` is not an invocable agent type in this session, so it was
**not** used. The reviewer ran `dotnet test` once (102/102 at that time). It did **not** run
Playwright or Stryker, did not read `ApprovalServiceTests.cs` in detail, `styles.css`,
`contracts/approvals-api.md`, or the session/`RequireRole` internals. No CRITICAL, HIGH or MEDIUM
findings. Checked and found correct: dual-approval rules and check order (same-user before
Director), case-insensitive duplicate check against every record, no mutation before any throw,
`DecisionLock` preserved, Reject after a first approval, session-derived identity, UI builds
nodes with text content (no `innerHTML`), E2E assertions.

| ID | Sev | Finding | Disposition |
|---|---|---|---|
| S2-F1 | LOW | `InvoiceApprovalPage.pendingItem` filters list items by substring of the invoice id, so ids that prefix each other (e.g. `INV-1` / `INV-10`) could match several items. | **Dismissed**: ids in this app are fixed-width (`INV-1001`…); revisit if the id format changes. |
| S2-F2 | LOW | The `ApprovalService` class summary still said an invoice is decided "by a single approver". | **Fixed**: comment now describes the single-approver range and the dual-approval range. |
| S2-M1 | — | Mutation survivor L114: first-approval save not asserted (test gap). | **Fixed**: `Approve_FirstApprovalAboveStandaloneCeiling_IsSavedToTheRepository`. |
| S2-M2 | — | Mutation survivor L89: `Any`→`All` in duplicate check (test gap in defensive code). | **Fixed**: `Approve_UserMatchingAnyRecordedApprover_IsRejectedEvenWithMoreThanOneRecord`. |
| S2-M3 | — | L82 `isSecondApproval = false` → `true`. | **Dismissed**: equivalent mutant, the initial value is never read. |
| S2-M4 | — | L47, L51 (`Submit`), `Invoice.cs` L7/L8 survivors. | **Deferred**: pre-existing code outside this feature. |
| S2-M5 | — | `OrdinalIgnoreCase` and final-approval assignments have no Stryker mutant. | **Deferred**: covered by test assertions only; not mutation-verified. |

Limitation: the reviewer did not read the unit tests in detail, so "test review" (T050) rests on
the mutation analysis and the author's own checks, not an independent test review.
