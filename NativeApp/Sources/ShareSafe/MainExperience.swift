import SwiftUI
import AppKit

@MainActor struct MainView: View {
    @ObservedObject var model:AppModel
    private let routes=["Review","Environments","Agent Workspace","Sandbox","Vault","Activity","Settings"]
    private let names=["Review & share","Environments","Prepare a project","Sandbox","Encrypted vault","Activity","Settings"]
    private let icons=["shield.lefthalf.filled","slider.horizontal.3","folder","play.circle","lock.doc","clock.arrow.circlepath","gearshape"]
    private var title:String { ["Review":"Review before you share","Environments":"Your privacy environments","Agent Workspace":"Prepare a safe project","Sandbox":"Work in a sandbox","Vault":"Your encrypted vault","Activity":"Your recent activity","Settings":"Make ShareSafe yours"][model.selection] ?? "ShareSafe" }
    private var subtitle:String { ["Review":"A little more privacy. The same useful context.","Environments":"Save different rules for work, study and personal use.","Agent Workspace":"Give an agent a reviewed copy while keeping your original in place.","Sandbox":"Run tasks on the reviewed copy in an isolated container.","Vault":"Keep reviewed exports protected on this device.","Activity":"See what ShareSafe did without saving your prompts in the log.","Settings":"Set up background review, browser pairing and local tools."][model.selection] ?? "" }
    var body: some View {
        HStack(spacing:0) {
            VStack(alignment:.leading,spacing:0) {
                Text("ShareSafe").font(.system(size:30,weight:.medium,design:.serif)).padding(.bottom,8)
                Text("A quieter way to share.").font(.callout).padding(.bottom,32)
                ForEach(Array(routes.enumerated()),id:\.offset) { index,page in
                    Button { model.selection=page } label: {
                        HStack(spacing:12) { Image(systemName:icons[index]).font(.system(size:16)).frame(width:20);Text(names[index]);Spacer(minLength:0) }
                            .padding(.horizontal,12).padding(.vertical,14).frame(maxWidth:.infinity,alignment:.leading)
                            .background(model.selection == page ? SSPalette.eggshell : SSPalette.brown)
                            .foregroundStyle(model.selection == page ? SSPalette.brown : SSPalette.eggshell)
                            .clipShape(RoundedRectangle(cornerRadius:16))
                    }.buttonStyle(.plain).disabled(model.busy).padding(.bottom,5)
                }
                Spacer()
                Text(model.ready ? "ON THIS DEVICE" : "STARTING LOCALLY").font(.caption.bold()).padding(.bottom,6)
                Text("Environment: \(model.activeEnvironmentName)").font(.callout)
                Text("Closing this window keeps ShareSafe in your menu bar.").font(.caption).fixedSize(horizontal:false,vertical:true).padding(.top,14)
            }.padding(22).frame(width:220).frame(maxHeight:.infinity).background(SSPalette.brown).foregroundStyle(SSPalette.eggshell)
            VStack(alignment:.leading,spacing:0) {
                HStack(alignment:.top) {
                    VStack(alignment:.leading,spacing:8) { Text(title).font(.system(size:31,weight:.medium,design:.serif));Text(subtitle).font(.callout) }
                    Spacer()
                    if model.busy { ProgressView().controlSize(.small).tint(SSPalette.brown);Text("Working locally…").font(.caption) }
                }.padding(.horizontal,26).padding(.vertical,24)
                if !model.error.isEmpty { Label(model.error,systemImage:"exclamationmark.circle").textSelection(.enabled).padding(14).frame(maxWidth:.infinity,alignment:.leading).overlay(RoundedRectangle(cornerRadius:8).stroke(SSPalette.brown,lineWidth:1)).padding(.horizontal,24).padding(.bottom,12).accessibilityLabel("Please check: "+model.error) }
                if !model.notice.isEmpty { Label(model.notice,systemImage:"checkmark.circle").font(.callout).padding(.horizontal,24).padding(.bottom,14) }
                Group {
                    switch model.selection {
                    case "Environments": EnvironmentsView(model:model)
                    case "Agent Workspace": ProjectView(model:model)
                    case "Sandbox": SandboxView(model:model)
                    case "Vault": VaultView(model:model)
                    case "Activity": ActivityView(model:model)
                    case "Settings": SettingsView(model:model)
                    default: ReviewView(model:model)
                    }
                }.frame(maxWidth:.infinity,maxHeight:.infinity).disabled(model.busy || !model.ready)
            }.frame(maxWidth:.infinity,maxHeight:.infinity)
        }.background(SSPalette.eggshell).foregroundStyle(SSPalette.brown).tint(SSPalette.brown).buttonStyle(SSButtonStyle()).textFieldStyle(SSTextFieldStyle()).preferredColorScheme(.light)
            .onChange(of:model.selection) { page in
                if page == "Vault" { model.refreshVault() }
                if page == "Activity" { Task { await model.refreshActivity() } }
                if page == "Sandbox" { Task { await model.perform { try await model.refreshWorkspaces() } } }
            }
    }
}
@MainActor struct ReviewView: View {
    @ObservedObject var model:AppModel
    @State private var format="txt"
    private var blocked:Bool { model.result["blocked"] as? Bool == true }
    private var canSave:Bool { model.session != nil && !blocked && (model.image == nil ? !model.prepared.isEmpty : model.imageReviewed) }
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                SSReviewSteps(step:model.session == nil ? 0 : canSave ? 2 : 1)
                SSCard("Your content",subtitle:"Paste a prompt or choose a document or screenshot. Your original file stays in place.") {
                    HStack {
                        Button { Task { await model.importFile() } } label:{Label("Choose a file",systemImage:"folder")}
                        Button { model.pasteClipboard() } label:{Label("Paste text",systemImage:"doc.on.clipboard")}
                        Button { model.clear() } label:{Label("Start over",systemImage:"arrow.counterclockwise")}
                        Button { model.openKnowledgeBase() } label:{Label("Knowledge base",systemImage:"circle.hexagongrid")}.disabled(model.session == nil)
                        Spacer()
                        Button("Edit privacy rules") { model.selection="Environments" }
                    }
                    TextEditor(text:$model.text).font(.body).frame(height:145).ssInput().accessibilityLabel("Source input")
                    Button { Task { await model.analyze() } } label:{Label("Check for sensitive information",systemImage:"checkmark.shield")}.ssPrimary().disabled(model.text.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty)
                }
                if model.session != nil {
                    SSCard("Suggested changes",subtitle:"\(model.findings.count) supported finding(s). Read each suggestion and choose what to change.") {
                        if blocked { Label("Saving is paused. A blocking rule or failed model review needs attention.",systemImage:"exclamationmark.circle").font(.callout) }
                        ForEach(model.result["warnings"] as? [String] ?? [],id:\.self) { Text($0).font(.caption) }
                        TabView {
                            ReviewFindingsView(model:model).tabItem { Text("Suggested changes") }
                            HighlightedSource(text:model.result["text"] as? String ?? "",findings:model.findings).tabItem { Text("Original source") }
                            GraphView(model:model).tabItem { Text("Why these changes?") }
                        }.frame(minHeight:280)
                    }
                    if let image=model.image {
                        SSCard("Review the entire image",subtitle:"Drag over anything that should be hidden. OCR can miss text and other private details.") {
                            RedactionCanvas(model:model,image:image).frame(height:390)
                            HStack { Button("Undo rectangle") { if !model.manualBoxes.isEmpty { model.manualBoxes.removeLast() } };Text("\(model.manualBoxes.count) added rectangle(s)").font(.caption) }
                            Toggle("I reviewed the full image",isOn:$model.imageReviewed)
                        }
                    } else {
                        SSCard("Your safe preview",subtitle:"Read the exact text you will save. Review every part before sharing it.") {
                            Button { Task { await model.preview() } } label:{Label("Create safe preview",systemImage:"doc.text.magnifyingglass")}.ssPrimary().disabled(blocked)
                            ScrollView { Text(model.prepared.isEmpty ? "Your prepared text will appear here after you create a preview." : model.prepared).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading).padding(12) }.frame(minHeight:100,maxHeight:220).ssInput()
                        }
                    }
                    SSCard("Ready to save?",subtitle:"Save a separate copy or keep it encrypted in your vault. ShareSafe never sends it to an AI provider for you.") {
                        Toggle("I accept the risks of anything I chose to keep",isOn:$model.acknowledged)
                        Toggle("Add instructions for a more grounded AI response",isOn:$model.optimize)
                        HStack {
                            if model.image == nil { Picker("Save as",selection:$format) { Text("Text (.txt)").tag("txt");Text("Word (.docx)").tag("docx");Text("PDF (.pdf)").tag("pdf") }.frame(width:200) }
                            Button { Task { await model.export(format) } } label:{Label("Save a copy",systemImage:"square.and.arrow.down")}.ssPrimary().disabled(!canSave)
                            Button { Task { await model.export(format,toVault:true) } } label:{Label("Keep in encrypted vault",systemImage:"lock.doc")}.disabled(!canSave)
                        }
                    }
                }
            }.padding(24)
        }
    }
}
@MainActor struct ReviewFindingsView: View {
    @ObservedObject var model:AppModel
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:14) {
                if model.findings.isEmpty { Label("No supported risk patterns found. Read the source and preview before sharing.",systemImage:"text.magnifyingglass").padding() }
                ForEach(Array(model.findings.enumerated()),id:\.offset) { _,finding in
                    let id=finding["id"] as? String ?? ""
                    HStack(alignment:.top,spacing:12) {
                        Toggle("Apply suggested change",isOn:Binding(get:{model.decisions[id] ?? true},set:{model.decisions[id]=$0})).labelsHidden().disabled(["allow","block"].contains(finding["action"] as? String ?? ""))
                        VStack(alignment:.leading,spacing:5) {
                            Text(SSPalette.label(finding["category"] as? String ?? "")).font(.headline)
                            Text(finding["value"] as? String ?? "").textSelection(.enabled)
                            Text(SSPalette.label(finding["action"] as? String ?? "")+" · "+(finding["reason"] as? String ?? "")).font(.caption)
                        };Spacer()
                    }.padding(12).overlay(RoundedRectangle(cornerRadius:8).stroke(SSPalette.brown,lineWidth:1))
                }
            }.padding(12)
        }
    }
}
