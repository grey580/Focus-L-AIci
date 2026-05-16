---
name: instrument-app-insights-telemetry
description: Add Azure Application Insights observability so the web platform emits useful health, error, and usage telemetry with a clear deployment path.
category: System
pinned: true
wing: microsoft
trigger_hints: app insights, telemetry, azure, observability, monitoring, incident triage
---

# Instrument App Insights telemetry

Add Azure Application Insights observability so the web platform emits useful health, error, and usage telemetry with a clear deployment path.

## When to Use This Skill

Use this when the web app needs stronger production telemetry, release visibility, or incident triage support in Azure-hosted environments.

## Workflow Overview

1. Confirm the hosting model, runtime, and deployment path before choosing instrumentation.
2. Prefer the least disruptive Azure instrumentation path that fits the hosting model.
3. Add application telemetry in code or infrastructure with clear configuration boundaries.
4. Verify the resulting telemetry covers health, failures, and the key operator journeys that matter during support incidents.

## Examples

- Add App Insights telemetry to the Focus web app.
- Plan Azure observability for a production ASP.NET Core deployment.
- Improve triage data for web incidents and failed user flows.
