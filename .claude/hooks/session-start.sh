#!/bin/bash
# Installs the .NET 10 SDK so agents can run the local build/test/publish gate.
# Microsoft's download host is blocked by the web sandbox's egress policy, but
# Ubuntu's archive (allowed) ships dotnet-sdk-10.0.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

export DEBIAN_FRONTEND=noninteractive

if ! command -v dotnet >/dev/null 2>&1; then
  apt-get update -qq || true
  apt-get install -y -qq dotnet-sdk-10.0
fi

{
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
  echo 'export DOTNET_NOLOGO=1'
} >> "${CLAUDE_ENV_FILE:-/dev/null}"

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
dotnet restore Overseer.slnx --nologo -v quiet
