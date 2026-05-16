---
name: govern-memory-safely
description: Use duplicate detection, canonical resolution, merge, and governance queues so durable knowledge stays clean instead of drifting.
category: Tooling
pinned: true
trigger_hints: governance, duplicates, merge, canonical, memory hygiene
---

# Govern memory safely

Use duplicate detection, canonical resolution, merge, and governance queues so durable knowledge stays clean instead of drifting.

## When to Use This Skill

Use this when adding or cleaning up durable memories, especially after repeated investigations in the same subsystem.

## Workflow Overview

1. Search first to see whether the knowledge already exists.
2. If a candidate exists, inspect duplicate suggestions or resolve the canonical memory before writing.
3. Use dry-run save for ambiguous updates and only persist when you know whether this should be new, merged, or superseding history.
4. Review governance queues regularly so archived, superseded, and unverified memories do not pile up unnoticed.

## Examples

- Check whether a new Entra mailbox note should merge into an existing memory.
- Use the governance queue to find unverified or aging records.
- Resolve canonical history before citing a memory as current architecture.
