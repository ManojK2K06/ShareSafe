#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOCKET="/var/run/docker.sock"
[[ ! -S "$HOME/.docker/run/docker.sock" ]] || SOCKET="$HOME/.docker/run/docker.sock"
docker --host "unix://$SOCKET" build --tag sharesafe-agent:local "$ROOT/sandbox"
echo "Image ready. Builds can download dependencies; agent execution has networking disabled."
