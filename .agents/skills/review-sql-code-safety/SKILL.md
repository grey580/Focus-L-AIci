---
name: review-sql-code-safety
description: Review SQL for injection risk, weak permissions, brittle schema decisions, unreadable query shape, and maintainability problems before performance tuning.
category: System
pinned: true
trigger_hints: sql, query review, injection, schema, permissions, code quality, database safety
---

# Review SQL code safety

Review SQL for injection risk, weak permissions, brittle schema decisions, unreadable query shape, and maintainability problems before performance tuning.

## When to Use This Skill

Use this when changing SQL queries, procedures, schema scripts, or database access patterns and you want a focused safety and quality pass.

## Workflow Overview

1. Check parameterization, access boundaries, and sensitive-data exposure before discussing optimization.
2. Review query readability, naming, joins, and schema constraints for maintainability and correctness.
3. Identify anti-patterns such as SELECT *, string-built SQL, or DISTINCT hiding a join problem.
4. Turn findings into concrete fixes or review notes tied to the actual query and database intent.

## Examples

- Review this SQL for injection risk and code-quality problems.
- Check whether a migration script is safe and maintainable before performance tuning.
- Evaluate database queries for least-privilege and schema hygiene concerns.
