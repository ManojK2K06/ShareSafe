import SwiftUI
import AppKit
import Vision
import ImageIO
import ServiceManagement

@MainActor final class AppModel: ObservableObject {
    @Published var ready = false
    @Published var busy = false
    @Published var error = ""
    @Published var notice = ""
    @Published var selection = "Review"
    @Published var text = "" { didSet { if text != oldValue { upload = nil; invalidate() } } }
    @Published var model = "qwen2.5:3b" { didSet { invalidate() } }
    @Published var useModel = false { didSet { invalidate() } }
    @Published var glossary = "{}" { didSet { invalidate() } }
    @Published var confidential = "" { didSet { invalidate() } }
    @Published var allowed = "" { didSet { invalidate() } }
    @Published var actions: [String: String] = ["EMAIL":"mask", "PHONE":"mask", "AADHAAR":"mask", "PAN":"mask", "ACCOUNT":"mask", "CARD":"mask", "CREDENTIAL":"block", "PERSON":"mask", "ADDRESS":"mask", "CONFIDENTIAL":"generalize", "FINANCIAL":"generalize"] { didSet { invalidate() } }
    @Published var result: JSON = [:]
    @Published var decisions: [String: Bool] = [:] { didSet { prepared = ""; imageReviewed = false } }
    @Published var prepared = ""
    @Published var acknowledged = false { didSet { prepared = "" } }
    @Published var optimize = false { didSet { prepared = "" } }
    @Published var imageReviewed = false
    @Published var manualBoxes: [JSON] = [] { didSet { imageReviewed = false } }
    @Published var question = ""
    @Published var evidence = ""
    @Published var source = "" { didSet { invalidateProject() } }
    @Published var projectGlossary = "{}" { didSet { invalidateProject() } }
    @Published var taskPrompt = "Review the prepared project and improve its implementation. Run available checks." { didSet { invalidateProject() } }
    @Published var project: JSON = [:]
    @Published var projectReviewed = false
    @Published var workspaces: [JSON] = []
    @Published var selectedWorkspace = ""
    @Published var argv = "[\"python\", \"--version\"]"
    @Published var sandboxOutput = ""
    @Published var deleteConfirmed = false
    @Published var vaultEntries: [URL] = []
    @Published var activity = ""
    @Published var status = ""
    @Published var monitoring: Bool = UserDefaults.standard.bool(forKey: "monitoring") { didSet { UserDefaults.standard.set(monitoring, forKey: "monitoring") } }
    @Published var bundles = UserDefaults.standard.string(forKey: "bundles") ?? "com.openai.chat\ncom.anthropic.claudefordesktop" { didSet { UserDefaults.standard.set(bundles, forKey: "bundles") } }
    @Published var launchAtLogin = false
    var bridge: Bridge?
    var vault: Vault?
    var monitor: PromptMonitor?
    private var upload: JSON?
    private var revision = 0
    private var projectRevision = 0
    init() { Task { await self.boot() } }
    var session: String? { result["session_id"] as? String }
    var findings: [JSON] { result["findings"] as? [JSON] ?? [] }
    var image: NSImage? { guard let value = result["image"] as? String, let data = Data(base64Encoded: value) else { return nil }; return NSImage(data: data) }

    func boot() async {
        await perform {
            let engine = try Bridge(); self.bridge = engine
            try await engine.start(); self.ready = true
            AppDelegate.cleanup = { [weak self] in self?.monitor?.stop(); self?.bridge?.stop() }
            do { self.vault = try Vault(root: engine.directory); self.refreshVault() } catch { self.notice = error.localizedDescription }
            self.monitor = PromptMonitor(model: self); self.monitor?.start()
            self.launchAtLogin = SMAppService.mainApp.status == .enabled
            try await self.refreshWorkspaces()
        }
    }
    func perform(_ work: @escaping () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = ""; notice = ""
        defer { busy = false }
        do { try await work() } catch { self.error = error.localizedDescription }
    }
    func call(_ path: String, _ body: JSON? = nil) async throws -> JSON {
        guard ready, let bridge else { throw ShareSafeError.message("The local service is starting.") }; return try await bridge.request(path, body)
    }
    func invalidate() {
        revision += 1
        if let session, let bridge { Task { _ = try? await bridge.request("/v1/forget", ["session_id": session]) } }
        result = [:]; prepared = ""; evidence = ""; imageReviewed = false; manualBoxes = []; acknowledged = false
    }
    func invalidateProject() {
        projectRevision += 1
        if let id = project["project_id"] as? String, let bridge { Task { _ = try? await bridge.request("/v1/project/forget", ["project_id": id]) } }
        project = [:]; projectReviewed = false
    }
    func clear() { text = ""; upload = nil; invalidate(); notice = "Review content cleared." }
    func clearAll() async { await perform { _ = try await self.call("/v1/clear", [:]); self.clear(); self.invalidateProject(); self.notice = "All in-memory document and project reviews cleared." } }
    func useQuickGlossary() async { await perform { let policy = try self.policy(); _ = try await self.call("/v1/inline/policy", ["replacements":policy["replacements"] ?? [:],"confidential_terms":policy["confidential_terms"] ?? [],"allow_terms":policy["allow_terms"] ?? []]); self.notice = "This glossary now applies to browser and app quick reviews until the service restarts." } }
    func pasteClipboard() { clear(); text = NSPasteboard.general.string(forType: .string) ?? ""; selection = "Review" }
    func policy() throws -> JSON { ["actions": actions, "use_model": useModel, "model": model, "replacements": try object(glossary), "confidential_terms": confidential.components(separatedBy: .newlines).filter { !$0.isEmpty }, "allow_terms": allowed.components(separatedBy: .newlines).filter { !$0.isEmpty }] }
    func analyze() async {
        await perform {
            let generation = self.revision
            var body = self.upload ?? ["text": self.text]; body["policy"] = try self.policy()
            let response = try await self.call("/v1/analyze", body)
            guard generation == self.revision else { _ = try? await self.call("/v1/forget", ["session_id": response["session_id"] ?? ""]); return }
            self.result = response; self.decisions = [:]; self.prepared = ""; self.notice = "Review each finding before exporting."
        }
    }
    func importFile() async {
        let panel = NSOpenPanel(); panel.canChooseDirectories = false; panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        await perform {
            let attributes = try FileManager.default.attributesOfItem(atPath: url.path)
            guard ((attributes[.size] as? NSNumber)?.intValue ?? Int.max) <= 12 * 1024 * 1024 else { throw ShareSafeError.message("Choose a file up to 12 MiB.") }
            var data = try Data(contentsOf: url); var body: JSON = ["name": url.lastPathComponent]
            if ["png", "jpg", "jpeg", "webp"].contains(url.pathExtension.lowercased()) {
                // The normalized pixels used by Vision are the exact bytes scanned and exported.
                let normalized = try await NativeOCR.read(data)
                data = normalized.0; body["name"] = "input.png"; if let ocr = normalized.1 { body["ocr"] = ocr }
            }
            body["file"] = data.base64EncodedString()
            self.clear(); self.text = "Imported: " + url.lastPathComponent; self.upload = body
            self.notice = "File loaded. Analyze it before exporting."
        }
    }
    func reviewBody() throws -> JSON {
        guard let session else { throw ShareSafeError.message("Analyze content first.") }
        return ["session_id":session, "decisions": decisions, "acknowledged": acknowledged, "optimize": optimize]
    }
    func preview() async { await perform { let generation = self.revision; let response = try await self.call("/v1/preview", self.reviewBody()); if generation == self.revision { self.prepared = response["text"] as? String ?? "" } } }
    func export(_ format: String, toVault: Bool = false) async {
        await perform {
            let generation = self.revision
            var body = try self.reviewBody(); body["format"] = format; body["image_reviewed"] = self.imageReviewed; body["boxes"] = self.manualBoxes
            let response = try await self.call("/v1/export", body)
            guard generation == self.revision else { throw ShareSafeError.message("Content changed during export. Analyze again.") }
            let (name, data) = try Self.exportData(response)
            if toVault {
                guard let vault = self.vault else { throw ShareSafeError.message("The Keychain vault is unavailable.") }
                try vault.save(data: data, name: name); self.refreshVault(); self.notice = "Reviewed export saved in the encrypted vault."
            } else { try Self.save(data, name: name) }
        }
    }
    static func exportData(_ response: JSON) throws -> (String, Data) {
        guard let encoded = response["data"] as? String, let data = Data(base64Encoded: encoded), let name = response["name"] as? String else { throw ShareSafeError.message("Invalid export.") }; return (name,data)
    }
    static func save(_ data: Data, name: String) throws {
        let panel = NSSavePanel(); panel.nameFieldStringValue = name
        guard panel.runModal() == .OK, let url = panel.url else { return }; try data.write(to: url, options: .atomic)
    }
    func query() async { await perform { self.evidence = pretty(try await self.call("/v1/query", ["session_id":self.session ?? "", "question": self.question, "use_model":self.useModel])) } }
    func chooseProject() { let panel = NSOpenPanel(); panel.canChooseFiles = false; panel.canChooseDirectories = true; guard panel.runModal() == .OK, let url = panel.url else { return }; source = url.path }
    func prepareProject() async {
        await perform {
            let generation = self.projectRevision
            let response = try await self.call("/v1/project/prepare", ["source":self.source, "glossary":try object(self.projectGlossary), "prompt":self.taskPrompt])
            guard generation == self.projectRevision else { _ = try? await self.call("/v1/project/forget", ["project_id":response["project_id"] ?? ""]); return }
            self.project = response; self.projectReviewed = false
        }
    }
    func exportProject() async { await perform { let response = try await self.call("/v1/project/export", ["project_id":self.project["project_id"] ?? "", "reviewed":self.projectReviewed]); let (name,data) = try Self.exportData(response); try Self.save(data,name:name) } }
    func createWorkspace() async {
        await perform {
            let response = try await self.call("/v1/workspace/create", ["project_id":self.project["project_id"] ?? "", "reviewed":self.projectReviewed])
            self.selectedWorkspace = response["workspace_id"] as? String ?? ""; self.sandboxOutput = pretty(response); self.project = [:]; self.projectReviewed = false
            try await self.refreshWorkspaces(); self.selection = "Sandbox"
        }
    }
    func refreshWorkspaces() async throws { let response = try await call("/v1/workspaces"); workspaces = response["workspaces"] as? [JSON] ?? [] }
    func sandbox(_ action: String) async {
        await perform {
            var body: JSON = ["workspace_id": self.selectedWorkspace]
            if action == "run" { body["argv"] = try JSONSerialization.jsonObject(with: Data(self.argv.utf8)) }
            if action == "agent" { body["model"] = self.model }
            if action == "delete" { body["confirmed"] = self.deleteConfirmed }
            let response = try await self.call("/v1/workspace/" + action, body)
            self.sandboxOutput = pretty(response); self.deleteConfirmed = false; try await self.refreshWorkspaces()
            if action == "delete" { self.selectedWorkspace = "" }
        }
    }
    func refreshVault() { vaultEntries = vault?.list() ?? [] }
    func openVault(_ url: URL) async { await perform { guard let vault = self.vault else { throw ShareSafeError.message("Vault unavailable") }; let (name,data) = try vault.read(url); try Self.save(data, name:name) } }
    func refreshStatus() async { await perform { self.status = pretty(try await self.call("/v1/status")) } }
    func refreshActivity() async { await perform { self.activity = pretty(try await self.call("/v1/audit")) } }
    func login(_ enabled: Bool) {
        do { if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }; launchAtLogin = SMAppService.mainApp.status == .enabled }
        catch { self.error = error.localizedDescription; launchAtLogin = SMAppService.mainApp.status == .enabled }
    }
    func copyToken() { guard let bridge else { return }; NSPasteboard.general.clearContents(); NSPasteboard.general.setString(bridge.browserToken, forType:.string); notice = "Browser pairing token copied. Paste it into the extension settings." }
}

enum NativeOCR {
    static func read(_ data: Data) async throws -> (Data, JSON?) {
        try await Task.detached(priority: .userInitiated) { () throws -> (Data, JSON?) in
            guard let source = CGImageSourceCreateWithData(data as CFData, nil),
                  let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [String: Any],
                  let width = (properties[kCGImagePropertyPixelWidth as String] as? NSNumber)?.intValue,
                  let height = (properties[kCGImagePropertyPixelHeight as String] as? NSNumber)?.intValue,
                  width > 0, height > 0, width <= 16_000_000 / height else { throw ShareSafeError.message("Image cannot be opened or exceeds 16 million pixels.") }
            let options: [String: Any] = [kCGImageSourceCreateThumbnailFromImageAlways as String: true, kCGImageSourceCreateThumbnailWithTransform as String: true, kCGImageSourceThumbnailMaxPixelSize as String: max(width,height)]
            guard let cg = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary),
                  let png = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]) else { throw ShareSafeError.message("The normalized image could not be created.") }
            let request = VNRecognizeTextRequest(); request.recognitionLevel = .accurate; request.usesLanguageCorrection = false
            do { try VNImageRequestHandler(cgImage: cg, options: [:]).perform([request]) }
            catch { return (png, nil) }
            var text = ""; var words: [JSON] = []
            for observation in request.results ?? [] {
                guard let line = observation.topCandidates(1).first else { continue }
                if !text.isEmpty { text += "\n" }
                let start = text.unicodeScalars.count; text += line.string; let end = text.unicodeScalars.count
                guard start < end else { continue }
                let box = observation.boundingBox
                // Vision has a bottom-left origin; image redaction uses top-left pixel coordinates.
                words.append(["text":line.string, "start":start, "end":end, "x":box.minX * Double(cg.width), "y":(1-box.maxY) * Double(cg.height), "width":box.width * Double(cg.width), "height":box.height * Double(cg.height)])
            }
            return (png, ["text": text, "words": words] as JSON)
        }.value
    }
}
