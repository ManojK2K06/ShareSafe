# Access boundaries and privacy claims

ShareSafe is a review workflow plus a managed execution controller. Its claim is bounded: **no supplied private terms or supported detectable risks found in the checked output**. It cannot prove that no unknown credential or sensitive information exists.

## Trusted and restricted components

| Component | Can access | Cannot be treated as |
|---|---|---|
| Native app and local engine | User-selected originals, Keychain vault key, private verification registry, prepared copies | An untrusted coding agent |
| Local Ollama service | Native review source when requested; cleaned task/tool output for coding work | An OS-isolated untrusted runtime |
| Browser extension | Enabled-page composer, browser pairing token, local prompt review API | A universally enforced send firewall |
| Managed container agent | Its prepared copy, fixed tool helper, container binaries, temporary files | An unrestricted host agent |
| Separate desktop coding agent | Whatever that app’s host permissions already allow | Restricted merely because it opened the copy |

The app is a directly distributed macOS utility using Accessibility and an external Docker service. It is not packaged as an App Store App Sandbox application. The coding-agent container is a separate restriction boundary.

## Local API protections

The service listens on 127.0.0.1:8766. Native and browser tokens are distinct and generated locally. Browser authorization can access only prompt review and readiness routes; it cannot read workspaces, update policy, access vault exports, browse source folders or execute a container.

The bridge checks the exact loopback Host header to prevent ordinary DNS rebinding, requires Bearer authentication, enforces JSON and bounded bodies, uses no-store responses and reflects CORS only for supported extension origins. There is no unauthenticated bootstrap/token endpoint. Website origins are denied on POST and preflight. Extension background code also validates sender identity and enabled HTTPS origin.

Tokens are saved user-private in native Application Support and extension-local storage. The app copies only the browser token explicitly. A compromised browser extension with that token can use the scoped review API. Same-user malware can read host files and is outside this token boundary.

## Source preparation and verification

Preparation excludes private configuration, links and unsupported files, validates relative paths, enforces bounded inventory and detects supported patterns and explicit private glossary values. A separate residual check scans paths, contents and the task. The actual ZIP data/metadata is checked before release.

The private registry contains original values to detect reintroduction. It is stored separately, never included in ZIP exports and never mounted into the agent. Prepared files receive SHA-256 hashes at verification; these hashes describe the checked snapshot rather than promising that later edits are safe.

Every managed job verifies the workspace first. Local-model tool proposals, tool outputs and later file state pass additional checks. Risk, binary content, links and non-regular files quarantine the copy. Verification does not validate all program behavior, detect every secret encoding or make arbitrary source instructions trustworthy.

## Container enforcement

The controller invokes Docker with a fixed locally installed image and one fixed prepared-workspace mount. User command arguments come after the image name, so they cannot change Docker mounts, network or privilege flags. Agent tools execute inside that container. The host never uses an agent-supplied shell command.

The local socket selection ignores saved remote contexts. Runtime pulls are disabled. No host credentials or credential directory is forwarded. The container has network none, a read-only root, dropped capabilities, no-new-privileges, resource limits and a temporary filesystem. Source-folder and private-registry paths are never agent inputs.

Container commands can modify the prepared copy, including producing bad code or unknown sensitive data. The controller removes containers after completion and reports verification. Docker isolation depends on a trusted daemon/VM/kernel; it does not protect against a host administrator, malicious host software or container-engine vulnerabilities. Individual-file limits and scans do not constitute a strict aggregate filesystem quota while a job is running.

Normal service shutdown attempts to remove tracked containers, and the packaged engine monitors the native parent’s existence. A hard kill, host crash or unavailable Docker daemon can interrupt cleanup; inspect Docker Desktop for leftover `sharesafe-*` containers after such failures. No claim of crash-proof cleanup is made.

## Practical failure behavior

- Requested model failure, blocked native findings, missing review acknowledgement and expired sessions stop export.
- Changed drafts invalidate prompt replacement; unsupported editors use explicit copy/paste.
- A missing local bridge leaves intercepted browser sending paused, with an explicit original-once acknowledgement available.
- Missing Docker or image stops sandbox execution; there is no automatic host fallback.
- Unsupported project files are omitted. Unknown secrets need supplied private terms and human inspection.
- Tool output that fails privacy checking is withheld from the coding model. Local command output remains reviewable in the UI and must be reviewed before external sharing.

## Primary implementation references

- [Apple Accessibility APIs](https://developer.apple.com/documentation/applicationservices/axuielement_h)
- [Apple login item service](https://developer.apple.com/documentation/servicemanagement/smappservice/mainapp)
- [Apple Vision text recognition](https://developer.apple.com/documentation/vision/vnrecognizetextrequest)
- [Apple Safari extension packaging](https://developer.apple.com/documentation/safariservices/packaging-a-web-extension-for-safari)
- [Docker run flags](https://docs.docker.com/reference/cli/docker/container/run/)
- [Docker security boundaries](https://docs.docker.com/engine/security/)
