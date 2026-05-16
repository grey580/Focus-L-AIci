---
name: run-security-review
description: Review code and configuration like a security researcher by tracing dangerous inputs, auth boundaries, dependency risk, secret exposure, and exploitable sinks.
category: System
pinned: true
trigger_hints: security review, auth, authorization, injection, xss, secrets, vulnerabilities
---

# Run security review

Review code and configuration like a security researcher by tracing dangerous inputs, auth boundaries, dependency risk, secret exposure, and exploitable sinks.

## When to Use This Skill

Use this when a change touches authentication, authorization, input handling, external process execution, secrets, or any path that could expose sensitive data or side effects.

## Workflow Overview

1. Define the scan scope and identify the runtime, frameworks, and trust boundaries involved.
2. Check dependencies and committed configuration for known security drift, leaked credentials, or weak defaults before reading business logic.
3. Trace user-controlled inputs toward sensitive sinks such as database queries, file writes, rendered output, or command execution.
4. Keep findings specific, severity-ranked, and tied to real exploitability instead of generic best-practice warnings.

## Examples

- Run a targeted security review on this auth and admin workflow.
- Check whether this API change introduces injection or access-control risk.
- Review this feature for secrets exposure, weak validation, or unsafe side effects.
