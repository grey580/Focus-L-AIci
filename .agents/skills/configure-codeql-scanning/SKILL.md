---
name: configure-codeql-scanning
description: Plan or review CodeQL scanning so security analysis, workflow triggers, language coverage, and SARIF reporting fit the real repository structure.
category: System
pinned: true
trigger_hints: codeql, code scanning, github actions, sarif, security analysis, workflow
---

# Configure CodeQL scanning

Plan or review CodeQL scanning so security analysis, workflow triggers, language coverage, and SARIF reporting fit the real repository structure.

## When to Use This Skill

Use this when a repo needs GitHub code scanning setup, better CodeQL workflow coverage, or troubleshooting around CodeQL analysis and alerts.

## Workflow Overview

1. Decide whether the repo needs default setup, advanced workflow control, or local CLI analysis.
2. Check language coverage, build mode, triggers, and monorepo layout before editing workflow files.
3. Keep permissions minimal while preserving SARIF upload and actionable analysis results.
4. Treat CodeQL as part of the broader security workflow by pairing setup choices with triage and validation expectations.

## Examples

- Configure CodeQL scanning for this .NET repository.
- Review whether the current codeql.yml has the right languages, triggers, and permissions.
- Plan a safer CodeQL setup for a monorepo or mixed-language project.
