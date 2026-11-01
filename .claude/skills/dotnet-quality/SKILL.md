---
name: dotnet-quality
description: Implement and validate .NET application changes using existing architecture, xUnit tests, and Stryker.NET mutation testing. Use when modifying or testing the ASP.NET Core application.
---

# .NET Quality Workflow

## Project Context

- Target framework: .NET 8.
- Solution: `backend/InvoiceApp.sln`.
- Domain: `backend/src/InvoiceApp.Domain`.
- API: `backend/src/InvoiceApp.Api`.
- Tests: `backend/tests/InvoiceApp.Tests`.
- Test framework: xUnit.
- Existing test repository: `FakeInvoiceRepository`.

## Implementation Rules

- Inspect existing classes and tests before modifying code.
- Keep business logic in Domain.
- Keep controllers focused on HTTP concerns.
- Follow existing dependency injection and repository patterns.
- Avoid unrelated refactoring and unnecessary dependencies.
- Preserve existing behaviour unless the approved requirement changes it.

## Testing Workflow

1. Identify affected classes and existing tests.
2. Add focused tests for new behaviour.
3. Cover positive, negative and boundary cases.
4. Verify rejected operations do not mutate invoice state or approval history.
5. Run the complete solution test suite:

   `dotnet test backend/InvoiceApp.sln`

6. Review failures rather than weakening assertions or removing tests.

## Mutation Testing

Use Stryker.NET version 4.13.0 as required by the assignment.

- Record the baseline mutation score before implementation.
- Run mutation testing after implementation.
- Record the actual final mutation score.
- Investigate surviving mutants relevant to approval rules.
- Never invent or estimate scores.

## Assignment-Specific Checks

- Manager limit: 50,000.
- Director limit: 500,000.
- Above 500,000: two distinct approvers.
- Second approver must be a Director.
- Same user cannot approve twice.
- Rejected attempts must preserve invoice state and approval history.

Keep single-approver and two-approver implementation stages separate.

## Reporting

Report changed files, implementation decisions, commands executed, test results, mutation score and unresolved issues.

Do not commit or push without explicit user approval.