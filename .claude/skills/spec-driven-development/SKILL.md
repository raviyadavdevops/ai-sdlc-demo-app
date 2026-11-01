---
name: spec-driven-development
description: Guide feature development using Spec Kit, from requirements analysis and constitution through specification, planning, task breakdown, and consistency analysis. Use before implementing new features.
---

# Spec-Driven Development

## Objective

Ensure feature implementation follows a structured, approved, specification-first workflow using Spec Kit.

## Mandatory Workflow

Follow these phases in order:

### Phase 1 — Understand the Existing Application

- Inspect the repository structure and relevant source files.
- Read `AGENTS.md` and applicable project instructions.
- Identify existing architecture, domain models, services, APIs, authentication and tests.
- Identify existing conventions and constraints.
- Do not modify application code during discovery.

### Phase 2 — Requirements Analysis

Delegate requirements analysis to `specification-agent`.

- Extract functional and non-functional requirements.
- Identify business rules, actors, permissions and constraints.
- Identify boundary conditions and negative scenarios.
- Identify ambiguous or conflicting requirements.
- Separate explicit requirements from assumptions.
- Present unresolved questions for human approval.

### Phase 3 — Constitution

Use the installed Spec Kit constitution workflow.

Ensure the constitution reflects:
- Project architecture and layering.
- Business logic ownership.
- Testing expectations.
- Security and authorization.
- Code quality and maintainability.
- Human approval requirements.

Obtain user approval before finalising it.

### Phase 4 — Feature Specification

Use the installed Spec Kit specification workflow.

The specification must include:
- Problem statement.
- User stories.
- Functional requirements.
- Non-functional requirements.
- Acceptance criteria.
- Edge cases and negative scenarios.
- Dependencies and assumptions.

Acceptance criteria must be testable and unambiguous.

### Phase 5 — Planning

Use the installed Spec Kit planning workflow.

The plan must describe:
- Technical approach.
- Components affected.
- Architecture decisions.
- Data model changes, if needed.
- API and authorization changes.
- Testing strategy.
- Risks and mitigations.

Preserve existing architecture unless a justified change is approved.

### Phase 6 — Task Breakdown

Use the installed Spec Kit task-generation workflow.

- Break implementation into small, independently verifiable tasks.
- Identify dependencies between tasks.
- Separate implementation and testing activities.
- Keep implementation stages independently reviewable.
- Identify tasks requiring human decisions.

### Phase 7 — Consistency Analysis

Run the installed Spec Kit analysis workflow.

Check for:
- Missing requirements.
- Contradictions.
- Uncovered acceptance criteria.
- Architecture inconsistencies.
- Missing security considerations.
- Missing negative tests.
- Unclear implementation tasks.

Resolve material issues before implementation.

## Assignment-Specific Rules

For the invoice approval assignment:

1. Analyse the existing Approver/Admin role model before proposing Manager/Director changes.
2. Explicitly document role mapping assumptions.
3. Define approval limits and boundary conditions.
4. Define the two-approver requirement for invoices above 500,000.
5. Enforce distinct approver identities.
6. Require the second approver to be a Director.
7. Treat rejection without invoice or approval-history mutation as a critical invariant.

Keep single-approver implementation and two-approver implementation as separate stages.

## Governance

- Do not implement application code while producing specifications.
- Do not silently resolve ambiguous requirements.
- Do not skip Spec Kit phases.
- Do not claim a Spec Kit command succeeded without verifying its output.
- Inspect the installed Spec Kit tooling and use its actual supported commands.
- Do not invent command names or assume a particular CLI interface.
- Ask for approval before finalising the constitution, specification, plan and tasks.
- Do not commit or push without explicit user approval.

## Completion Report

At the end, report:

- Existing architecture reviewed.
- Requirements and assumptions identified.
- Spec Kit artifacts generated.
- Analysis findings and resolutions.
- Files created or modified.
- Outstanding questions.
- Whether the user has approved proceeding to implementation.