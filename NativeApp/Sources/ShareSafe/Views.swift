import SwiftUI
import AppKit

@MainActor struct MainView: View {
    @ObservedObject var model: AppModel
    private let pages = ["Review", "Agent Workspace", "Sandbox", "Vault", "Activity", "Settings"]
    var body: some View {
        NavigationSplitView {
            VStack(alignment:.leading,spacing:16) {
                Label("ShareSafe",systemImage:"shield.lefthalf.filled").font(.title2.bold()).padding(.horizontal)
                List(pages,id:\.self) { page in Button { model.selection = page } label: { Text(page).frame(maxWidth:.infinity,alignment:.leading).padding(6).background(model.selection == page ? Color.teal.opacity(0.14) : Color.clear).clipShape(RoundedRectangle(cornerRadius:6)) }.buttonStyle(.plain) }
                Label(model.ready ? "Local service ready" : "Starting local service",systemImage:model.ready ? "checkmark.circle.fill" : "clock").font(.caption).padding()
            }.padding(.top)
        } detail: {
            VStack(spacing:0) {
                HStack { Text(model.selection).font(.title.bold()); Spacer(); if model.busy { ProgressView().controlSize(.small); Text("Working locally…").font(.caption) } }.padding()
                if !model.error.isEmpty { Text(model.error).foregroundStyle(.red).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading).padding().background(.red.opacity(0.07)) }
                if !model.notice.isEmpty { Text(model.notice).font(.callout).frame(maxWidth:.infinity,alignment:.leading).padding().background(.green.opacity(0.07)) }
                Divider()
                Group {
                    switch model.selection {
                    case "Agent Workspace": ProjectView(model:model)
                    case "Sandbox": SandboxView(model:model)
                    case "Vault": VaultView(model:model)
                    case "Activity": ActivityView(model:model)
                    case "Settings": SettingsView(model:model)
                    default: ReviewView(model:model)
                    }
                }.disabled(model.busy || !model.ready)
            }
        }
    }
}
@MainActor struct ReviewView: View {
    @ObservedObject var model: AppModel
    @State private var format = "txt"
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:18) {
                Text("Review a prompt, document or screenshot locally. Files export as clean text; screenshots export with permanent black rectangles.").foregroundStyle(.secondary)
                HStack {
                    Button("Import file") { Task { await model.importFile() } }
                    Button("Paste clipboard") { model.pasteClipboard() }
                    Button("Clear review") { model.clear() }
                    Spacer(); Button("Analyze") { Task { await model.analyze() } }.buttonStyle(.borderedProminent)
                }
                TextEditor(text:$model.text).font(.system(.body,design:.monospaced)).frame(height:170).border(.quaternary)
                DisclosureGroup("Policy, private glossary and local model") {
                    VStack(alignment:.leading,spacing:12) {
                        LazyVGrid(columns:[GridItem(.flexible()),GridItem(.flexible()),GridItem(.flexible())]) {
                            ForEach(model.actions.keys.sorted(),id:\.self) { category in
                                Picker(category,selection:Binding(get:{model.actions[category] ?? "mask"},set:{model.actions[category]=$0})) { ForEach(["allow","warn","mask","generalize","block"],id:\.self) { Text($0).tag($0) } }
                            }
                        }
                        Text("Workflow glossary: JSON mapping each private phrase to a generic phrase.").font(.caption)
                        TextEditor(text:$model.glossary).font(.system(.body,design:.monospaced)).frame(height:65).border(.quaternary)
                        Button("Use this glossary for browser and app quick reviews") { Task { await model.useQuickGlossary() } }
                        HStack { TextField("Additional confidential terms, one per line",text:$model.confidential,axis:.vertical); TextField("Allowed terms, one per line",text:$model.allowed,axis:.vertical) }
                        HStack { Toggle("Use local model for semantic findings",isOn:$model.useModel); TextField("Installed Ollama model",text:$model.model) }
                        Text("Model suggestions require exact source evidence. A failed requested model check stops export; it never silently approves.").font(.caption).foregroundStyle(.secondary)
                    }.padding(.top)
                }
                if let session = model.session {
                    Text("Review \(session.prefix(8)) · \(model.findings.count) findings · \(model.result["model_status"] as? String ?? "")").font(.headline)
                    ForEach(model.result["warnings"] as? [String] ?? [],id:\.self) { Text($0).font(.caption).foregroundStyle(.orange) }
                    TabView {
                        HighlightedSource(text:model.result["text"] as? String ?? "",findings:model.findings).tabItem { Text("Source highlights") }
                        findings.tabItem { Text("Findings") }
                        GraphView(model:model).tabItem { Text("Graph & evidence") }
                        VStack(alignment:.leading) { Button("Prepare preview") { Task { await model.preview() } }; ScrollView { Text(model.prepared.isEmpty ? "Prepare the reviewed preview to see the exact output." : model.prepared).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading) } }.padding().tabItem { Text("Prepared text") }
                    }.frame(minHeight:300)
                    if let image = model.image {
                        Text("Screenshot review — drag rectangles over anything OCR missed.").font(.headline)
                        RedactionCanvas(model:model,image:image).frame(height:400)
                        HStack { Button("Undo rectangle") { if !model.manualBoxes.isEmpty { model.manualBoxes.removeLast() } }; Text("\(model.manualBoxes.count) manual rectangles") }
                        Toggle("I reviewed the entire image, including missed text and other sensitive details",isOn:$model.imageReviewed)
                    }
                    Toggle("I acknowledge any findings deliberately retained or marked Warn",isOn:$model.acknowledged)
                    Toggle("Add a grounding instruction scaffold",isOn:$model.optimize)
                    HStack {
                        if model.image == nil { Picker("Export",selection:$format) { Text("TXT").tag("txt"); Text("DOCX").tag("docx"); Text("PDF").tag("pdf") }.frame(width:200) }
                        Button("Export reviewed copy") { Task { await model.export(format) } }.buttonStyle(.borderedProminent).disabled(model.result["blocked"] as? Bool == true)
                        Button("Save in encrypted vault") { Task { await model.export(format,toVault:true) } }.disabled(model.result["blocked"] as? Bool == true)
                    }
                }
            }.padding(22)
        }
    }
    private var findings: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:12) {
                if model.findings.isEmpty { Text("No supported risk patterns found. Review the source yourself before sharing.") }
                ForEach(Array(model.findings.enumerated()),id:\.offset) { _, finding in
                    let id = finding["id"] as? String ?? ""
                    HStack(alignment:.top) {
                        Toggle("",isOn:Binding(get:{model.decisions[id] ?? true},set:{model.decisions[id]=$0})).labelsHidden().disabled(["allow","block"].contains(finding["action"] as? String ?? ""))
                        VStack(alignment:.leading,spacing:4) {
                            Text("\(finding["category"] as? String ?? "") · \(finding["action"] as? String ?? "")").font(.headline)
                            Text(finding["value"] as? String ?? "").font(.system(.body,design:.monospaced)).textSelection(.enabled)
                            Text(finding["reason"] as? String ?? "").font(.caption).foregroundStyle(.secondary)
                            if let evidence = finding["evidence"] as? [String], !evidence.isEmpty { Text(evidence.joined(separator:", ")).font(.caption2) }
                        }; Spacer()
                    }.padding(8).background(.quaternary.opacity(0.5)).clipShape(RoundedRectangle(cornerRadius:8))
                }
            }.padding()
        }
    }
}
@MainActor struct HighlightedSource: NSViewRepresentable {
    let text: String; let findings: [JSON]
    func makeNSView(context:Context) -> NSScrollView {
        let scroll=NSScrollView();scroll.hasVerticalScroller=true
        let editor=NSTextView();editor.isEditable=false;editor.isSelectable=true;editor.isRichText=true
        editor.isVerticallyResizable=true;editor.isHorizontallyResizable=false;editor.autoresizingMask=[.width]
        editor.textContainer?.widthTracksTextView=true;editor.textContainerInset=NSSize(width:12,height:12)
        scroll.documentView=editor;return scroll
    }
    func updateNSView(_ scroll:NSScrollView,context:Context) {
        guard let editor=scroll.documentView as? NSTextView else { return }
        let storage=NSMutableAttributedString(string:text,attributes:[.font:NSFont.monospacedSystemFont(ofSize:13,weight:.regular),.foregroundColor:NSColor.textColor])
        var offsets=[0];var offset=0
        for scalar in text.unicodeScalars { offset += scalar.value > 0xffff ? 2 : 1; offsets.append(offset) }
        for finding in findings {
            guard let start=finding["start"] as? Int, let end=finding["end"] as? Int, start>=0,end>start,end<offsets.count else { continue }
            let range=NSRange(location:offsets[start],length:offsets[end]-offsets[start])
            let color=(finding["action"] as? String == "block" ? NSColor.systemRed : NSColor.systemYellow).withAlphaComponent(0.25)
            storage.addAttributes([.backgroundColor:color,.toolTip:"\(finding["category"] as? String ?? "") · \(finding["action"] as? String ?? "")"],range:range)
        }
        editor.textStorage?.setAttributedString(storage)
    }
}
@MainActor struct GraphView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:12) {
                Text("Entity co-occurrence, explicit aliases, connected communities and exact source evidence.").font(.caption).foregroundStyle(.secondary)
                let graph = model.result["graph"] as? JSON ?? [:]
                let nodes = graph["nodes"] as? [JSON] ?? []
                let edges = graph["edges"] as? [JSON] ?? []
                GraphCanvas(nodes:nodes,edges:edges).frame(height:200)
                DisclosureGroup("Graph details and community reports") { Text(pretty(graph)).font(.system(.caption,design:.monospaced)).textSelection(.enabled) }
                HStack { TextField("Ask for source evidence",text:$model.question); Button("Retrieve evidence") { Task { await model.query() } } }
                Text(model.evidence).font(.system(.caption,design:.monospaced)).textSelection(.enabled)
            }.padding()
        }
    }
}
@MainActor struct GraphCanvas: View {
    let nodes: [JSON]; let edges: [JSON]
    var body: some View {
        GeometryReader { geometry in
            Canvas { context, size in
                let limited = Array(nodes.prefix(24))
                func point(_ index:Int) -> CGPoint { let angle = Double(index) / Double(max(limited.count,1)) * .pi * 2; return CGPoint(x:size.width/2+cos(angle)*(size.width/2-85),y:size.height/2+sin(angle)*(size.height/2-30)) }
                let ids = limited.map { String(describing:$0["id"] ?? "") }
                for edge in edges { if let a = ids.firstIndex(of:String(describing:edge["source"] ?? "")), let b = ids.firstIndex(of:String(describing:edge["target"] ?? "")) { var path=Path(); path.move(to:point(a)); path.addLine(to:point(b)); context.stroke(path,with:.color(.secondary.opacity(0.3)),lineWidth:1) } }
                for (index,node) in limited.enumerated() { let p=point(index); context.fill(Path(ellipseIn:CGRect(x:p.x-5,y:p.y-5,width:10,height:10)),with:.color(.teal)); context.draw(Text(String(describing:node["label"] ?? node["name"] ?? node["id"] ?? "Entity")).font(.caption2),at:CGPoint(x:p.x,y:p.y+15)) }
            }
        }
    }
}
@MainActor struct RedactionCanvas: View {
    @ObservedObject var model: AppModel
    let image: NSImage
    @State private var drag: CGRect?
    var body: some View {
        GeometryReader { geometry in
            let width = (model.result["width"] as? NSNumber)?.doubleValue ?? 1
            let height = (model.result["height"] as? NSNumber)?.doubleValue ?? 1
            let scale = min(geometry.size.width/width,geometry.size.height/height)
            ZStack(alignment:.topLeading) {
                Image(nsImage:image).resizable().frame(width:width*scale,height:height*scale)
                let auto = (model.result["boxes"] as? [JSON] ?? []).filter { box in model.decisions[box["finding_id"] as? String ?? ""] ?? true }
                ForEach(Array((auto+model.manualBoxes).enumerated()),id:\.offset) { _,box in
                    let x=(box["x"] as? NSNumber)?.doubleValue ?? 0; let y=(box["y"] as? NSNumber)?.doubleValue ?? 0
                    let w=(box["width"] as? NSNumber)?.doubleValue ?? 0; let h=(box["height"] as? NSNumber)?.doubleValue ?? 0
                    Rectangle().fill(.black).frame(width:w*scale,height:h*scale).offset(x:x*scale,y:y*scale)
                }
                if let drag { Rectangle().stroke(.red,lineWidth:2).frame(width:drag.width,height:drag.height).offset(x:drag.minX,y:drag.minY) }
            }.frame(width:width*scale,height:height*scale).contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance:2).onChanged { value in
                    let a = CGPoint(x:max(0,min(width*scale,value.startLocation.x)),y:max(0,min(height*scale,value.startLocation.y)))
                    let b = CGPoint(x:max(0,min(width*scale,value.location.x)),y:max(0,min(height*scale,value.location.y)))
                    drag = CGRect(x:min(a.x,b.x),y:min(a.y,b.y),width:abs(a.x-b.x),height:abs(a.y-b.y))
                }.onEnded { _ in
                    if let rect=drag, rect.width>1, rect.height>1 { model.manualBoxes.append(["x":rect.minX/scale,"y":rect.minY/scale,"width":rect.width/scale,"height":rect.height/scale]) }; drag=nil
                })
        }
    }
}
@MainActor struct ProjectView: View {
    @ObservedObject var model: AppModel
    @State private var selectedFile = ""
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:16) {
                Text("Create a reviewed substitute for an agent. The original stays in place. Private configuration, keys, binaries and unsupported files are excluded; supported text files are cleaned and copied.").foregroundStyle(.secondary)
                HStack { TextField("Original project folder",text:$model.source); Button("Choose folder") { model.chooseProject() } }
                Text("Private identifier glossary → safe identifier, for example {\"InternalProject\":\"ExampleProject\"}.").font(.caption)
                TextEditor(text:$model.projectGlossary).font(.system(.body,design:.monospaced)).frame(height:75).border(.quaternary)
                Text("Agent task").font(.headline); TextEditor(text:$model.taskPrompt).frame(height:85).border(.quaternary)
                Button("Prepare cleaned copy") { Task { await model.prepareProject() } }.buttonStyle(.borderedProminent)
                if model.project["project_id"] != nil {
                    Text(pretty(model.project["report"] ?? [:])).font(.system(.caption,design:.monospaced)).textSelection(.enabled)
                    Text("Prepared task").font(.headline); Text(model.project["prompt"] as? String ?? "").textSelection(.enabled)
                    let files = model.project["files"] as? [JSON] ?? []
                    Picker("Prepared file",selection:$selectedFile) { Text("Choose a file").tag(""); ForEach(Array(files.enumerated()),id:\.offset) { _,file in Text(file["path"] as? String ?? "").tag(file["path"] as? String ?? "") } }
                    if let file=files.first(where:{$0["path"] as? String == selectedFile}) { Text("Syntax: \(file["syntax"] as? String ?? "unchecked")").font(.caption); ScrollView { Text(file["text"] as? String ?? "").font(.system(.body,design:.monospaced)).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading) }.frame(height:250) }
                    Toggle("I reviewed every prepared file, exclusions and the task prompt",isOn:$model.projectReviewed)
                    HStack { Button("Create isolated agent sandbox") { Task { await model.createWorkspace() } }.buttonStyle(.borderedProminent); Button("Export reviewed ZIP") { Task { await model.exportProject() } } }.disabled(!model.projectReviewed)
                }
                Text("Detection is bounded. Unknown private information may remain; supply your private terms and inspect the full copy. A copied folder alone cannot restrict an ordinary host-installed agent.").font(.caption).foregroundStyle(.secondary)
            }.padding(22)
        }
    }
}
@MainActor struct SandboxView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:16) {
                Text("Only the prepared copy is mounted in a managed container. Networking is off, the container root is read-only, and host credentials, original folders and the Docker socket are absent.").foregroundStyle(.secondary)
                HStack {
                    Picker("Sandbox",selection:$model.selectedWorkspace) { Text("Choose a sandbox").tag(""); ForEach(Array(model.workspaces.enumerated()),id:\.offset) { _,item in Text((item["id"] as? String ?? "") + ((item["quarantined"] as? Bool ?? true) ? " · quarantined" : " · verified")).tag(item["id"] as? String ?? "") } }
                    Button("Refresh") { Task { await model.perform { try await model.refreshWorkspaces() } } }
                }
                Button("Verify copy again") { Task { await model.sandbox("verify") } }.disabled(model.selectedWorkspace.isEmpty)
                Text("Command arguments (JSON array)").font(.headline)
                TextEditor(text:$model.argv).font(.system(.body,design:.monospaced)).frame(height:70).border(.quaternary)
                HStack { Button("Run in sandbox") { Task { await model.sandbox("run") } }; Button("Run local coding agent") { Task { await model.sandbox("agent") } }.buttonStyle(.borderedProminent) }.disabled(model.selectedWorkspace.isEmpty)
                Text("The local agent uses the installed Ollama model (\(model.model)) with read, write and command tools. It runs at most eight steps. Verification runs before tools, before model requests and after completion; detected risks quarantine the copy.").font(.caption).foregroundStyle(.secondary)
                ScrollView { Text(model.sandboxOutput).font(.system(.body,design:.monospaced)).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading) }.frame(minHeight:260)
                Divider(); Toggle("Delete the selected prepared sandbox and its private review record",isOn:$model.deleteConfirmed)
                Button("Delete prepared sandbox",role:.destructive) { Task { await model.sandbox("delete") } }.disabled(!model.deleteConfirmed || model.selectedWorkspace.isEmpty)
                Text("Host administrators can still access storage. This protects the managed agent execution path; it does not change permissions of separate desktop coding agents. Review sandbox output before sharing it.").font(.caption).foregroundStyle(.secondary)
            }.padding(22)
        }
    }
}
@MainActor struct VaultView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment:.leading,spacing:16) {
            Text("Reviewed exports are encrypted with AES-GCM. The device key lives in your macOS Keychain. Originals are never saved here automatically.").foregroundStyle(.secondary)
            Button("Refresh vault") { model.refreshVault() }
            List(model.vaultEntries,id:\.self) { url in HStack { Label(url.deletingPathExtension().lastPathComponent,systemImage:"lock.doc"); Spacer(); Button("Decrypt and export") { Task { await model.openVault(url) } } } }
            Text("Keep this Mac’s Keychain available. Encrypted vault files alone cannot restore the device key.").font(.caption)
        }.padding(22)
    }
}
@MainActor struct ActivityView: View {
    @ObservedObject var model: AppModel
    var body: some View { VStack(alignment:.leading,spacing:16) { Text("Local audit events contain counts, operation names and model state. Prompt text, original paths, private terms and finding values are omitted.").foregroundStyle(.secondary); Button("Refresh activity") { Task { await model.refreshActivity() } }; ScrollView { Text(model.activity).font(.system(.body,design:.monospaced)).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading) } }.padding(22) }
}
@MainActor struct SettingsView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                Toggle("Offer prompt review in AI applications",isOn:$model.monitoring)
                Toggle("Launch at login",isOn:Binding(get:{model.launchAtLogin},set:{model.login($0)}))
                Button("Grant Accessibility permission") { PromptMonitor.requestPermission() }
                Text("Enable ShareSafe in System Settings → Privacy & Security → Accessibility. The overlay offers review of editable text in the AI applications listed below. It never reads secure password fields.").font(.caption).foregroundStyle(.secondary)
                Text("Monitored application bundle IDs, one per line").font(.headline)
                TextEditor(text:$model.bundles).font(.system(.body,design:.monospaced)).frame(height:100).border(.quaternary)
                Text("Add a wrapper app’s bundle ID to support another AI platform. If its editor is unavailable through Accessibility, use Review clipboard. Use the browser extension for Safari, Chrome, Edge or Firefox.").font(.caption)
                DisclosureGroup("Running application bundle IDs") { Text(NSWorkspace.shared.runningApplications.compactMap { app in app.bundleIdentifier.map { "\(app.localizedName ?? "App"): \($0)" } }.sorted().joined(separator:"\n")).font(.system(.caption,design:.monospaced)).textSelection(.enabled) }
                Divider(); Text("Browser extension pairing").font(.headline)
                Text("Install the extension from the ShareSafe repository’s BrowserExtension folder, then paste the browser-only token into its settings. The extension can review prompts; it cannot browse files, export the vault or control sandboxes.").font(.caption)
                Button("Copy browser pairing token") { model.copyToken() }
                Button("Clear all in-memory reviews") { Task { await model.clearAll() } }
                Divider(); Button("Check local model and Docker") { Task { await model.refreshStatus() } }
                Text(model.status).font(.system(.caption,design:.monospaced)).textSelection(.enabled)
                Text("Install Ollama and download a local model for semantic review or coding-agent work. Build the sandbox image with scripts/build-sandbox.sh after starting Docker Desktop. Rules and graph review work without either dependency.").font(.caption)
            }.padding(22)
        }
    }
}
