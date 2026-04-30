# Focus .NET Guardrails Hook

Runs a lightweight build-and-test validation at the end of Copilot coding-agent sessions.

## What it does

- restores/builds the `FocusLAIci.slnx` solution
- runs the test suite with `dotnet test`
- reports failures without editing files

## Default behavior

The included `hooks.json` runs in **warn** mode so failed validation is surfaced without hard-blocking the session.

To make it strict, change:

```json
"VALIDATION_MODE": "warn"
```

to:

```json
"VALIDATION_MODE": "fail"
```
