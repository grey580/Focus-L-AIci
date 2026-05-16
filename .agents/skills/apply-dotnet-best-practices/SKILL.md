---
name: apply-dotnet-best-practices
description: Review .NET and C# changes for maintainability, correctness, dependency injection hygiene, async behavior, logging, configuration, and testability.
category: System
pinned: true
trigger_hints: dotnet, csharp, best practices, dependency injection, logging, configuration
---

# Apply .NET best practices

Review .NET and C# changes for maintainability, correctness, dependency injection hygiene, async behavior, logging, configuration, and testability.

## When to Use This Skill

Use this when changing C# or ASP.NET code and you want a focused pass on idiomatic .NET structure before a bug, warning, or style drift turns into a larger issue.

## Workflow Overview

1. Check whether the change follows the repo's existing C# and ASP.NET conventions before applying generic advice.
2. Review dependency injection, exception handling, logging, configuration binding, and async usage for consistency and safety.
3. Look for over-abstraction, leaky visibility, weak naming, or behavior-changing cleanup disguised as style work.
4. Tighten the implementation only where it improves clarity, reliability, or maintainability without widening the scope.

## Examples

- Review this ASP.NET Core change for .NET best-practice drift.
- Check whether the new service registration and logging pattern fit the rest of the solution.
- Give a focused .NET quality pass before we merge.
