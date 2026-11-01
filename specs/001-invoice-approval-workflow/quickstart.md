# Quickstart: Validating the Invoice Approval Workflow

This is a runnable validation guide, not an implementation guide — see `data-model.md` and
`contracts/approvals-api.md` for the design details it exercises.

## Prerequisites

- The Stage 1 and/or Stage 2 tasks in `tasks.md` are completed for whichever stage you are
  validating.
- Demo accounts for Manager and Director added to
  `backend/src/InvoiceApp.Api/appsettings.json` (parallel to the existing `demo.approver` /
  `demo.admin` entries) — exact credentials are an implementation detail, not specified here.

## Setup

```powershell
dotnet run --project backend/src/InvoiceApp.Api
```

Open `http://localhost:5000`.

Demo accounts to add (demo-only credentials; `demo.director2` has its own unique fake
password, not `ChangeMe123!`): `demo.manager`, `demo.director`, `demo.director2`. Create test invoices with `POST /api/invoices` and `.../submit`
(no seeded invoice exceeds 50,000).

## Stage 1 validation — single-approver limits

1. `dotnet test backend/InvoiceApp.sln` — existing tests plus new boundary tests pass.
2. Manager approves an invoice of exactly 50,000 → `200`, `status: "Approved"`.
3. Manager attempts 50,001 → `400` with `Manager approval limit is 50,000; invoice amount is
   50,001.`; re-fetch and compare every field with the snapshot taken before the attempt.
4. Manager attempts exactly 500,000 → `400`. Director, Approver and Admin each approve exactly
   500,000 → `200`.
5. Any role attempts 500,000.01 → `400` (interim fail-closed message). Manager rejects a
   1,000,000 invoice with a reason → `200`, `status: "Rejected"`.

## Stage 2 validation — dual approval

1. Run the unit tests, including the dual-approval cases.
2. Create a 600,000 invoice. As Manager, Director, Approver or Admin (one per fresh invoice)
   approve → `200`, `status` still `"PendingApproval"`, `decidedBy: null`, one `approvals` entry.
3. As a **different** Director approve the same invoice → `200`, `"Approved"`, two entries,
   `decidedBy` = second approver. Repeat with `demo.director` then `demo.director2`.
4. After a first approval, the **same** user approves again → `400` (`has already approved`);
   invoice still one record and pending.
5. After a first approval, a different Manager/Approver/Admin approves → `400` (`second
   approver ... must be a Director`); state unchanged.
6. After a first approval, a Manager rejects with a reason → `200`, `"Rejected"`, `approvals`
   still lists the first approval.

## End-to-end validation (Playwright)

```powershell
cd e2e
npm install
npx playwright install chromium
npm test
```

Stage 1: `e2e/tests/rejected-over-limit-approval.spec.ts` passes — a Manager approves an
invoice above 50,000 through the UI, the displayed reason matches the contract text, and the
invoice's displayed status is unchanged. Stage 2 adds `e2e/tests/dual-approval.spec.ts`.

## Mutation testing

```powershell
dotnet tool run dotnet-stryker --version 4.13.0 --project backend/src/InvoiceApp.Domain
```

(Exact invocation depends on how Stryker.NET is installed/configured in this repo — confirm
command against the installed tool before relying on this snippet. Record the actual baseline
and final mutation scores; do not estimate them.)
