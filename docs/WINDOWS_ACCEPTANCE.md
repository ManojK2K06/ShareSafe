# Windows build and acceptance

## Run the local build

This checkout already has a compiled development executable and a local Python environment. Quit any older ShareSafe test instance through its tray menu, then double-click `ShareSafe/Start-ShareSafe.cmd` or the project-level `Start-ShareSafe.cmd`. The current executable is `WindowsApp/bin/ShareSafe.Windows.exe`.

For a clean Windows x64 machine, install Python 3.11+ and use .NET Framework 4.8, then run at the ShareSafe repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-windows.ps1 -Python python
.\Start-ShareSafe.cmd
```

Use a currently supported Windows version compatible with your chosen Docker Desktop version. ShareSafe is a regular-user application. Do not run it as administrator to obtain access to more apps.

The offline development build here reused the installed Python runtime's packages and copied PyMuPDF from the retained legacy application's dependency directory into this checkout's venv. It does not import legacy application code at runtime. Recorded engine checks used Python 3.12, Pillow 13, python-docx 1.2.0 and PyMuPDF 1.26.7. The normal fresh build uses `backend/requirements.txt` (including its supported Pillow range). No dependency lockfile or self-contained signed release was produced in this environment.

## Build the optional isolated agent image

Install/start Docker Desktop with its supported WSL 2 or Hyper-V Linux backend. Switch to Linux containers, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-sandbox.ps1
```

For local model review and the coding agent, install Ollama and pull the model configured in the active environment, for example:

```powershell
ollama pull qwen2.5:3b
```

These installations and downloads are separate from ShareSafe. Rules, basic review, environments, project preparation, preview/export and the vault do not need Docker or a model. Actual agent execution needs both.

## Package for another Windows computer

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-windows.ps1 -Package
```

This optional build installs PyInstaller, packages the engine and assembles `build/windows/ShareSafe-Windows-x64.zip`. It requires package access and must be tested on a clean machine. The current session could not download dependencies or PyInstaller, so this package has not been generated or validated. Code signing and an installer remain release work.

## Automated checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-windows.ps1
# Add actual container acceptance only when Docker/image are available:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-windows.ps1 -Docker
# Managed environments that prohibit loopback can exercise logic without TCP:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-windows.ps1 -Restricted
```

The restricted runner explicitly uses in-memory HTTP handlers; it is not a replacement for real TCP, browser or actual-container verification. The normal engine suite uses real loopback requests. Native self-tests create synthetic app data under `tests/tmp`; they do not overwrite the user's normal preferences or vault.

For real Chromium workflows, install Playwright in a development environment and point the runner at it:

```powershell
$env:SHARESAFE_PYTHON = (Resolve-Path .venv\Scripts\python.exe).Path
$env:SHARESAFE_PLAYWRIGHT = 'C:\your-development-tools\node_modules\playwright'
$env:SHARESAFE_BROWSER = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
node tests\browser.mjs
```

Quit ShareSafe before a runner that needs fixed port 8766. The browser suite uses a synthetic composer, not real external submissions. Test the loaded extension against current provider composers as a separate acceptance activity.

## Manual acceptance matrix

| Check | Procedure and pass condition | Current evidence |
|---|---|---|
| Open app | Launch local executable; authenticated engine becomes ready | Verified directly on Windows desktop |
| Review | Analyze `Contact alice@example.org. Name: Alice Morgan.`; inspect EMAIL/PERSON rows | Verified directly |
| Preview | Prepare preview; originals absent, placeholders visible | Verified directly |
| Vault | Save reviewed output; encrypted entry appears in Vault; decrypt to chosen location and inspect | Entry creation verified; DPAPI primitive round trip passed; GUI decrypt/save pending |
| Background | Close window, launch again; same session and vault remain | Verified directly |
| Quit | Quit through tray; native and owned engine exit; active managed containers removed | Parent-handle cleanup tested; full tray/container cleanup pending |
| Sign-in launch | Opt in, sign out/in; tray starts without main window; opt out and verify Run entry removed | Pending |
| Create environment | Create Work, add rules/glossary, save, restart and activate | DPAPI profile persistence tested; full GUI workflow pending |
| Scope environments | Work glossary changes quick reviews; switching profiles invalidates native reviews | Handler policy tests passed; GUI/browser switching pending |
| Browser overlay | Pair extension, review recognized sends, replace only unchanged composer, send explicitly | Existing implementation retained; Windows extension acceptance pending |
| Browser failure | Stop app; recognized send remains paused; original bypass is explicit | Existing browser suite needs Windows rerun |
| Desktop capture | Enable only a synthetic test editor's executable; type and pause; panel appears by editor | Pending per-app UIA provider acceptance |
| Desktop replacement | Review, edit source before Replace; stale replacement refused; supported unchanged editor accepts new text | Source guards implemented; actual provider test pending |
| Unsupported editor | Read-only/TextPattern-only editor offers copy/manual workflow | Pending |
| Excluded apps/passwords | Nonallowed process and password controls never prompt or supply captured text | Code guards implemented; native acceptance pending |
| Documents | Import DOCX/text PDF; export new TXT/DOCX/PDF and inspect omissions | Portable file tests passed; Windows import dialogs pending |
| Screenshot OCR | Import oriented screenshot with known email; inspect OCR geometry and Unicode offsets | Engine OCR validation passed; real Windows OCR pending |
| Manual image review | Add/undo rectangle; export denied until full-image confirmation; output pixels flattened | Engine image export tests passed; native canvas acceptance pending |
| Project copy | Prepare a synthetic project with `.env`; review files and exclusion report; originals unchanged | Portable tests passed |
| Create sandbox | Confirm reviewed plan; workspace appears and verifies; private registry is absent from mount | Controller tests passed; GUI acceptance pending |
| Actual isolation | Run `tests/docker_isolation.py` with built image; no source/network/host secrets/socket; nonroot/read-only root | Blocked: Docker Linux daemon unavailable |
| Quarantine | Introduce supported secret/binary/link into prepared copy; verify, run and export refuse it | Portable tests passed; hard-link creation blocked in managed host |
| Local agent | With model/image ready, run task; inspect actual steps, changed files and verification | Pending |
| Result ZIP | Review resulting files, export; only verified workspace files included | Export controller tests passed; native save flow pending |
| Fresh machine | Build packaged distribution, copy to clean x64 Windows account, run without repository venv | Pending; no packaged release claim |

Use `WINDOWS_ARCHITECTURE.md` for component ownership, storage, trust boundaries, limits and known unsupported features.
