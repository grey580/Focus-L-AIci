---
name: manage-secret-scanning
description: Set up or review secret scanning and push protection so exposed credentials are caught early and remediation stays operationally clear.
category: System
pinned: true
trigger_hints: secret scanning, push protection, leaked credentials, github security, secrets, remediation
---

# Manage secret scanning

Set up or review secret scanning and push protection so exposed credentials are caught early and remediation stays operationally clear.

## When to Use This Skill

Use this when a repo needs stronger secret hygiene, a blocked push must be understood, or credential-leak handling needs a repeatable workflow.

## Workflow Overview

1. Decide whether the task is repository setup, push-protection triage, custom pattern definition, or alert remediation.
2. Prefer preventing secrets from landing in history over documenting cleanup after the fact.
3. Review exclusions, bypasses, and custom patterns carefully so they narrow false positives without creating blind spots.
4. Tie secret scanning guidance back to real remediation steps such as rotation, removal, and alert follow-up.

## Examples

- Configure secret scanning and push protection for this repository.
- Review a blocked push and decide whether the right fix is removal, rotation, or a justified bypass.
- Tighten secret-scanning exclusions without creating blind spots.
