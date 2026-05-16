---
name: review-dotnet-design-patterns
description: Inspect a C# area for design-pattern fit, separation of concerns, testability, and over-engineering without blindly adding abstractions.
category: System
pinned: true
trigger_hints: dotnet, design patterns, architecture review, abstractions, solid, maintainability
---

# Review .NET design patterns

Inspect a C# area for design-pattern fit, separation of concerns, testability, and over-engineering without blindly adding abstractions.

## When to Use This Skill

Use this when a subsystem feels tangled, over-layered, or inconsistent and you need a design review grounded in how the code actually works.

## Workflow Overview

1. Map the concrete responsibilities and dependency flow before recommending patterns.
2. Check for unnecessary wrappers, misplaced abstractions, or missing seams around external dependencies.
3. Review how the code handles commands, factories, repositories, providers, and other recurring patterns only where they fit the current design.
4. Prefer specific recommendations that reduce complexity or improve testability over pattern-by-pattern scoring.

## Examples

- Review this C# feature area for pattern misuse or needless abstraction.
- Check whether the service and repository split is helping or hurting maintainability.
- Evaluate a .NET subsystem for design-pattern drift before refactoring.
