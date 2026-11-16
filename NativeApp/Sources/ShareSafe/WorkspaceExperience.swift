import SwiftUI
import AppKit

@MainActor struct ProjectView: View {
    @ObservedObject var model:AppModel
    @State private var selectedFile=""
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                Text("1  Choose the original   /   2  Inspect a separate safe copy   /   3  Create a sandbox").font(.callout)
                SSCard("Your original project",subtitle:"ShareSafe prepares a separate copy. It does not run or modify your original files.") {
                    HStack { TextField("Choose a project folder",text:$model.source);Button("Choose folder") { model.chooseProject() } }
                    Text("What should the agent do?").font(.headline)
                    TextEditor(text:$model.taskPrompt).frame(height:95).ssInput()
                    HStack { Button("Prepare safe copy") { Task { await model.prepareProject() } }.ssPrimary().disabled(model.source.isEmpty);Button("Edit private code names") { model.selection="Environments" } }
                }
                if model.project["project_id"] != nil {
                    let report=model.project["report"] as? JSON ?? [:]
                    SSCard("Inspect the prepared copy",subtitle:"\(report["included_files"] as? Int ?? 0) file(s) included · \(report["excluded_files"] as? Int ?? 0) file(s) left out") {
                        Text("Prepared task").font(.headline)
                        Text(model.project["prompt"] as? String ?? "").textSelection(.enabled)
                        let files=model.project["files"] as? [JSON] ?? []
                        Picker("Prepared file",selection:$selectedFile) { Text("Choose a file").tag("");ForEach(Array(files.enumerated()),id:\.offset) { _,file in Text(file["path"] as? String ?? "").tag(file["path"] as? String ?? "") } }
                        if let file=files.first(where:{$0["path"] as? String == selectedFile}) {
                            Text("Syntax check: \(file["syntax"] as? String ?? "not available")").font(.caption)
                            ScrollView { Text(file["text"] as? String ?? "").font(.system(.body,design:.monospaced)).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading) }.frame(height:250).ssInput()
                        }
                        DisclosureGroup("Files left out of this copy") {
                            let excluded=model.project["excluded"] as? [JSON] ?? []
                            if excluded.isEmpty { Text("No exclusions reported.").font(.caption) }
                            ForEach(Array(excluded.enumerated()),id:\.offset) { _,file in Text((file["path"] as? String ?? "")+" — "+(file["reason"] as? String ?? "").replacingOccurrences(of:"_",with:" ")).font(.caption).textSelection(.enabled) }
                        }
                        DisclosureGroup("Technical preparation details") { Text(pretty(report)).font(.system(.caption,design:.monospaced)).textSelection(.enabled) }
                    }
                    SSCard("Ready for the next step?",subtitle:"Unknown private information may remain. Read every prepared file, the task and the exclusions.") {
                        Toggle("I reviewed all prepared files, the task and the exclusions",isOn:$model.projectReviewed)
                        HStack { Button("Create sandbox") { Task { await model.createWorkspace() } }.ssPrimary();Button("Export reviewed ZIP") { Task { await model.exportProject() } } }.disabled(!model.projectReviewed)
                    }
                }
            }.padding(24)
        }.onChange(of:model.project["project_id"] as? String) { _ in selectedFile="" }
    }
}
@MainActor struct SandboxView: View {
    @ObservedObject var model:AppModel
    @State private var taskChoice="Check Python"
    @State private var pythonFile="main.py"
    @State private var resultReviewed=false
    private let tasks=["Check Python","List project files","Run a Python file","Custom command (advanced)"]
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                Text("1  Choose a copy   /   2  Verify and run   /   3  Review the result").font(.callout)
                SSCard("Choose your prepared copy",subtitle:"Only the reviewed copy is available inside the container. Network access is off.") {
                    if model.workspaces.isEmpty { Text("No sandboxes yet. Start in Prepare a project, inspect the safe copy, then create a sandbox.");Button("Prepare a project") { model.selection="Agent Workspace" } }
                    HStack {
                        Picker("Sandbox",selection:$model.selectedWorkspace) { Text("Choose a sandbox").tag("");ForEach(Array(model.workspaces.enumerated()),id:\.offset) { _,item in Text(workspaceLabel(item)).tag(item["id"] as? String ?? "") } }
                        Button("Refresh list") { Task { await model.perform { try await model.refreshWorkspaces() } } }
                        Button("Verify copy") { Task { await model.sandbox("verify") } }.disabled(model.selectedWorkspace.isEmpty)
                    }
                }
                SSCard("What would you like to run?",subtitle:"Docker Desktop and the ShareSafe image are required. Local AI tasks also need an installed Ollama model.") {
                    Picker("Task",selection:$taskChoice) { ForEach(tasks,id:\.self) { Text($0).tag($0) } }.frame(maxWidth:400)
                    if taskChoice == "Run a Python file" { TextField("Python file in the prepared copy",text:$pythonFile) }
                    if taskChoice == "Custom command (advanced)" { Text("Command argument array").font(.caption);TextEditor(text:$model.argv).font(.system(.body,design:.monospaced)).frame(height:75).ssInput() }
                    HStack { Button("Run selected task") { runTask() };Button("Run task with local AI") { Task { await model.sandbox("agent") } }.ssPrimary() }.disabled(model.selectedWorkspace.isEmpty)
                }
                SSCard("Results",subtitle:"Read what actually happened before saving or sharing the output.") {
                    Text(model.sandboxOutput.isEmpty ? "Task results will appear here. ShareSafe only runs commands inside the prepared container." : model.sandboxOutput).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading)
                    DisclosureGroup("Technical details and tool results") { Text(model.sandboxDetails.isEmpty ? "No task has run yet." : model.sandboxDetails).font(.system(.caption,design:.monospaced)).textSelection(.enabled) }
                    Toggle("I reviewed the resulting files and task output",isOn:$resultReviewed)
                    Button("Export reviewed result") { Task { await model.exportWorkspace() } }.disabled(!resultReviewed || model.selectedWorkspace.isEmpty)
                }
                DisclosureGroup("Remove this prepared copy") {
                    Text("Your original project stays in place. This removes the selected sandbox and its private verification record.").font(.caption)
                    Toggle("I want to permanently delete this prepared copy",isOn:$model.deleteConfirmed)
                    Button("Delete sandbox") { Task { await model.sandbox("delete") } }.disabled(!model.deleteConfirmed || model.selectedWorkspace.isEmpty)
                }
            }.padding(24)
        }.onChange(of:model.sandboxOutput) { _ in resultReviewed=false }.onChange(of:model.selectedWorkspace) { _ in resultReviewed=false;model.deleteConfirmed=false }
    }
    private func workspaceLabel(_ item:JSON) -> String {
        let state=(item["quarantined"] as? Bool ?? true) ? "review needed" : "verified"
        let name=String((item["id"] as? String ?? "").prefix(8))
        return "Copy \(name) · \(state)"
    }
    private func runTask() {
        let arguments:[String]
        switch taskChoice {
        case "Check Python": arguments=["python","--version"]
        case "List project files": arguments=["python","-c","from pathlib import Path; print('\\n'.join(str(p) for p in Path('.').rglob('*') if p.is_file()))"]
        case "Run a Python file": arguments=["python",pythonFile.trimmingCharacters(in:.whitespacesAndNewlines)]
        default: Task { await model.sandbox("run") };return
        }
        guard let data=try? JSONSerialization.data(withJSONObject:arguments),let json=String(data:data,encoding:.utf8) else { return }
        model.argv=json;Task { await model.sandbox("run") }
    }
}
@MainActor struct VaultView: View {
    @ObservedObject var model:AppModel
    var body: some View {
        ScrollView {
            SSCard("Reviewed copies, safely kept",subtitle:"Exports are encrypted on this Mac. Your Keychain keeps the device key; original files are never saved here automatically.") {
                Button("Refresh vault") { model.refreshVault() }
                if model.vaultEntries.isEmpty { Label("No saved copies yet. Review your content, then choose Keep in encrypted vault.",systemImage:"lock.doc").padding(.vertical,24) }
                ForEach(model.vaultEntries,id:\.self) { url in
                    HStack { Label(vaultLabel(url),systemImage:"lock.doc");Spacer();Button("Save decrypted copy") { Task { await model.openVault(url) } } }.padding(12).overlay(RoundedRectangle(cornerRadius:8).stroke(SSPalette.brown,lineWidth:1))
                }
                Text("Saving a decrypted copy creates a normal file at the location you choose. Keep this Mac's Keychain available to open your encrypted entries.").font(.caption)
            }.padding(24)
        }
    }
    private func vaultLabel(_ url:URL) -> String {
        let values=try? url.resourceValues(forKeys:[.creationDateKey]);let formatter=DateFormatter();formatter.dateStyle = .medium;formatter.timeStyle = .short
        if let created=values?.creationDate { return "Reviewed copy · "+formatter.string(from:created) }
        return "Reviewed copy · "+String(url.lastPathComponent.prefix(8))
    }
}
@MainActor struct ActivityView: View {
    @ObservedObject var model:AppModel
    var body: some View {
        ScrollView {
            SSCard("What happened on this device",subtitle:"This history contains operation names and counts. It leaves out your prompts, original paths and private words.") {
                Button("Refresh activity") { Task { await model.refreshActivity() } }
                Text(model.activity.isEmpty ? "Completed reviews and exports will appear here." : model.activity).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading)
            }.padding(24)
        }
    }
}
@MainActor struct SettingsView: View {
    @ObservedObject var model:AppModel
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                SSCard("Always within reach",subtitle:"Closing the window leaves ShareSafe in the menu bar. Use Quit ShareSafe there to stop it.") {
                    Toggle("Offer prompt review in desktop applications",isOn:$model.monitoring)
                    Toggle("Start when I sign in",isOn:Binding(get:{model.launchAtLogin},set:{model.login($0)}))
                    HStack { Button("Open Accessibility setup") { PromptMonitor.requestPermission() };Button("Choose applications") { model.selection="Environments" } }
                    Text("Enable ShareSafe under System Settings → Privacy & Security → Accessibility. Desktop suggestions are advisory and never press Send for you.").font(.caption)
                    DisclosureGroup("Find application bundle IDs") { Text(NSWorkspace.shared.runningApplications.compactMap { app in app.bundleIdentifier.map { "\(app.localizedName ?? "App"): \($0)" } }.sorted().joined(separator:"\n")).font(.system(.caption,design:.monospaced)).textSelection(.enabled) }
                }
                SSCard("Connect your browser",subtitle:"Pair once to review prompts through the extension. Additional HTTPS sites require your explicit permission.") {
                    Text("1. Install the supplied extension.\n2. Copy the browser pairing token below.\n3. Paste it into the extension and choose Connect ShareSafe.")
                    Button("Copy browser pairing token") { model.copyToken() }
                    Text("Chrome and Edge can load the BrowserExtension folder. Safari needs the packaged Safari wrapper. Firefox needs a separate compatible package.").font(.caption)
                }
                SSCard("Local tools",subtitle:"Rules work without Docker or AI. Sandbox tasks need Docker; local AI tasks also need Ollama.") {
                    Button("Check local tools") { Task { await model.refreshStatus() } }
                    if !model.status.isEmpty { Text(model.status).textSelection(.enabled) }
                    DisclosureGroup("Setup details") { Text("Start Docker Desktop, then build the supplied sandbox image with scripts/build-sandbox.sh. For local AI, install Ollama and download the model selected in your environment.").font(.caption) }
                }
                DisclosureGroup("Clear temporary reviews") {
                    Text("This clears document sessions and project previews in memory. It does not delete your files, encrypted vault or prepared sandboxes.").font(.caption)
                    Button("Clear temporary reviews") { Task { await model.clearAll() } }
                }
            }.padding(24)
        }
    }
}
