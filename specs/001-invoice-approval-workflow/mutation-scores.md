# Baseline and Mutation Scores — Invoice Approval Workflow

Real tool output only (constitution: Quality Gates). Nothing here is estimated.

## Baseline (T005 + T006) — recorded 2026-11-01, before any Stage 1 code

Repository state: commit `e0e1a82` (Gate 0) plus uncommitted tooling; no application code or
existing test was changed.

### T005 — existing unit tests

Command (repo root):

```powershell
dotnet test backend/InvoiceApp.sln
```

Result: **Passed! Failed: 0, Passed: 7, Skipped: 0, Total: 7** (InvoiceApp.Tests.dll, net8.0).
.NET SDK 10.0.403 building `net8.0`; runtimes 8.0.33 and 10.0.14 installed.
This is the regression baseline: Stage 1 and Stage 2 must keep these 7 passing unmodified.

### T006 — Stryker.NET 4.13.0 baseline

Install (local tool manifest `dotnet-tools.json`, repo root):

```powershell
dotnet new tool-manifest
dotnet tool install dotnet-stryker --version 4.13.0
```

Run (from `backend/tests/InvoiceApp.Tests`, no Stryker config file exists in the repo, so
defaults apply; reports written outside the repo):

```powershell
dotnet tool run dotnet-stryker --project InvoiceApp.Domain.csproj --output <scratch>/stryker-baseline --reporter json --reporter html --reporter cleartext
```

Output: `Version: 4.13.0`, 7 tests found, 51 mutants created, 22 tested, 29 skipped
(17 NoCoverage, 12 Ignored by the "block already covered" filter), elapsed 45 s.

**Final mutation score (baseline): 30.77 %** (Stryker thresholds: high 80, low 60).

| File | Killed | Survived | NoCoverage | Ignored |
|---|---|---|---|---|
| `ApprovalService.cs` | 12 | 8 | 0 | 7 |
| `DiscountCalculator.cs` | 0 | 0 | 17 | 5 |
| `Invoice.cs` | 0 | 2 | 0 | 0 |
| `IInvoiceRepository.cs`, `InvoiceStatus.cs` | 0 | 0 | 0 | 0 |
| **Project total** | **12** | **10** | **17** | **12** |

Score arithmetic (Stryker's definition): killed ÷ (killed + survived + no-coverage) =
12 ÷ 39 = 30.77 %. Ignored mutants are excluded.

### Notes for later comparison

- The 30.77 % covers the whole `InvoiceApp.Domain` project, which includes
  `DiscountCalculator.cs` (volume/loyalty discounts, unrelated to this feature, with no
  tests). Its 17 uncovered mutants cap the project score. Not touched by this feature.
- For reference only: `ApprovalService.cs` alone was 12 ÷ (12 + 8) = 60.00 %. This per-file
  figure is derived from the report, not a Stryker headline number.
- Stage 1 and final scores will be measured with the same command and project scope. If the
  score is to be reported for the approval logic only, that must be agreed first (for
  example via `--mutate "**/ApprovalService.cs"`); the headline baseline stays 30.77 %.
- HTML and JSON reports live in the session scratchpad, not in the repository:
  `…/scratchpad/stryker-baseline/reports/mutation-report.{html,json}`.

## Stage 1 score (T028) — recorded 2026-11-01, uncommitted Stage 1 working tree

Same command and scope as the baseline (`InvoiceApp.Domain`, Stryker.NET 4.13.0, 69 mutants,
1 compile-error mutant excluded).

| Run | Tests | Score | Killed / Survived / NoCoverage |
|---|---|---|---|
| First Stage 1 run (feature tests only) | 49 | **50.00 %** | 27 / 10 / 17 |
| After strengthening tests for survivors | 58 | **61.11 %** | 33 / 4 / 17 |
| After review fixes (2 more tests) | 60 | **61.11 %** | 33 / 4 / 17 |
| After S1-F1 / S1-F2 fixes (decision lock, history snapshot, amount format) | 66 | **62.50 %** | 35 / 4 / 17 |
| Pre-commit run 1 (machine under heavy load, 7 Timeout classifications) | 67 | 66.07 % | 37 detected (30 Killed + 7 Timeout) / 2 / 17 |
| Pre-commit run 2 (same code, same command) | 67 | **62.50 %** | 35 / 4 / 17 |

**Reported Stage 1 result: 62.50 %** (35 ÷ (35 + 4 + 17) = 62.50 %, 74 mutants created), against
the **30.77 %** baseline (+31.73 percentage points). It was produced by two of the three runs on
the final-stage code (the 66-test run and pre-commit run 2); one run produced **66.07 %**. A single
stable score is **not** claimed — see the instability note below.

### Pre-commit verification (2026-11-01, final Stage 1 code, 67 tests, no code changes)

Exact command, run twice from `backend/tests/InvoiceApp.Tests`, identical to the baseline and
every earlier run (`--output` and the reporters are the only extras; there is no Stryker config
file in the repo):

```powershell
dotnet tool run dotnet-stryker --project InvoiceApp.Domain.csproj --output <scratchpad>/stryker-stage1-final --reporter json --reporter html --reporter cleartext
```

Key output (Stryker.NET `Version: 4.13.0`), both runs: `Number of tests found: 67`,
`74 mutants created`, `1 CompileError`, `17 NoCoverage`, `17 Ignored`, `39 total mutants will be
tested`.

- **Run 1:** `The final mutation score is 66.07 %` (37 ÷ 56). **7** mutants were classified
  **Timeout** (4 in `ApprovalService.cs`, 3 in `Invoice.cs`); Killed 30, Survived 2, NoCoverage 17.
  Stryker counts Timeout as detected. Compared with the earlier 62.50 % run, `Invoice.cs` L7 and
  L8 went from Survived to Timeout and `Invoice.cs` L24 from Killed to Timeout; the four
  `ApprovalService.cs` Timeouts (limit check, approver-name check, pending check and one more
  statement) were Killed before (33 Killed then, 29 now). The run took 2:55 (about 1:08 for
  earlier runs) and logged `Error while cancelling VsTest session`.
- **Run 2:** `The final mutation score is 62.50 %`. `ApprovalService.cs` Killed 33, Survived 2,
  Ignored 11, CompileError 1; `DiscountCalculator.cs` NoCoverage 17, Ignored 5; `Invoice.cs`
  Killed 2, Survived 2, Ignored 1. No Timeout mutants. Surviving mutants (4): `ApprovalService.cs`
  L47 (string in `Submit`'s error), L51 (`Submit`'s `_repository.Update` call), `Invoice.cs` L7 and
  L8 (`Id` / `CustomerName` defaults). The run took 16:46 and its analysis step alone about
  3½ minutes (normally about 9 seconds).

**Instability and classification uncertainty.** Both pre-commit runs were slow, so the machine was
under heavy load, but the cause of run 1's Timeouts was **not** diagnosed. The likely explanation
is that load pushed some mutant test runs past Stryker's per-mutant time limit, and the
concurrency regression tests make failing runs slow (they wait up to 300 ms, and up to 5–10 s on
failure). That is an inference, not a verified cause. What is established: the same code and
command produced 66.07 % once and 62.50 % once; Timeout counts as detected, so 66.07 % may
overstate test strength; 62.50 % (no Timeouts) was reproduced and is the more conservative figure.
Treat the score as 62.50 % with a known sensitivity to machine load, and re-measure on an idle
machine before quoting a final number in `ASSIGNMENT.md`.

Reports (not in the repo): `…/scratchpad/stryker-stage1-final/` (run 1) and
`…/scratchpad/stryker-stage1-final2/` (run 2), each with `reports/mutation-report.{json,html}`.
Per file (earlier 66-test run, unchanged in run 2): `ApprovalService.cs` killed 33, survived 2, ignored 11, compile-error 1
(derived 33 ÷ 35 = 94.29 %); `DiscountCalculator.cs` 17 no-coverage, 5 ignored (unchanged,
out of scope); `Invoice.cs` killed 2, survived 2, ignored 1 (derived 2 ÷ 4 = 50.00 %);
`InvoiceApprovalRecord.cs` no mutants. The two new `Invoice.cs` kills come from the
approval-history snapshot and lock code.

Survivors of the first run that were killed by new tests: `Approve` no longer saving the
invoice (`_repository.Update` removal), `Reject` skipping the non-pending check, and the
unasserted messages for blank reason, missing invoice and non-pending invoice (recording
repository and message assertions added).

Remaining survivors (4), all in code this feature does not change — **Deferred** (reason:
outside Stage 1 scope, unrelated to the approval rules):
- `ApprovalService.cs` L47 (was L36 before lock/comment lines were added) — message text of
  `Submit`'s "only drafts can be submitted" error.
- `ApprovalService.cs` L51 (was L40) — `Submit`'s `_repository.Update(invoice)` call.
- `Invoice.cs` L7, L8 — default values (`string.Empty`) of `Id` and `CustomerName`.

The limit comparisons (`>` against 50,000 and 500,000), the role lookup and the
blank-approver check had no survivors in the final run.

## Final score
Not yet recorded.
