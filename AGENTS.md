# Project Instructions

ASP.NET Core 8 invoice approval demo application with a static web UI.

## 1. Project Structure

- `backend/src/InvoiceApp.Domain` – Business logic and invoice lifecycle (`ApprovalService`).
- `backend/src/InvoiceApp.Api` – API controllers, authentication, in-memory repository and `wwwroot` UI.
- `backend/tests/InvoiceApp.Tests` – xUnit tests.
- `e2e` – Playwright tests and page objects.
- `docs/use-cases` – Feature briefs.
- `.claude/agents` – Specialist agent definitions.
- `.claude/skills` – Reusable workflows.
- `.specify` – Spec Kit artifacts.
- `logs` – AI interaction records.

## 2. Existing Coding Conventions

- Business rules belong in Domain, never in controllers or `Program.cs`.
- Controllers reside in `Controllers/`, inherit `ApiControllerBase` and use `Handle(...)`.
- Routes follow `api/[controller]`.
- Request bodies use `record` types declared inside controllers.
- Every endpoint requires authentication. Use `[RequireRole("...")]` for authorization.
- Destructive actions are Admin-only; existing approvals allow Approver or Admin.
- Register services in `Program.cs`.
- Repositories are interfaces in Domain with implementations in Api.
- Use `FakeInvoiceRepository` for unit tests.
- Follow the existing xUnit naming convention: `<ClassUnderTest>Tests`.
- Use clear variable names; avoid one-letter variables and `flag` parameters.
- Playwright tests must use page objects and accessible locators (`getByRole`, `getByLabel`), not CSS or XPath selectors.

## 3. Development Commands

```powershell
dotnet run --project backend/src/InvoiceApp.Api
dotnet test backend/InvoiceApp.sln
```

```powershell
cd e2e
npm test
```

Application URL: `http://localhost:5000`

Demo accounts:
- `demo.approver` / `ChangeMe123!`
- `demo.admin` / `ChangeMe123!`

These are demo credentials only.

## 4. Specialist Agents

Use the existing project-level agents under `.claude/agents/`.

| Agent | Definition | Responsibility | Tools |
|---|---|---|---|
| specification-agent | `specification-agent.md` | Requirements, assumptions, specifications and acceptance criteria | Read, Grep, Glob |
| implementation-agent | `implementation-agent.md` | Approved Domain and API implementation | Read, Grep, Glob, Edit, Write, Bash |
| testing-agent | `testing-agent.md` | Unit tests, Playwright and mutation testing | Read, Grep, Glob, Edit, Write, Bash |
| review-agent | `review-agent.md` | Independent, read-only code and security review | Read, Grep, Glob, Bash |

**Delegation rules:**
- Specification agent before implementation.
- Implementation agent only after approval.
- Testing agent for independent test validation.
- Review agent after each implementation stage.
- Do not assign concurrent modifications to the same files.
- The main session coordinates agents and reviews their findings.
- Do not claim an agent was used unless it was actually invoked.

## 5. Invoice Approval Assignment

Implement these business rules:

- Manager: approval limit 50,000.
- Director: approval limit 500,000.
- Above 500,000: two distinct approvers required; second approver must be a Director.
- Rejected attempts must return a clear reason and leave invoice state unchanged.

**Critical invariants:** the same user cannot approve twice, and rejected operations must not mutate invoice state or approval history.

Before implementation:
- Inspect the existing Approver/Admin authentication model.
- Do not assume its mapping to Manager/Director.
- Resolve approval sequencing and other material ambiguities in the specification.

## 6. Required Workflow

Use Spec Kit for constitution, specification, plan, tasks and consistency analysis.

Implement in two separate stages:

1. Single-approver limits, with unit tests.
2. Two-approver rules, with unit tests.

For each stage:
- Implement only approved tasks.
- Run the complete unit test suite.
- Review tests and business-rule coverage.
- Obtain approval before committing.

Also:
- Add a Playwright E2E test for rejected over-limit approval.
- Run Stryker.NET 4.13.0 mutation testing.
- Record baseline and final mutation scores.
- Perform independent code review.
- Record every finding as Fixed, Dismissed (with reason) or Deferred (with reason).

## 7. Git, Logging and Deliverables

- Keep specification, Stage 1 and Stage 2 in separate commits.
- Never commit, push or rewrite Git history without explicit user approval.
- Preserve unrelated user changes.
- Maintain AI interaction records under `logs/`; never log secrets or customer data.
- Use Claude Code skills and hooks where they add practical value.
- Do not invent command results, test results or mutation scores.

Final repository deliverables:
- Spec Kit artifacts.
- Implementation and unit tests.
- Playwright E2E test.
- Mutation scores.
- Review findings and dispositions.
- AI interaction logs.
- Root-level `ASSIGNMENT.md`.

The demonstration must include live execution of tests, mutation testing or E2E testing.

## 8. Human Approval

Obtain explicit user approval before:
- Finalising specifications and implementation plans.
- Starting each implementation stage.
- Committing changes.
- Pushing or submitting the pull request.

Do not silently resolve material business-rule ambiguities.