---
name: design-agent-governance
description: Apply policy controls, trust boundaries, threat detection, and audit logging so agent workflows stay safe and reviewable in production.
category: System
pinned: true
trigger_hints: governance, policy, audit, trust, tool safety, agent controls
---

# Design agent governance

Apply policy controls, trust boundaries, threat detection, and audit logging so agent workflows stay safe and reviewable in production.

## When to Use This Skill

Use this when an agent can call tools, touch durable data, trigger side effects, or coordinate with other agents or external systems.

## Workflow Overview

1. Define the allowed tools, blocked actions, and approval boundaries for the workflow.
2. Add pre-execution intent or threat checks before sensitive tool calls run.
3. Make policy decisions deterministic and auditable instead of relying on the model alone.
4. Record enough telemetry to explain what the agent did, why it was allowed, and how to review it later.

## Examples

- Add tool governance to a Focus MCP workflow.
- Design safety controls for an agent that can modify durable memory.
- Add audit trails and trust boundaries to a multi-agent automation flow.
