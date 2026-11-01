# AI in SDLC Assignment — Invoice Approval Workflow

ASP.NET Core 8 invoice approval demo, extended with role-based limits and dual approval, built
with Claude Code, Spec Kit and specialist agents. Everything below is taken from real runs in this
repository; nothing is estimated.

## 1. Objective and rules implemented

| Rule | Behaviour |
|---|---|
| Manager | approves up to and including 50,000 alone |
| Director, Approver, Admin | approve up to and including 500,000 alone |
| Above 500,000 | two successful approvals from two distinct users; first by any eligible role (Manager, Director, Approver, Admin); second by a **different Director**; no role bypasses this |
| Same user | detected on the authenticated username, case-insensitively, before the Director check |
| After first approval | invoice stays `PendingApproval`, approval recorded in history, `DecidedBy`/`DecidedAt` unset |
| Rejection | Manager/Director (and Approver/Admin) may reject any amount; a rejected approval attempt returns a clear reason and leaves status, approvals, `DecidedBy`, `DecidedAt` and note unchanged |

Decisions D1–D5 and R1–R5 (Manager first approval, sequencing, Approver/Admin under dual approval,
rejection, API shape) are recorded in `specs/001-invoice-approval-workflow/spec.md`.

## 2. Architecture

- `backend/src/InvoiceApp.Domain` — all business rules (`ApprovalService`, `Invoice`,
  `InvoiceApprovalRecord`). One process-wide `DecisionLock` makes check-then-mutate atomic.
- `backend/src/InvoiceApp.Api` — controllers (identity and role come only from the session),
  in-memory repository, static UI (`wwwroot`). Controllers contain no approval rules.
- `backend/tests/InvoiceApp.Tests` — xUnit, `FakeInvoiceRepository`, value snapshots
  (`InvoiceSnapshot`) for every "unchanged" assertion.
- `e2e/` — Playwright with page objects and accessible locators.
- No new database, service, package or architectural layer.

## 3. Spec Kit workflow and artefacts

Constitution (v1.1.0, `.specify/memory/constitution.md`) → specification → plan → tasks →
consistency analysis, under `specs/001-invoice-approval-workflow/`: `spec.md`, `plan.md`,
`research.md`, `data-model.md`, `quickstart.md`, `tasks.md`, `contracts/approvals-api.md`,
`checklists/requirements.md`, plus `mutation-scores.md` and `review-findings.md`.

## 4. AGENTS.md, skills, agents, hooks

- `AGENTS.md` — project conventions, delegation rules and the assignment brief.
- `.claude/skills` — `dotnet-quality`, `playwright-e2e`, `spec-driven-development` and the Spec Kit skills.
- `.claude/agents` — `specification-agent`, `implementation-agent`, `testing-agent`, `review-agent` definitions.
- `.claude/hooks` + `.claude/settings.json` — a logging hook records AI interaction events.

**Which agents were actually invoked (honest disclosure):**
- `testing-agent`: **not invocable** in the session (not a registered agent type). The Stage 2 unit
  tests (T035–T042) were written by a **general-purpose agent briefed with `testing-agent.md`**,
  not by the testing-agent itself.
- `review-agent`: **not invocable**. Independent reviews used the **`engineering-toolkit:reviewer`**
  agent (two rounds for Stage 1, one for Stage 2).
- `specification-agent` / `implementation-agent`: not invoked as agent types; the main session did that work.

## 5. Test-first, two-stage delivery

Commits: `e0e1a82` specification (Gate 0), `e43fb0b` Stage 1 (single-approver limits), and a
separate Stage 2 commit (dual approval; see `git log`). Stage 2 tests (T035–T042) were written and
confirmed failing against the Stage 1 code (26 failing for the expected reasons) before
`ApprovalService` was changed (T043). Stage 1 interim tests asserting fail-closed behaviour above
500,000 were replaced on purpose (T045, documented in `review-findings.md`).

## 6. Test coverage

- **Unit (104 tests):** role limits and boundaries (50,000/50,001, 500,000/500,000.01), unknown role,
  blank approver, non-pending precedence, legacy two-argument form acts as `Approver`, all Stage 2
  sequences (first approval by each role, Manager→Director, Director→Director, same user incl.
  different case, non-Director second approver), reject after first approval, third approval
  conflict, a data-driven "rejected attempt never mutates" test, concurrency tests for the lock,
  controller/session tests, `SessionAuthMiddleware` 401 and `RequireRole` 403.
- **Playwright (2 tests):** `rejected-over-limit-approval.spec.ts` (Stage 1) and
  `dual-approval.spec.ts` (Manager first approval → pending notice and approver shown → same user
  refused with the duplicate reason → different Director completes → Approved).

Final runs (2026-11-01): `dotnet test backend/InvoiceApp.sln` → **104 passed, 0 failed, 0 skipped**;
`cd e2e && npm test` → **2 passed**.

## 7. Mutation testing (Stryker.NET 4.13.0, `InvoiceApp.Domain`)

| Stage | Tests | Score | Killed / Survived / NoCoverage |
|---|---|---|---|
| Baseline | 7 | **30.77 %** | 12 / 10 / 17 |
| Stage 1 | 67 | **62.50 %** | 35 / 4 / 17 |
| Stage 2 (two identical runs) | 102 | **67.12 %** | 49 / 7 / 17 |
| Stage 2 after closing two test gaps (one run) | 104 | **69.86 %** | 51 / 5 / 17 |

## 8. Mutant breakdown and survivor analysis

Latest run: 98 mutants created; 51 killed, 5 survived, 17 no coverage, 22 ignored (block-already-covered
filter), 3 compile errors, 0 timeouts. The score is capped by `DiscountCalculator.cs` (17 no-coverage
mutants; unrelated, untested, untouched). `ApprovalService.cs` alone: 49 killed of 52 non-ignored
(derived 94.23 %).

Survivors (5): `ApprovalService.cs` L82 — equivalent mutant (initial value never read); L47, L51
(`Submit`) and `Invoice.cs` L7, L8 (default values) — pre-existing code, **Deferred**. Two earlier
survivors were real test gaps (first-approval save not asserted; `Any`→`All` in the duplicate check)
and were **closed** with two tests. Stryker generated **no mutant** for the `OrdinalIgnoreCase`
comparison or the final-approval assignments, so those rest on test assertions only. Details:
`specs/001-invoice-approval-workflow/mutation-scores.md`. Stage 1 had one load-induced run with 7
Timeouts (66.07 %); it was not used as the reported score.

## 9. Review findings and dispositions

Full tables in `specs/001-invoice-approval-workflow/review-findings.md`. No CRITICAL/HIGH findings in
any round. Stage 1: two review rounds (concurrency lock, decimal formatting and others → mostly
Fixed; the rest Dismissed/Deferred with reasons). Stage 2: 2 LOW (stale class comment → Fixed;
id-prefix locator → Dismissed, ids are fixed-width) plus the mutation findings above.

## 10. AI interaction logging

A Claude Code hook (`.claude/hooks/log-event.ps1`) appends one JSON line per event (session start,
prompt submit, tool use, stop) to `logs/ai-interactions.jsonl` with timestamp, session id, event,
agent and tool name only — no prompt text, tool input or secrets. A search of the file for
"password", "token" and "secret" found no matches. The hook records metadata, not content; what each
agent did is documented here and in the spec artefacts.

## 11. Run it

```powershell
dotnet run --project backend/src/InvoiceApp.Api          # http://localhost:5000
dotnet test backend/InvoiceApp.sln
cd e2e; npm install; npx playwright install chromium; npm test
cd backend/tests/InvoiceApp.Tests
dotnet tool run dotnet-stryker --project InvoiceApp.Domain.csproj --reporter html --reporter cleartext
```

Demo accounts (demo only): `demo.approver`, `demo.admin`, `demo.manager`, `demo.director` with
`ChangeMe123!`; `demo.director2` has its own fake password in `appsettings.json`.

## 12. Demo video

**PLACEHOLDER — not yet recorded or uploaded.** Link: _TBD_

## 13. Pull request

**PLACEHOLDER — no pull request created yet.** Link: _TBD_

## 14. Limitations and deferred work

- Demo video and PR link are outstanding (above).
- `quickstart.md` was not followed as a separate manual walkthrough (T052); its checks are covered by
  the automated unit and E2E tests. The live demonstration (T056) is still to be done.
- Stage 2 test review (T050) was not performed by an independent reviewer; the reviewer agent did not
  read the unit tests in detail.
- 69.86 % comes from one run; 67.12 % was reproduced twice.
- Deferred survivors in `Submit` and `Invoice` defaults; no mutants exist for the case-insensitive
  comparison or the final-approval assignments.
- The UI currency formatter is pre-existing (USD) although the spec treats amounts as INR.
- In-memory, single-process design: the decision lock does not serialise with `Submit`, delete or reads.
- `AGENTS.md` carries the owner's own uncommitted edits and may not be part of the submitted commits.
