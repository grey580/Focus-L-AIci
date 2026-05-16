---
name: review-ef-core-data-access
description: Review Entity Framework Core usage for query shape, tracking behavior, relationship mapping, migration safety, and common performance traps.
category: System
pinned: true
trigger_hints: ef core, dbcontext, migrations, tracking, includes, queries, entity framework
---

# Review EF Core data access

Review Entity Framework Core usage for query shape, tracking behavior, relationship mapping, migration safety, and common performance traps.

## When to Use This Skill

Use this when changing EF Core entities, queries, or migrations and you want to avoid N+1 behavior, weak modeling, or fragile persistence changes.

## Workflow Overview

1. Check whether the DbContext, entity configuration, and navigation model match the actual usage pattern.
2. Review queries for tracking mode, projection, pagination, Include usage, and N+1 risk.
3. Inspect SaveChanges boundaries, concurrency assumptions, and transaction handling where writes span multiple operations.
4. Treat migrations as deployable artifacts that need clear intent, safe naming, and runtime awareness.

## Examples

- Review this EF Core query for tracking and N+1 problems.
- Check whether the new migration is shaped safely for deployment.
- Evaluate an entity change for model configuration and relationship drift.
