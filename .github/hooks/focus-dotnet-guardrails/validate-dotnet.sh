#!/usr/bin/env bash
set -u

mode="${VALIDATION_MODE:-warn}"

run_validation() {
  dotnet build "./FocusLAIci.slnx" &&
  dotnet test "./FocusLAIci.slnx" --no-build
}

if run_validation; then
  echo "[focus-dotnet-guardrails] build and tests passed."
  exit 0
fi

echo "[focus-dotnet-guardrails] build or test validation failed." >&2

if [ "$mode" = "fail" ]; then
  exit 1
fi

echo "[focus-dotnet-guardrails] continuing because VALIDATION_MODE=$mode" >&2
exit 0
