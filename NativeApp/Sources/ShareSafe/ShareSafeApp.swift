import SwiftUI
import AppKit

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
    static var cleanup: (() -> Void)?
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationWillTerminate(_ notification: Notification) { Self.cleanup?() }
}
@main @MainActor struct ShareSafeApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject var model = AppModel()
    var body: some Scene {
        Window("ShareSafe", id:"workspace") {
            MainView(model:model).frame(minWidth:1050,minHeight:730)
        }.defaultSize(width:1200,height:820)
        MenuBarExtra("ShareSafe", systemImage:"shield.lefthalf.filled") { MenuContent(model:model) }
    }
}
@MainActor struct MenuContent: View {
    @ObservedObject var model: AppModel
    @Environment(\.openWindow) private var openWindow
    var body: some View {
        Button("Open ShareSafe") { openWindow(id:"workspace"); NSApp.activate(ignoringOtherApps:true) }
        Button("Review clipboard") { model.pasteClipboard(); openWindow(id:"workspace"); NSApp.activate(ignoringOtherApps:true) }
        Toggle("Offer prompt review in AI apps",isOn:$model.monitoring)
        Divider()
        Button("Quit ShareSafe") { model.monitor?.stop(); model.bridge?.stop(); NSApp.terminate(nil) }
    }
}
