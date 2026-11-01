# Invoice Approval Workflow Constitution

## Core Principles

### I. Domain Ownership of Approval Decisions
All invoice approval business rules — eligibility, approval limits, dual-approval
requirements, and rejection logic — MUST live in `backend/src/InvoiceApp.Domain`
(e.g. `ApprovalService`). API controllers under `backend/src/InvoiceApp.Api/Controllers`
MUST be limited to HTTP concerns: routing, request/response mapping, and invoking
authentication/authorization already established by `RequireRoleAttribute` and the
session middleware. Controllers MUST NOT contain approval-limit checks, dual-approval
logic, or any other business decision.
Rationale: `AGENTS.md` already establishes "business rules go in the Domain project,
never in controllers"; this feature extends that boundary rather than replacing it.

### II. Single-Approver Limits Are Non-Negotiable
A Manager MUST be able to approve invoices up to and including 50,000. A Director MUST
be able to approve invoices up to and including 500,000. The existing Approver and Admin
roles keep standalone approval permission up to and including 500,000; above 500,000 they
follow Principle III like every other role. An approver MUST NOT be able to independently
approve an invoice whose amount exceeds their individual standalone approval limit. For
invoices exceeding 500,000, approvals may only be recorded as part of the mandatory
dual-approval workflow defined in Principle III. The first approval does not independently
approve the invoice. These rules apply regardless of UI state or client-supplied data.
Limit checks MUST be evaluated against the authenticated user's
actual role and the invoice's actual amount at the time of decision.
Rationale: These thresholds are the core business constraint this feature exists to
enforce; any gap here is a direct financial-control failure.

### III. Dual Approval Above the Director Limit
Invoices exceeding 500,000 MUST require two successful approvals from two distinct users,
irrespective of role, before the invoice is Approved. The first approval MAY come from a
Manager, Director, Approver or Admin, subject to authenticated authorization. The second
approval MUST come from a different user whose role is Director. No role may bypass the
dual-approval requirement. A Director may provide the first approval, and a different
Director may provide the second approval. Both approvals must be associated with distinct
authenticated users. Distinctness MUST be checked on the authenticated username,
case-insensitively, and the
system MUST reject an attempt by the same user to provide both approvals on one invoice.
After the first approval the invoice MUST remain pending, the approval MUST be recorded in
the approval history, and the final decider and decision time MUST stay unset until the
second approval completes the invoice. Manager and Director may reject an invoice of any
amount; rejection is not subject to approval limits.
Rationale: Dual control above the highest individual limit is the explicit control this
feature introduces; weakening the distinct-user check or exempting any role would defeat its
purpose entirely.

### IV. Rejection and State Integrity
Every approval attempt MUST be fully validated — role eligibility, amount limit,
dual-approval sequencing, and distinct-approver identity — before any state mutation is
applied. When an attempt is rejected for any reason, the system MUST return a clear,
actionable rejection reason, and the invoice's status, approver history, decision
timestamp, and decision note MUST remain exactly as they were before the attempt. No
partial approval, partial approver assignment, or partial audit mutation is permitted
under any circumstance.
Rationale: An approval workflow that mutates state on a failed/rejected attempt cannot be
trusted for financial control; validate-then-mutate is the only acceptable sequencing.

### V. Server-Side Authorization, Never Client-Trusted
Only an authenticated user whose session carries an eligible role may approve or reject an
invoice. Role and approval-limit enforcement MUST happen server-side, derived from the
authenticated session (as established by the existing session middleware and
`RequireRoleAttribute`), never from a client-supplied role, limit, or approver-identity
field. The approver's identity for an approval or rejection MUST be read from the
authenticated session, not from the request body. The system MUST NOT allow a direct API
call to bypass an approval limit or the dual-approval requirement. Error responses and logs
MUST NOT leak sensitive information (credentials, session tokens, or unrelated customer
data).
Rationale: `AGENTS.md` already requires every endpoint to sit behind sign-in with
role-based restriction; this principle extends that model to the new approval rules rather
than introducing a parallel authorization path.

### VI. Testing Discipline
Every business rule introduced by this feature MUST have corresponding automated tests
before it is considered complete. Tests MUST use the existing xUnit framework and
conventions (`<ClassUnderTest>Tests`, `FakeInvoiceRepository`, or an equivalent new fake
following the same pattern). Required coverage includes, at minimum: Manager and Director
limits tested separately; boundary values at exactly 50,000 and exactly 500,000; the
dual-approval requirement and the second-approver-must-be-Director rule; rejection of a
same-user dual-approval attempt; unauthorized and over-limit attempts; and explicit
assertions that a rejected attempt leaves invoice state and approval history unchanged.
Existing tests MUST NOT be weakened, deleted, or bypassed to make an implementation pass.
At least one Playwright end-to-end test, built with page objects and accessible locators
(`getByRole`, `getByLabel`) per existing convention, MUST cover a rejected over-limit
approval through the actual UI.
Rationale: Given the financial-control nature of this feature, test coverage is the
primary evidence that the non-negotiable rules in Principles II–IV actually hold.

### VII. Minimal, Compatible Change
This feature extends an existing application; it is not a rewrite. Changes MUST stay
focused on the invoice approval workflow. The team MUST NOT introduce a database, external
service, new architectural layer, or dependency unless the existing architecture
genuinely cannot satisfy a stated requirement without one. Unrelated refactoring is
prohibited. Existing application behavior outside the approval workflow MUST be preserved,
and backward compatibility MUST be maintained unless an approved requirement explicitly
demands a breaking change.
Rationale: Scope creep in a financial-control feature increases risk without adding
control value; the existing Domain/API/Repository separation and DI patterns already fit
this feature's needs.

### VIII. Staged, Reviewable Delivery
Implementation MUST proceed in two separate stages, each completed and committed
independently: Stage 1 introduces and enforces the single-approver Manager/Director
limits with their unit tests; Stage 2 introduces the dual-approval requirement (distinct
approvers, second-approver-must-be-Director) with its unit tests. The two stages MUST NOT
be combined into a single implementation commit. Each stage MUST pass its own test run and
independent code review before the next stage begins.
Rationale: Staging isolates the simpler, higher-confidence rule (individual limits) from
the more complex control (dual approval with identity checks), keeping each change
independently verifiable and reviewable.

## Quality Gates & Verification

Every stage of this feature MUST pass the following gates before being considered done:

- Run `dotnet test backend/InvoiceApp.sln` and confirm all tests pass.
- Run mutation testing with Stryker.NET version 4.13.0 against the Domain project; record
  an actual baseline score captured before implementation and an actual final score
  captured after. Scores MUST be real tool output, never invented or estimated.
- Review surviving mutants, with particular attention to approval-limit boundaries,
  approver-identity checks, and rejected-state integrity; document any survivors left
  unresolved and why.
- Run the Playwright end-to-end suite (`cd e2e && npm test`) and confirm the rejected
  over-limit scenario passes.
- Review AI-generated tests for meaningful assertions on the actual business rule, not
  merely line or branch coverage.
- Complete an independent code review (via the `review-agent`, a human reviewer, or both)
  covering correctness, security, maintainability, and regression risk before any stage is
  merged.

## AI-Assisted Engineering & Collaboration

This repository uses Claude Code with specialist subagents (`specification-agent`,
`implementation-agent`, `testing-agent`, `review-agent`) and custom skills
(`spec-driven-development`, `dotnet-quality`, `playwright-e2e`), orchestrated through Spec
Kit as the source of truth for the feature specification, implementation plan, and task
breakdown. The following rules govern their use on this feature:

- Each specialist agent MUST be used only for its defined responsibility; implementation
  work MUST NOT begin before a specification and task breakdown have been produced and
  approved.
- `implementation-agent` MUST follow the approved specification and task breakdown; it
  MUST NOT invent business rules not present in the specification or this constitution.
- `review-agent` (or an equivalent independent review) MUST assess implementation output
  independently of the agent that produced it — passing existing tests is not sufficient
  grounds to dismiss a review finding.
- All AI-generated code, tests, and reported results (including mutation and test-run
  results) require human review before being accepted; AI tool output MUST NOT be presented
  as already-verified fact.
- No agent or workflow may commit, push, or open a pull request without explicit human
  approval at that specific step.
- Existing Claude Code hooks used for AI-interaction logging MUST be preserved and MUST NOT
  be disabled or bypassed by this feature's work.

## Governance

This constitution supersedes any conflicting ad hoc practice for the Invoice Approval
Workflow feature. `AGENTS.md` and the Spec Kit artifacts (`spec.md`, `plan.md`,
`tasks.md`) operate under it: where a conflict is found between this constitution and an
existing document, the conflict MUST be raised for human resolution rather than silently
decided by an agent.

**Amendment procedure**: Amendments are proposed via `/speckit-constitution`, re-running
the full template resolution and placeholder-derivation process. A proposed amendment
MUST state the version-bump rationale (MAJOR/MINOR/PATCH) and MUST be approved by a human
project owner before the amended file replaces the current one.

**Versioning policy**: Semantic versioning applies —
MAJOR for backward-incompatible principle removal or redefinition (e.g. changing an
approval limit value or removing the dual-approval requirement), MINOR for adding a new
principle or materially expanding guidance, PATCH for clarifications and non-semantic
wording fixes.

**Compliance review**: Every implementation stage (Principle VIII) and every pull request
touching the approval workflow MUST be checked against this constitution's principles,
particularly Principles II–V, as part of the independent code review required under
Quality Gates & Verification. Any deviation MUST be explicitly recorded as Fixed,
Dismissed (with reason), or Deferred (with reason) rather than silently merged.

**Version**: 1.1.0 | **Ratified**: 2026-10-30 | **Last Amended**: 2026-11-01
