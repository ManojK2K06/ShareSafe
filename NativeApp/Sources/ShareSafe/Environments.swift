import SwiftUI
import CryptoKit
import AppKit

struct PrivacyEnvironment: Codable, Identifiable {
    var id = UUID().uuidString
    var name:String
    var actions:[String:String]
    var glossary:String
    var confidential:String
    var allowed:String
    var projectGlossary:String
    var useModel:Bool
    var model:String
    var bundles:String
}
struct EnvironmentCollection: Codable {
    var active:String
    var profiles:[PrivacyEnvironment]
}
@MainActor extension AppModel {
    func captureEnvironment(_ name:String, id:String = UUID().uuidString) -> PrivacyEnvironment {
        PrivacyEnvironment(id:id,name:name,actions:actions,glossary:glossary,confidential:confidential,allowed:allowed,projectGlossary:projectGlossary,useModel:useModel,model:model,bundles:bundles)
    }
    func restoreEnvironments() async throws {
        guard let vault else { return }
        if let data = try vault.loadPreferences() {
            let saved = try JSONDecoder().decode(EnvironmentCollection.self,from:data)
            guard !saved.profiles.isEmpty else { throw ShareSafeError.message("No saved environment is available.") }
            environments = saved.profiles; activeEnvironmentID = saved.active
            guard let profile = environments.first(where:{$0.id == saved.active}) else { throw ShareSafeError.message("The saved environment could not be found.") }
            applyEnvironmentFields(profile)
        } else {
            let initial = captureEnvironment("Personal"); environments=[initial]; activeEnvironmentID=initial.id
        }
        try await applyInlineEnvironment()
    }
    func applyEnvironmentFields(_ profile:PrivacyEnvironment) {
        actions=profile.actions;glossary=profile.glossary;confidential=profile.confidential;allowed=profile.allowed;projectGlossary=profile.projectGlossary;useModel=profile.useModel;model=profile.model;bundles=profile.bundles
        invalidate();invalidateProject();monitor?.dismiss()
    }
    func applyInlineEnvironment() async throws {
        var inline = try policy(); inline["use_model"]=false
        _ = try await call("/v1/inline/policy",inline)
    }
    func storeEnvironments() throws {
        guard let vault else { throw ShareSafeError.message("Unlock the Keychain to save privacy environments.") }
        try vault.savePreferences(JSONEncoder().encode(EnvironmentCollection(active:activeEnvironmentID,profiles:environments)))
    }
    func saveEnvironment() async {
        await perform {
            _ = try object(self.projectGlossary)
            try await self.applyInlineEnvironment()
            let name=self.environments.first(where:{$0.id == self.activeEnvironmentID})?.name ?? "Personal"
            let profile=self.captureEnvironment(name,id:self.activeEnvironmentID)
            var updated=self.environments
            if let index=updated.firstIndex(where:{$0.id == profile.id}) { updated[index]=profile } else { updated.append(profile) }
            guard let vault=self.vault else { throw ShareSafeError.message("Unlock the Keychain to save environments.") }
            try vault.savePreferences(JSONEncoder().encode(EnvironmentCollection(active:profile.id,profiles:updated)))
            self.environments=updated;self.monitor?.dismiss();self.notice="Saved \(name). These rules now apply to new reviews."
        }
    }
    func activateEnvironment(_ id:String) async {
        await perform {
            guard let profile=self.environments.first(where:{$0.id == id}) else { return }
            let previous=self.captureEnvironment(self.activeEnvironmentName,id:self.activeEnvironmentID)
            let previousID=self.activeEnvironmentID
            self.applyEnvironmentFields(profile)
            do { try await self.applyInlineEnvironment();self.activeEnvironmentID=id;try self.storeEnvironments() }
            catch { self.applyEnvironmentFields(previous);self.activeEnvironmentID=previousID;try? await self.applyInlineEnvironment();throw error }
            self.notice="You are now using \(profile.name)."
        }
    }
    func createEnvironment(_ name:String) async {
        let trimmed=name.trimmingCharacters(in:.whitespacesAndNewlines)
        guard !trimmed.isEmpty,trimmed.count<=60,!environments.contains(where:{$0.name.caseInsensitiveCompare(trimmed) == .orderedSame}) else { error="Give your environment a unique name, up to 60 characters.";return }
        await perform {
            let previous=self.captureEnvironment(self.activeEnvironmentName,id:self.activeEnvironmentID)
            let profile=PrivacyEnvironment(name:trimmed,actions:["EMAIL":"mask","PHONE":"mask","AADHAAR":"mask","PAN":"mask","ACCOUNT":"mask","CARD":"mask","CREDENTIAL":"block","PERSON":"mask","ADDRESS":"mask","CONFIDENTIAL":"generalize","FINANCIAL":"generalize"],glossary:"{}",confidential:"",allowed:"",projectGlossary:"{}",useModel:false,model:"qwen2.5:3b",bundles:"")
            self.applyEnvironmentFields(profile)
            do {
                try await self.applyInlineEnvironment()
                guard let vault=self.vault else { throw ShareSafeError.message("Unlock the Keychain to save environments.") }
                let profiles=self.environments+[profile]
                try vault.savePreferences(JSONEncoder().encode(EnvironmentCollection(active:profile.id,profiles:profiles)))
                self.environments=profiles;self.activeEnvironmentID=profile.id;self.notice="Created \(trimmed). Add your rules and save the environment."
            } catch { self.applyEnvironmentFields(previous);try? await self.applyInlineEnvironment();throw error }
        }
    }
    var activeEnvironmentName:String { environments.first(where:{$0.id == activeEnvironmentID})?.name ?? "Personal" }
}
struct ReplacementRow: Identifiable {
    let id=UUID()
    var source=""
    var replacement=""
}
@MainActor struct ReplacementEditor: View {
    let title:String
    @Binding var json:String
    @State private var rows:[ReplacementRow]=[]
    @State private var message=""
    var body: some View {
        VStack(alignment:.leading,spacing:10) {
            Text(title).font(.headline)
            ForEach($rows) { $row in
                HStack { TextField("Private phrase",text:$row.source);TextField("Use this instead",text:$row.replacement);Button { rows.removeAll(where:{$0.id == row.id});sync() } label:{Image(systemName:"minus.circle").accessibilityLabel("Remove replacement")} }
            }
            Button("Add replacement") { rows.append(ReplacementRow()) }
            if !message.isEmpty { Text(message).font(.caption) }
        }.onAppear { load() }.onChange(of:rows.map { $0.source+"\u{0}"+$0.replacement }) { _ in sync() }
    }
    private func load() { rows=((try? object(json)) ?? [:]).sorted(by:{$0.key<$1.key}).map { ReplacementRow(source:$0.key,replacement:$0.value as? String ?? "") } }
    private func sync() {
        var values:[String:String]=[:];message=""
        for row in rows {
            let source=row.source.trimmingCharacters(in:.whitespacesAndNewlines),replacement=row.replacement.trimmingCharacters(in:.whitespacesAndNewlines)
            if source.isEmpty && replacement.isEmpty { continue }
            guard !source.isEmpty,!replacement.isEmpty,!values.keys.contains(where:{$0.caseInsensitiveCompare(source) == .orderedSame}) else { message="Complete both fields and use each private phrase only once.";json="";return }
            values[source]=replacement
        }
        if let data=try? JSONSerialization.data(withJSONObject:values,options:[.sortedKeys]),let value=String(data:data,encoding:.utf8),value != json { json=value }
    }
}
@MainActor struct EnvironmentsView: View {
    @ObservedObject var model:AppModel
    @State private var selected=""
    @State private var newName=""
    @State private var application=""
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                SSCard("A place for each kind of work",subtitle:"An environment saves your privacy rules. Code isolation is created separately in Sandbox.") {
                    HStack {
                        Picker("Saved environment",selection:$selected) { ForEach(model.environments) { Text($0.name).tag($0.id) } }.frame(maxWidth:300)
                        Button("Use selected environment") { Task { await model.activateEnvironment(selected) } }.disabled(selected.isEmpty || selected == model.activeEnvironmentID)
                        Spacer()
                    }
                    HStack { TextField("New environment name",text:$newName).frame(maxWidth:280);Button("Create environment") { let name=newName;Task { await model.createEnvironment(name);if model.error.isEmpty { newName="" } } } }
                    Text("Editing: \(model.activeEnvironmentName)").font(.headline)
                }
                SSCard("What should stay private?",subtitle:"Choose a response for each information type.") {
                    LazyVGrid(columns:[GridItem(.flexible()),GridItem(.flexible())],alignment:.leading,spacing:14) {
                        ForEach(model.actions.keys.sorted(),id:\.self) { category in
                            Picker(SSPalette.label(category),selection:Binding(get:{model.actions[category] ?? "mask"},set:{model.actions[category]=$0})) { ForEach(["mask","generalize","block","warn","allow"],id:\.self) { Text(SSPalette.label($0)).tag($0) } }
                        }
                    }
                }
                SSCard("Your private words",subtitle:"Add one phrase per line. The same words apply to document and quick prompt reviews after saving.") {
                    HStack(alignment:.top) {
                        VStack(alignment:.leading) { Text("Always check these words");TextEditor(text:$model.confidential).frame(height:100).ssInput() }
                        VStack(alignment:.leading) { Text("Words you have approved");TextEditor(text:$model.allowed).frame(height:100).ssInput() }
                    }
                    ReplacementEditor(title:"Readable replacements",json:$model.glossary).id(model.activeEnvironmentID+"phrases")
                    ReplacementEditor(title:"Code replacements (use names such as GenericClient)",json:$model.projectGlossary).id(model.activeEnvironmentID+"code")
                }
                SSCard("Applications and local AI",subtitle:"Enable desktop suggestions in Settings, then list the applications to include here.") {
                    HStack {
                        Picker("Running application",selection:$application) {
                            Text("Choose an application").tag("")
                            ForEach(NSWorkspace.shared.runningApplications.filter { $0.bundleIdentifier != nil && $0.bundleIdentifier != Bundle.main.bundleIdentifier },id:\.processIdentifier) { app in Text(app.localizedName ?? app.bundleIdentifier ?? "Application").tag(app.bundleIdentifier ?? "") }
                        }
                        Button("Add application") { if !application.isEmpty && !model.bundles.components(separatedBy:.newlines).contains(application) { model.bundles += (model.bundles.isEmpty ? "" : "\n")+application } }.disabled(application.isEmpty)
                    }
                    Text("Application bundle IDs, one per line").font(.headline)
                    TextEditor(text:$model.bundles).frame(height:80).ssInput()
                    Text("For example: com.anthropic.claudefordesktop. Browsers use the extension.").font(.caption)
                    Toggle("Include my local AI model in full document reviews",isOn:$model.useModel)
                    TextField("Installed Ollama model",text:$model.model)
                }
                Button("Save environment") { Task { await model.saveEnvironment() } }.ssPrimary()
            }.padding(24)
        }.onAppear { selected=model.activeEnvironmentID }.onChange(of:model.activeEnvironmentID) { selected=$0 }
    }
}
