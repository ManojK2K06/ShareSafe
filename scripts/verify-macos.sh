#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PYTHONPATH="$ROOT/backend"
"$ROOT/.venv/bin/python" -m unittest discover -s "$ROOT/backend/tests" -v
cd "$ROOT/NativeApp"
swift build
SHARESAFE_DOCKER_TEST=1 "$ROOT/.venv/bin/python" "$ROOT/tests/docker_isolation.py"
echo "Automated checks completed. Finish docs/MAC_ACCEPTANCE.md before claiming native readiness."
