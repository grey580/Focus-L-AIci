---
name: plan-dotnet-upgrade
description: Plan a .NET framework or package upgrade in a staged way so target frameworks, package drift, build changes, and validation order stay manageable.
category: Task
pinned: true
trigger_hints: dotnet upgrade, target framework, packages, migration, build pipeline, compatibility
---

# Plan .NET upgrade

Plan a .NET framework or package upgrade in a staged way so target frameworks, package drift, build changes, and validation order stay manageable.

## When to Use This Skill

Use this when a solution needs a .NET upgrade, dependency modernization, or project sequencing review before editing frameworks and packages.

## Workflow Overview

1. Inventory the projects, target frameworks, SDK constraints, and package drift first.
2. Order the work from least-coupled libraries toward app hosts, tests, and pipelines.
3. Identify likely breaking changes, legacy package risks, and configuration updates before making edits.
4. Turn the upgrade into explicit checkpoints covering restore, build, tests, runtime validation, and rollback points.

## Examples

- Plan the upgrade path for this solution to a newer .NET release.
- Check package compatibility before changing TargetFramework values.
- Create a safe sequencing plan for a multi-project .NET upgrade.
