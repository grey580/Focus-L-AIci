---
name: test-web-application-flows
description: Exercise a running web app like a user would so navigation, forms, console errors, screenshots, and regressions are checked in one pass.
category: Task
pinned: true
trigger_hints: webapp testing, playwright, ui flow, screenshots, console logs, regression
---

# Test web application flows

Exercise a running web app like a user would so navigation, forms, console errors, screenshots, and regressions are checked in one pass.

## When to Use This Skill

Use this after web changes, before shipping a UI fix, or whenever a user flow needs evidence instead of a code-only confidence check.

## Workflow Overview

1. Confirm the target app is running and accessible before testing interactions.
2. Walk the real user flow with browser automation or browser-like requests, not just unit assumptions.
3. Capture screenshots, console output, and broken steps when a flow fails so the fix has usable evidence.
4. Re-run the changed flow after fixes and note any remaining friction clearly.

## Examples

- Test the login or settings flow end to end after a UI change.
- Reproduce a reported browser issue and capture the evidence.
- Verify a web feature with real interactions before closing the work.
