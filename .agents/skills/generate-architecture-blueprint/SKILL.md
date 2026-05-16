---
name: generate-architecture-blueprint
description: Create a durable architecture blueprint that explains system boundaries, component responsibilities, data flow, and cross-cutting patterns from the actual code.
category: Task
pinned: true
trigger_hints: architecture, blueprint, system design, dependencies, data flow, handoff
---

# Generate architecture blueprint

Create a durable architecture blueprint that explains system boundaries, component responsibilities, data flow, and cross-cutting patterns from the actual code.

## When to Use This Skill

Use this when the team needs a formal architecture reference, a system handoff, or a grounded view of how the implementation is really put together.

## Workflow Overview

1. Detect the real architecture from the codebase instead of restating intended patterns from old docs.
2. Describe major subsystems, boundaries, dependencies, and data flow in implementation terms.
3. Call out cross-cutting patterns such as auth, validation, logging, resilience, and configuration.
4. Save the blueprint in a form that supports planning, review, and future extension work.

## Examples

- Generate a blueprint for the Focus web platform before a redesign.
- Document the actual architecture of a mixed web, service, and data system.
- Build an implementation-ready system reference from code.
