---
name: plan-threat-model-analysis
description: Build a practical threat model by mapping architecture, trust boundaries, attack surfaces, and prioritized risks before security work fragments into isolated findings.
category: System
pinned: true
trigger_hints: threat model, stride, trust boundary, attack surface, abuse case, architecture risk
---

# Plan threat model analysis

Build a practical threat model by mapping architecture, trust boundaries, attack surfaces, and prioritized risks before security work fragments into isolated findings.

## When to Use This Skill

Use this when a subsystem is security-sensitive, a release needs architectural risk review, or the team needs STRIDE-style analysis rather than only code scanning.

## Workflow Overview

1. Start from the real architecture and trust boundaries, not an idealized diagram.
2. Identify assets, entry points, data flows, privileged operations, and abuse paths that matter for the current system shape.
3. Group risks by boundary and attacker opportunity so the model explains where controls should live.
4. Turn the threat model into concrete mitigations, verification points, and follow-up security work instead of a static document.

## Examples

- Threat-model this new service boundary before rollout.
- Map the attack surface for a workflow that handles secrets or admin operations.
- Build a focused threat model for a new endpoint or background agent path.
