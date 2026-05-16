---
name: review-endpoint-uninstall-and-recovery-flow
description: Follow the Grey Canary endpoint removal workflow carefully so uninstall, recovery, and final cleanup stay aligned with prior decisions.
category: Product
pinned: true
wing: grey-canary
trigger_hints: grey-canary, endpoints, uninstall, recovery, removal
---

# Review endpoint uninstall and recovery flow

Follow the Grey Canary endpoint removal workflow carefully so uninstall, recovery, and final cleanup stay aligned with prior decisions.

## When to Use This Skill

Use this when touching endpoint removal, uninstall jobs, hidden endpoints, recovery UX, or final-removal behavior.

## Workflow Overview

1. Search Grey Canary memories for uninstall, endpoint removal, recovery, and manual uninstall behavior.
2. Inspect active tickets or recent changes before editing endpoint state transitions.
3. Check whether the current flow expects a queued uninstall job, a warning modal, or a recovery path instead of direct deletion.
4. Capture any changed operator guidance back into Focus when the workflow shifts.

## Examples

- Review the current final endpoint removal workflow.
- Check how manual uninstall differs from remote uninstall job handling.
- Verify whether endpoint recovery guidance is still current.
