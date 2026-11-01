---
name: playwright-e2e
description: Create, execute and troubleshoot Playwright end-to-end tests for the invoice approval application's web UI. Use when validating user workflows through the browser.
---

# Playwright E2E Testing

## Project Context

- E2E project: `e2e/`
- Tests: `e2e/tests/`
- Page objects: `e2e/pages/`
- Configuration: `e2e/playwright.config.ts`
- Test runner: Playwright Test.

## Conventions

- Use page objects for reusable UI interactions.
- Use accessible locators: `getByRole` and `getByLabel`.
- Do not use CSS or XPath selectors.
- Keep tests independent and deterministic.
- Avoid arbitrary timeouts and unnecessary waits.
- Use existing application login and demo accounts.
- Do not hardcode real credentials or secrets.

## Workflow

1. Inspect the existing Playwright configuration and application UI.
2. Review existing page objects and tests.
3. Identify the user workflow and expected outcome.
4. Implement or update page objects.
5. Write focused E2E tests.
6. Execute the tests.
7. Investigate failures using browser output and traces.
8. Report results and remaining issues.

## Invoice Approval Assignment

Add an E2E scenario covering a rejected over-limit approval attempt.

Verify:
- The user attempts an approval beyond their permitted limit.
- The application displays a clear rejection reason.
- The invoice remains unchanged after rejection.

Use the actual application's authentication, UI and API flow. Do not mock the behaviour being tested.

## Commands

Run from `e2e/`:

`npm install`

`npx playwright install chromium`

`npm test`

## Reporting

Report:
- Test scenarios added.
- Files created or modified.
- Commands executed.
- Passed and failed tests.
- Relevant errors or traces.
- Outstanding issues.

Do not modify production code unless explicitly delegated.

Do not commit or push without explicit user approval.