import AppKit
import SwiftUI
import ApplicationServices
import CryptoKit

@MainActor final class PromptMonitor: NSObject, NSWindowDelegate {
    weak var model: AppModel?
    private var timer: Timer?
    private var panel: NSPanel?
    private var lastDraft = ""
    private var stableSince = Date()
    private var shown = ""
    private var snoozedUntil = Date.distantPast
    init(model: AppModel) { self.model = model; super.init() }
    func windowWillClose(_ notification: Notification) { panel = nil; snoozedUntil = Date().addingTimeInterval(20) }
    static var trusted: Bool { AXIsProcessTrusted() }
    static func requestPermission() { _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary) }
    func start() {
        timer = Timer.scheduledTimer(withTimeInterval: 0.6, repeats: true) { [weak self] _ in Task { @MainActor in self?.poll() } }
    }
    func stop() { timer?.invalidate(); panel?.close() }
    func value(_ element: AXUIElement, _ attribute: CFString) -> CFTypeRef? {
        var result: CFTypeRef?; guard AXUIElementCopyAttributeValue(element, attribute, &result) == .success else { return nil }; return result
    }
    func dismiss() { panel?.close(); panel = nil; snoozedUntil = Date().addingTimeInterval(20) }
    func poll() {
        guard let model, model.ready, model.monitoring, Self.trusted else { panel?.close(); panel = nil; return }
        guard let app = NSWorkspace.shared.frontmostApplication, app.processIdentifier != ProcessInfo.processInfo.processIdentifier,
              let bundle = app.bundleIdentifier else { return }
        let allowed = Set(model.bundles.components(separatedBy: .newlines).map { $0.trimmingCharacters(in:.whitespaces) }.filter { !$0.isEmpty })
        guard allowed.contains(bundle) else { panel?.close(); panel = nil; return }
        let root = AXUIElementCreateApplication(app.processIdentifier)
        guard let focused = value(root, kAXFocusedUIElementAttribute as CFString), CFGetTypeID(focused) == AXUIElementGetTypeID() else { return }
        let element = unsafeBitCast(focused, to: AXUIElement.self)
        let role = value(element, kAXRoleAttribute as CFString) as? String ?? ""
        let subrole = value(element, kAXSubroleAttribute as CFString) as? String ?? ""
        var settable = DarwinBoolean(false)
        guard [kAXTextAreaRole as String, kAXTextFieldRole as String].contains(role), subrole != kAXSecureTextFieldSubrole as String,
              AXUIElementIsAttributeSettable(element, kAXValueAttribute as CFString, &settable) == .success, settable.boolValue,
              let draft = value(element, kAXValueAttribute as CFString) as? String, draft.count >= 8, draft.count <= 200000 else { return }
        let hash = SHA256.hash(data: Data((bundle + "\n" + draft).utf8)).map { String(format:"%02x", $0) }.joined()
        if hash != lastDraft { lastDraft = hash; stableSince = Date(); return }
        guard Date().timeIntervalSince(stableSince) >= 1.0, hash != shown, Date() > snoozedUntil, panel == nil else { return }
        shown = hash
        let viewModel = OverlayModel(bridge: model.bridge, element: element, original: draft, app: app) { [weak self] in self?.dismiss() }
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 380, height: 310), styleMask: [.titled, .closable, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.delegate = self
        panel.title = "ShareSafe · Prompt review"; panel.level = .floating; panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false; panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = NSHostingView(rootView: PromptOverlay(model: viewModel))
        let screen = NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) ?? NSScreen.main
        if let screen { panel.setFrameOrigin(NSPoint(x:screen.visibleFrame.maxX - 410, y:screen.visibleFrame.maxY - 350)) }
        panel.isReleasedWhenClosed = false; self.panel = panel; panel.orderFrontRegardless()
    }
}

@MainActor final class OverlayModel: ObservableObject {
    @Published var clean = ""
    @Published var message = "This draft can be checked locally before you send it."
    @Published var busy = false
    @Published var reviewed = false
    let bridge: Bridge?
    let element: AXUIElement
    let original: String
    let app: NSRunningApplication
    let close: () -> Void
    init(bridge: Bridge?, element: AXUIElement, original: String, app: NSRunningApplication, close: @escaping () -> Void) { self.bridge = bridge; self.element = element; self.original = original; self.app = app; self.close = close }
    func review() async {
        guard let bridge else { return }; busy = true; defer { busy = false }
        do { let response = try await bridge.request("/v1/browser/review", ["text":original], browser:true); clean = response["text"] as? String ?? ""; reviewed = true; message = "Local check complete. Review the prepared draft; sending remains your choice." }
        catch { message = error.localizedDescription; reviewed = false }
    }
    func replace() {
        var current: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXValueAttribute as CFString, &current) == .success, current as? String == original else { message = "The draft changed. Close this review and check the latest prompt."; return }
        guard AXUIElementSetAttributeValue(element, kAXValueAttribute as CFString, clean as CFString) == .success else { message = "This editor cannot be replaced. Use Copy prepared and paste it yourself."; return }
        var actual: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXValueAttribute as CFString, &actual) == .success, actual as? String == clean else { message = "The editor did not accept the replacement. Copy prepared and paste it yourself."; return }
        close(); app.activate(options: [])
    }
    func copy() { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(clean,forType:.string); message = "Prepared draft copied. Review it in the original app before sending." }
}
@MainActor struct PromptOverlay: View {
    @ObservedObject var model: OverlayModel
    var body: some View {
        VStack(alignment:.leading, spacing:12) {
            Text("Clean this prompt?").font(.headline)
            Text(model.message).font(.caption).foregroundStyle(.secondary)
            if model.reviewed { ScrollView { Text(model.clean).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading) }.frame(maxHeight:135) }
            if model.busy { ProgressView() }
            HStack {
                if model.reviewed { Button("Replace prompt") { model.replace() }; Button("Copy prepared") { model.copy() } }
                else { Button("Review locally") { Task { await model.review() } }.disabled(model.busy) }
                Button("Dismiss") { model.close() }
            }
            Text("Accessibility review is advisory. It does not intercept every app’s Send action.").font(.caption2).foregroundStyle(.secondary)
        }.padding(18).frame(width:380,height:310)
    }
}
