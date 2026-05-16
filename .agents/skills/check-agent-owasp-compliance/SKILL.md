---
name: check-agent-owasp-compliance
description: Review an agent system against the OWASP Agentic Security Initiative Top 10 so prompt injection, tool abuse, escalation, and audit gaps are found early.
category: System
pinned: true
trigger_hints: owasp, asi, compliance, agent security, prompt injection, tool abuse
---

# Check agent OWASP compliance

Review an agent system against the OWASP Agentic Security Initiative Top 10 so prompt injection, tool abuse, escalation, and audit gaps are found early.

## When to Use This Skill

Use this before production deployment, during a security review, or after major changes to an agent workflow with tools and durable side effects.

## Workflow Overview

1. Evaluate the workflow against the OWASP agentic risk areas, not just general app security.
2. Check prompt-injection handling, tool restrictions, agency limits, trust boundaries, and logging coverage.
3. Note which controls are present, which are missing, and what evidence supports each conclusion.
4. Turn the findings into a concrete hardening backlog instead of a vague compliance label.

## Examples

- Check whether our agent platform covers the OWASP ASI top risks.
- Audit a tool-calling workflow before rollout.
- Review an MCP automation path for escalation and logging gaps.
