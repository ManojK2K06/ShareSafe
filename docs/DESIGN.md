# ShareSafe interface refresh

The Windows and macOS interfaces share an eggshell and dark-brown design, with the same section order, action labels and review steps.

## Palette and visual language

| Token | Color | Use |
|---|---|---|
| Eggshell | `#F7F2E7` | Page surfaces, inputs, inverse text and selected navigation |
| Dark brown | `#3B251C` | Text, borders, sidebar, primary actions, graph marks and overlay controls |

Serif page titles and a compact product wordmark provide contrast with readable system body text. Cards group related work. Labels, icons, outlines and explicit messages convey state without adding red, green or blue status colors. Imported images retain their original pixels. Native operating-system chrome, file dialogs and scrollbars retain the platform's appearance.

The app windows, desktop review panels, screenshot-review controls, browser prompt overlay and extension popup use these same two base colors. Windows also has a matching tray icon and tray menu. macOS keeps the native template menu-bar symbol.

## Navigation

Both platforms expose the same seven destinations in a persistent sidebar:

1. **Review & share** — inspect a prompt, document or screenshot and save a reviewed copy.
2. **Environments** — save and switch privacy rules for different kinds of work.
3. **Prepare a project** — make and inspect a separate sanitized project copy.
4. **Sandbox** — verify a prepared copy, run a task and inspect results.
5. **Encrypted vault** — find saved reviewed exports and save a decrypted copy.
6. **Activity** — read local operation history without prompt contents.
7. **Settings** — configure background operation, browser pairing and local tools.

The current environment remains visible. Closing the main window continues to leave ShareSafe running in the tray/menu bar.

## Simpler review flow

The core workflow is **Add content → Review suggestions → Save a copy**. Content and prepared output are clearly labelled. Windows presents them side by side; macOS uses stacked cards with scrollable content. Both expose the same primary actions:

- Choose a file or paste text.
- Check for sensitive information.
- Inspect the suggested changes and their source evidence.
- Create a safe preview, or explicitly review the entire image.
- Save a copy or keep it in the encrypted vault.

Save actions remain unavailable until the corresponding review step is complete. Editing source invalidates the review. A blocking finding prevents saving. Changing finding decisions invalidates the preview and image confirmation. The retained-risk acknowledgement remains explicit. Backend export checks still revalidate the request.

Ordinary errors appear as inline messages instead of interrupting the workflow with repeated dialogs. Deleting a prepared copy or vault entry retains explicit confirmation.

## Environment forms

Users no longer need to edit JSON for everyday policy changes. The forms provide:

- Per-category action selectors with readable labels such as Hide the value and Ask me to review.
- One private or approved phrase per line.
- Pairs of private phrases and readable replacements.
- Separate code-name replacement pairs for project preparation.
- A list of included applications, with a running-app picker.
- Optional local-model settings.

Saving applies the environment to new inline reviews. Windows preserves the existing DPAPI preferences format. macOS now persists named environments in an AES-GCM encrypted preferences file using its existing Keychain-backed vault key. Settings are stored separately from exported vault entries. Environment switches invalidate existing native reviews and close captured desktop prompt panels.

## Project and sandbox operations

Project preparation now follows Choose original → Inspect safe copy → Create sandbox. Summaries show included files, exclusions and the prepared task. Full diagnostic information remains available separately.

Common sandbox tasks are presets: Check Python, List project files and Run a Python file. Custom argument arrays remain under an explicitly advanced choice. Presets are still passed to the existing container controller as argument arrays; there is no host-shell execution path. Local AI is a separate explicit action.

Results explain what happened in ordinary language. Tool transcripts, hashes, graph data and other diagnostic detail remain accessible without dominating the main screens. Both platforms offer reviewed sandbox-result export.

## Source map

| Windows | macOS | Shared browser |
|---|---|---|
| `WindowsApp/Appearance.cs` | `NativeApp/Sources/ShareSafe/Appearance.swift` | `BrowserExtension/popup.css` |
| `WindowsApp/Experience.cs` | `MainExperience.swift`, `WorkspaceExperience.swift` | `BrowserExtension/content.js` |
| `WindowsApp/MainWindow.cs` | `Environments.swift`, `AppModel.swift` | `BrowserExtension/popup.html` |

## Validation

The Windows executable is rebuilt with `scripts/build-windows.ps1 -SkipDependencies` in this installed checkout. The built-in `--render-preview <directory>` mode renders seven native screens with synthetic content and checks meaningful save gates and replacement-form validation. It performs no engine request or external desktop automation.

The restricted engine suite still reports 149 passed and one skipped. JavaScript syntax checks cover the changed extension scripts. Real browser/provider acceptance remains separate from those checks.

macOS changes are source changes in this Windows environment. There is no Swift/macOS toolchain here, so compilation, Keychain round trips for environment preferences and native layout acceptance must be completed on a Mac using `scripts/build-macos.sh`. The encrypted preferences path uses the existing vault key and keeps its payload separate from export entries.
