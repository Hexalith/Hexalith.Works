#!/usr/bin/env bash
set -euo pipefail

nuget_api_key="${NUGET_API_KEY:-}"
if [ -z "${nuget_api_key//[[:space:]]/}" ]; then
  echo "[release-secrets] NUGET_API_KEY is required before publishing NuGet packages." >&2
  exit 1
fi
