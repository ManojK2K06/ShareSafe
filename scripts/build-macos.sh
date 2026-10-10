#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
[[ "$(uname -s)" == Darwin ]] || { echo "Build this app on macOS 14 or later."; exit 1; }
command -v swift >/dev/null || { echo "Install Xcode command line tools first: xcode-select --install"; exit 1; }
PYTHON="${SHARESAFE_PYTHON:-python3}"
"$PYTHON" -c 'import sys; assert sys.version_info >= (3,11), "Python 3.11 or later is required"'
"$PYTHON" -m venv "$ROOT/.venv"
"$ROOT/.venv/bin/python" -m pip install -r "$ROOT/backend/requirements.txt" 'pyinstaller>=6.11,<7'
export PYTHONPATH="$ROOT/backend"
"$ROOT/.venv/bin/python" -m unittest discover -s "$ROOT/backend/tests" -v
"$ROOT/.venv/bin/python" -m PyInstaller --noconfirm --clean --onedir --name sharesafe-engine \
  --paths "$ROOT/backend" --collect-all PIL --collect-all docx --collect-all pymupdf \
  --distpath "$ROOT/build/engine" --workpath "$ROOT/build/pyinstaller" --specpath "$ROOT/build" "$ROOT/backend/bridge_entry.py"
cd "$ROOT/NativeApp"
swift build -c release
BIN="$(swift build -c release --show-bin-path)"
APP="$ROOT/build/ShareSafe.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Engine"
cp "$BIN/ShareSafe" "$APP/Contents/MacOS/ShareSafe"
cp "$ROOT/NativeApp/Info.plist" "$APP/Contents/Info.plist"
ditto "$ROOT/build/engine/sharesafe-engine" "$APP/Contents/Resources/Engine/sharesafe-engine"
codesign --force --deep --sign "${SHARESAFE_SIGN_IDENTITY:--}" "$APP"
echo "Built: $APP"
echo "Open it with: open \"$APP\""
echo "Developer ID signing and notarization are required before distributing outside your own Mac."
