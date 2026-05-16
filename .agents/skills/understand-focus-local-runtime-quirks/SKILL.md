---
name: understand-focus-local-runtime-quirks
description: Remember the local rules that usually matter first: content root, database target override, locked DLLs, and restart/test sequencing.
category: System
pinned: true
trigger_hints: content root, database target, locked dll, launch path, static files
---

# Understand Focus local runtime quirks

Remember the local rules that usually matter first: content root, database target override, locked DLLs, and restart/test sequencing.

## When to Use This Skill

Use this before debugging confusing local behavior where Focus seems to use the wrong data, wrong assets, or refuses to rebuild cleanly.

## Workflow Overview

1. Check which content root the app is using and whether wwwroot resolves from that location.
2. Verify the effective database target and whether focus-palace.database-target.json is overriding it.
3. Stop the exact running dotnet host before rebuilding if DLLs are locked.
4. Retest using the same launch path and command that reproduces the issue.

## Examples

- Why is Focus using the wrong database?
- Why do tests fail because FocusLAIci.Web.dll is locked?
- Why does the page HTML load but static files 404?
