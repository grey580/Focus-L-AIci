---
name: optimize-sql-performance
description: Tune SQL queries and indexing strategy by focusing on query shape, predicate selectivity, join behavior, pagination, and real execution costs.
category: System
pinned: true
trigger_hints: sql optimization, indexes, query plan, pagination, joins, performance, database
---

# Optimize SQL performance

Tune SQL queries and indexing strategy by focusing on query shape, predicate selectivity, join behavior, pagination, and real execution costs.

## When to Use This Skill

Use this when database work is slow, query plans look suspicious, or a change needs a practical performance review across common SQL engines.

## Workflow Overview

1. Start with the slow query shape and likely access path before proposing indexes.
2. Look for non-sargable predicates, over-broad selects, poor pagination, and avoidable subquery or join costs.
3. Recommend indexes that fit the actual filter and sort patterns instead of generic indexing advice.
4. Keep the optimization tied to measurable bottlenecks, not theoretical micro-tuning.

## Examples

- Optimize this slow SQL query and explain the likely indexing strategy.
- Review pagination and filtering patterns for better database performance.
- Check whether a join or subquery rewrite would materially improve execution cost.
