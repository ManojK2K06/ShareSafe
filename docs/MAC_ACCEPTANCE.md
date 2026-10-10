# Required Mac acceptance run

These checks have **not** been marked passed on Windows. Complete them on the intended Mac after the portable checks. Record hardware, macOS/Xcode versions, date and actual results.

## Build and lifecycle

- Run `bash scripts/build-macos.sh`. Require all portable tests and the Swift release build to pass. Resolve any compile diagnostics before using the app.
- Open the built bundle, inspect every native screen at normal and reduced window sizes, and verify the packaged Python engine starts without the source tree being present.
- Close the main window; confirm the menu bar and extension service keep working. Reopen it from the menu. Quit with both menu Quit and Command-Q; confirm the app-owned engine stops.
- Enable login launch, log out and back in, verify the app appears; disable login launch and confirm the setting changes.

## Native overlay

- With Accessibility denied, confirm no focused-field content is read and explicit review still works.
- Grant Accessibility; enable only the intended AI bundle. Type a synthetic email and token in its composer; pause and inspect the panel.
- Inspect quick-review output; replace it and verify exact editor contents. Confirm the app does not send the prompt automatically.
- Edit the draft after capture and before replacement; require rejection. Dismiss the panel, switch apps and check that it does not remain over an unrelated app.
- Confirm secure password fields are excluded. Test an unsupported/non-Accessibility composer and use the clipboard fallback.
- Check each installed wrapper’s real bundle ID. Do not certify a provider solely because its name appears in Settings.

## Documents and Vision

- Import UTF-8 text with emoji, a DOCX with split runs/table text and a text-bearing PDF. Require reviewed exports to omit the synthetic identifiers and hidden source metadata.
- Import portrait/landscape screenshots with rotation metadata, emoji and multiline text. Inspect normalized pixels, top-left redaction coordinates and exact source offsets.
- Add a manual rectangle, undo it, toggle findings and confirm image acknowledgement resets. Export requires full-image review and black pixels must remain after opening the saved PNG elsewhere.
- Test a screenshot with unreadable text, an image-only PDF and an oversize input. Require clear limits without a false clean claim.

## Vault and audit

- Save a reviewed export, quit/reopen, then decrypt/export it. Ciphertext must omit plaintext content/name. Rename or corrupt an encrypted entry; authenticated decryption must fail.
- Lock the Keychain and check the reported error. Verify no silent key replacement occurs for unreadable existing keys.
- Inspect audit events, logs, browser storage and project ZIP metadata for absence of raw prompt text and original private terms. Tokens should only be in the explicitly private pairing stores.

## Browser and Safari

- Pair Chromium and test real current composers on the named AI domains using synthetic data. Cancel Send, use the prepared draft, press Send again, edit drafts and test connection failure. No ShareSafe test should submit private data.
- Test Send-button and Enter behavior, Shift+Enter, React/contenteditable replacement, attachment review and custom site enable/disable.
- Generate the Safari wrapper, review conversion warnings, sign/build it, enable it in Safari and repeat the paired prompt workflow.
- If Safari’s dynamic content-script registration is unavailable, confirm the popup explains the per-tab enable fallback. Do not claim Firefox installation support from the Chromium manifest.

## Actual sandbox isolation

- Start Docker Desktop; build the image; run `bash scripts/verify-macos.sh`.
- Require the actual container checks to prove: non-root UID, read-only root, no host-secret environment, no original folder or Docker socket, loopback-only container interface, zero effective capabilities, no-new-privileges, blocked outbound network, writable prepared copy and unchanged original project.
- Try creating a link, binary file and new supported credential in the copy. Require quarantine before another managed run/model request.
- Run a real small coding task with the installed Ollama model. Inspect actual tool steps and changed files; require the reported final verification state to match the copy. Test timeout, output flooding and an invalid tool request.
- Confirm container cleanup after success and failure. Missing daemon/image must fail, without a host-shell fallback. Review Docker Desktop file sharing permissions for Application Support.

## Distribution

Local ad-hoc signing is for development. Before distribution, sign nested engine components with a stable Developer ID using an appropriate hardened-runtime signing plan, notarize and staple the app, and sign the Safari wrapper. Test Accessibility identity across updates and build/test each architecture you intend to ship. The supplied build creates the host architecture; it does not claim a universal binary or App Store approval.
