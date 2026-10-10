#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/build/safari"
if xcrun --find safari-web-extension-packager >/dev/null 2>&1; then TOOL=safari-web-extension-packager
elif xcrun --find safari-web-extension-converter >/dev/null 2>&1; then TOOL=safari-web-extension-converter
else echo "Install Safari web extension tools from a supported Xcode release."; exit 1; fi
echo "Generating Safari wrapper using $TOOL. Finish signing and enable the extension in Safari Settings."
xcrun "$TOOL" "$ROOT/BrowserExtension" --project-location "$ROOT/build/safari" --app-name "ShareSafe Browser" --bundle-identifier org.sharesafe.browser
