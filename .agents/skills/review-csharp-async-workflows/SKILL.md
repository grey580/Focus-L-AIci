---
name: review-csharp-async-workflows
description: Check async C# code for deadlock risks, blocking calls, weak cancellation flow, unnecessary allocations, and exception-handling mistakes.
category: System
pinned: true
trigger_hints: csharp, async, await, cancellation, tasks, deadlock, concurrency
---

# Review C# async workflows

Check async C# code for deadlock risks, blocking calls, weak cancellation flow, unnecessary allocations, and exception-handling mistakes.

## When to Use This Skill

Use this when code adds async behavior, background work, I/O, or concurrency and you want a targeted pass on correctness and runtime behavior.

## Workflow Overview

1. Look for blocking calls, fire-and-forget work, and Task usage that breaks async end-to-end behavior.
2. Check naming, return types, cancellation propagation, and exception handling for consistency with the surrounding code.
3. Prefer simple async fixes that preserve behavior before reaching for advanced task patterns.
4. Call out hot-path allocation or parallelization opportunities only when they are likely to matter in practice.

## Examples

- Review this C# async path for blocking or deadlock risks.
- Check whether cancellation tokens are flowing through the new service calls.
- Evaluate a background workflow for Task misuse and exception handling gaps.
