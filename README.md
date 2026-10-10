# ShareSafe for macOS — detailed MVP codebase

This folder contains the new Apple desktop application source, the browser extension, the portable local review engine and the restricted agent workspace controller. The macOS interface uses SwiftUI and AppKit; image OCR uses Apple Vision; vault encryption uses CryptoKit with a Keychain key.

**Current readiness:** the portable engine and browser workflows have measured passing checks in `tests/results`. Native compilation, Accessibility behavior, Keychain behavior, Safari packaging and real container isolation still require the Mac acceptance run. This Windows workspace has no Swift toolchain and its Docker daemon is unavailable. An installable Mac binary has not been produced here.

Recorded checks: **143 portable backend checks**, **19 browser checks** (13 real-browser workflows with the real engine and 6 mock-transport permission checks), and **4 local-model smoke checks** using the installed Qwen model. These counts do not include native Mac or actual-container passes.

The complete product specification is [docs/MVP.md](docs/MVP.md). Read [docs/SECURITY.md](docs/SECURITY.md) for the access boundaries, and [docs/MAC_ACCEPTANCE.md](docs/MAC_ACCEPTANCE.md) before calling this a validated Mac build.

## Build on a Mac

Use macOS 14 or later, Xcode command line tools and Python 3.11 or later. Use a supported Xcode release with the Safari extension tools for Safari packaging. Clone the ShareSafe repository onto the Mac, or copy this whole `ShareSafe` folder, then open Terminal at its root:

```bash
bash scripts/build-macos.sh
open build/ShareSafe.app
```

The build installs Python packages into this folder’s `.venv`, builds a self-contained local engine with PyInstaller, compiles Swift, and assembles a locally signed `ShareSafe.app`. It does not install a system-wide Python package. Dependency downloads need networking during the build.

For the agent sandbox, start Docker Desktop and run:

```bash
bash scripts/build-sandbox.sh
```

For optional semantic review and the local coding agent, install Ollama and download a model:

```bash
ollama pull qwen2.5:3b
```

Ollama must listen locally on port 11434. Rules, graph review, file preparation and export work without a model. If you explicitly request a model check and it fails, export stops. The coding agent requires both a ready local model and the built Docker image.

Grant ShareSafe permission under System Settings → Privacy & Security → Accessibility. Enable prompt monitoring in ShareSafe Settings and add any other AI application bundle IDs you want to monitor. Enable Launch at login separately if desired. Closing the main window leaves the menu bar service running; Quit stops the app-owned engine.

## Browser extension

For Chrome or Edge, open the browser’s extension management page, enable Developer mode, choose **Load unpacked**, and select `BrowserExtension`. In ShareSafe Settings, copy the browser pairing token. Paste it into the extension popup and choose **Pair local app**.

Known domains: ChatGPT, Claude, Gemini, DeepSeek and z.ai. On another HTTPS AI platform, open the extension popup and choose **Enable on this AI website**. Custom-site access is optional and granted one origin at a time.

For Safari:

```bash
bash scripts/package-safari.sh
```

The script selects Apple’s current `safari-web-extension-packager` tool, with the older converter as a fallback, and generates an Xcode wrapper. Finish wrapper signing/building and enable the extension in Safari Settings. Review any API compatibility warnings from Apple’s tool. Safari packaging and installation have not been verified here. Firefox packaging needs a browser-specific manifest/background wrapper and is outside the validated MVP; the shipped manifest targets Chromium plus Safari conversion.

## First demonstration

1. In Review, paste `Contact alice@example.org. Name: Alice Morgan.` Analyze, inspect findings, prepare a preview and export a reviewed copy.
2. Import a screenshot, analyze it, inspect the full image, draw any missed rectangles and confirm image review before exporting a flattened PNG.
3. Add private workflow names in the glossary and use **Use this glossary for browser and app quick reviews**. That quick-review configuration stays in service memory until restart.
4. In a supported browser composer, press Send. Choose **Review locally**, inspect the prepared text, use it, then press Send again. The extension never sends a cleaned prompt automatically.
5. In an enabled AI desktop app, type a draft and pause. The floating panel offers local review. Replacement is available only if the editor exposes an editable Accessibility value and the captured draft is unchanged.
6. In Agent Workspace, choose a small source project. Add private identifiers to the project glossary, prepare the copy, inspect every prepared file and exclusion, and confirm review.
7. Create a sandbox, verify it, run `["python", "--version"]`, then run the local coding agent. Review its actual tool results and the verification report.
8. Save a reviewed export to Vault, decrypt it through the native app, and inspect the counts-only Activity history.

## Folder map

| Folder | Purpose |
|---|---|
| `NativeApp/` | SwiftUI screens, AppKit panel, Accessibility monitoring, Vision OCR, Keychain vault |
| `BrowserExtension/` | Prompt review overlay, pairing, optional site permissions, loopback transport |
| `backend/` | Local findings, policy, graph retrieval, file processing, authenticated API, workspace controller |
| `sandbox/` | Restricted container image and internal agent tool process |
| `scripts/` | Mac app build, Safari wrapper, sandbox build, acceptance runner |
| `docs/` | Detailed MVP, security boundaries, Mac validation checklist |
| `tests/` | Browser checks, real-container acceptance checks and measured results |

Runtime data is stored under `~/Library/Application Support/ShareSafeMac`, outside this codebase: private pairing tokens, audit counts, encrypted vault entries, prepared workspaces and a private verification registry. The registry contains original values for residual checks and is never mounted into the agent’s container.

## Verification

Portable checks:

```bash
export PYTHONPATH="$PWD/backend"
.venv/bin/python tests/run_backend.py
# Install Playwright in a separate development environment before browser checks:
node tests/browser.mjs
# Optional actual local model checks:
.venv/bin/python tests/model_smoke.py
```

The browser runner uses a synthetic AI composer in a real browser and the real local engine. Its background permission tests explicitly use mock transport. It does not submit anything to real AI providers or certify current provider-specific layouts. To use an existing browser/runtime, set `SHARESAFE_BROWSER`, `SHARESAFE_PLAYWRIGHT` and `SHARESAFE_PYTHON` as documented in the runner.

On the Mac after building the app and container:

```bash
bash scripts/verify-macos.sh
```

This calls actual Docker isolation checks. Missing Docker/image causes a failed check, never a fabricated pass. Complete the native manual checks in `docs/MAC_ACCEPTANCE.md` as well.

In the original project workspace, the legacy Windows application is retained separately in `../sharesafe-old/`, alongside the research artifacts. Those materials are not required to build this repository. The poster and paper were not edited by this Apple codebase work.
