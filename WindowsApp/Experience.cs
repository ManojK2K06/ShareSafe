using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareSafe.Windows {
    public sealed partial class MainWindow {
        Panel navigation;
        readonly Label pageTitle = new Label(), pageHint = new Label(), progress = new Label(), environmentBadge = new Label();
        readonly List<WarmButton> navigationButtons = new List<WarmButton>();
        readonly TextBox privateTerms = Editor(), allowedTerms = Editor(), modelName = new TextBox { Width = 240 };
        readonly CheckBox semanticReview = new CheckBox { Text = "Include my local AI model in full document reviews", AutoSize = true };
        readonly DataGridView actionRules = EditableGrid(false), phraseRules = PairGrid("Private phrase", "Use this instead"), codeRules = PairGrid("Private code name", "Safe code name");
        readonly ComboBox commandPreset = new WarmComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        readonly TextBox pythonFile = new TextBox { Text = "main.py", Width = 220 };
        object lastDetails;
        bool previewMode;
        readonly string[] titles = { "Review before you share", "Your privacy environments", "Prepare a safe project", "Work in a sandbox", "Your encrypted vault", "Your recent activity", "Make ShareSafe yours" };
        readonly string[] hints = { "A little more privacy. The same useful context.", "Save different rules for work, study and personal use.", "Give an agent a reviewed copy while keeping your original in place.", "Run tasks on the reviewed copy in an isolated container.", "Keep reviewed exports protected on this device.", "See what ShareSafe did without storing your prompts in the log.", "Set up background review, browser pairing and local tools." };
        void BuildShell() {
            Size = new Size(1400, 940); MinimumSize = new Size(1140, 820);
            Controls.Clear(); tabs.Appearance = TabAppearance.FlatButtons; tabs.SizeMode = TabSizeMode.Fixed; tabs.ItemSize = new Size(0, 1); tabs.Multiline = false;
            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 18, 8) };
            var header = new Panel { Dock = DockStyle.Top, Height = 132, Padding = new Padding(20, 20, 20, 6) };
            pageTitle.Text = titles[0]; pageTitle.Font = new Font("Georgia", 27); pageTitle.Dock = DockStyle.Top; pageTitle.Height = 49;
            pageHint.Text = hints[0]; pageHint.Dock = DockStyle.Top; pageHint.Height = 33;
            progress.Dock = DockStyle.Bottom; progress.Height = 30; progress.Font = new Font("Segoe UI Semibold", 10);
            header.Controls.Add(pageHint); header.Controls.Add(pageTitle); header.Controls.Add(progress);
            content.Controls.Add(tabs); content.Controls.Add(header);
            status.Height = 48; status.Padding = new Padding(24, 12, 12, 6);
            navigation = new WarmSurface { Dock = DockStyle.Left, Width = 222, Padding = new Padding(16, 25, 16, 20), Inverted = true };
            var brand = new Label { Text = "ShareSafe", Font = new Font("Georgia", 25), Height = 54, Dock = DockStyle.Top };
            var subtitle = new Label { Text = "A quieter way to share.", Height = 50, Dock = DockStyle.Top };
            var links = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 18, 0, 0) };
            string[] labels = { "01   Review & share", "02   Environments", "03   Prepare a project", "04   Sandbox", "05   Encrypted vault", "06   Activity", "07   Settings" };
            for (int i = 0; i < labels.Length; i++) {
                var index = i; var button = new WarmButton { Text = labels[i], Width = 187, Height = 48, AutoSize = false, Selected = i == 0, Inverted = true, Font = new Font("Segoe UI", 10.5f) };
                button.Click += delegate { tabs.SelectedIndex = index; }; navigationButtons.Add(button); links.Controls.Add(button);
            }
            environmentBadge.Dock = DockStyle.Bottom; environmentBadge.Height = 78; environmentBadge.Text = "ON THIS DEVICE\r\nEnvironment: " + prefs.Active.Name;
            navigation.Controls.Add(links); navigation.Controls.Add(subtitle); navigation.Controls.Add(brand); navigation.Controls.Add(environmentBadge);
            Controls.Add(content); Controls.Add(status); Controls.Add(navigation);
            tabs.SelectedIndexChanged += delegate { var index = Math.Max(0, tabs.SelectedIndex); pageTitle.Text = titles[index]; pageHint.Text = hints[index]; for (int i = 0; i < navigationButtons.Count; i++) { navigationButtons[i].Selected = i == index; navigationButtons[i].Invalidate(); } RefreshWorkflow(); };
            projectReviewed.CheckedChanged += delegate { RefreshWorkflow(); }; workspaces.SelectedIndexChanged += delegate { RefreshWorkflow(); }; vault.SelectedIndexChanged += delegate { RefreshWorkflow(); };
        }
        void RefreshWorkflow() {
            if (navigation == null) return;
            bool hasReview = session != null, blocked = analysis != null && analysis.ContainsKey("blocked") && Convert.ToBoolean(analysis["blocked"]);
            bool image = analysis != null && Json.Str(analysis, "kind") == "image";
            bool save = hasReview && !blocked && (image ? imageReviewed : !string.IsNullOrEmpty(prepared.Text));
            progress.Text = tabs.SelectedIndex == 0 ? (hasReview ? (save ? "1  Add content     /     2  Review complete     /     3  Ready to save" : "1  Content added     /     2  Review the suggestions     /     3  Save a copy") : "1  Add your content     /     2  Review the suggestions     /     3  Save a copy") : "Environment: " + prefs.Active.Name + "   /   Processing stays on this device";
            environmentBadge.Text = "ON THIS DEVICE\r\nEnvironment: " + prefs.Active.Name;
            foreach (var button in AllControls(tabs).OfType<Button>()) {
                switch (button.Name) {
                    case "Check for sensitive information": button.Enabled = (engine.Ready || previewMode) && (imported != null || source.Text.Trim().Length > 0); break;
                    case "Create safe preview": button.Enabled = hasReview && !blocked && !image; break;
                    case "Review image": button.Enabled = hasReview && image; break;
                    case "Save a copy": case "Keep in encrypted vault": button.Enabled = save; break;
                    case "Export reviewed ZIP": case "Create sandbox": button.Enabled = projectId != null && projectReviewed.Checked; break;
                    case "Verify copy": case "Run selected task": case "Run task with local AI": case "Export reviewed result": case "Delete sandbox": button.Enabled = workspaces.SelectedItem != null; break;
                    case "Save decrypted copy": case "Delete vault entry": button.Enabled = vault.SelectedItem != null; break;
                }
            }
        }
        static IEnumerable<Control> AllControls(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in AllControls(child)) yield return nested; } }
        static DataGridView EditableGrid(bool newRows = true) { return new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = newRows, AllowUserToDeleteRows = newRows, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false, AutoGenerateColumns = false }; }
        static DataGridView PairGrid(string first, string second) { var grid = EditableGrid(); grid.Columns.Add("source", first); grid.Columns.Add("replacement", second); return grid; }
        static string[] Lines(string text) { return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); }
        static Dictionary<string, string> Pairs(DataGridView grid) {
            grid.EndEdit(); var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.Rows) { if (row.IsNewRow) continue; var key = Convert.ToString(row.Cells[0].Value).Trim(); var value = Convert.ToString(row.Cells[1].Value).Trim(); if (key.Length == 0 && value.Length == 0) continue; if (key.Length == 0 || value.Length == 0 || result.ContainsKey(key)) throw new InvalidOperationException("Each replacement needs a unique private phrase and its safe replacement."); result.Add(key, value); }
            return result;
        }
        sealed class ActionChoice { public string Value { get; set; } public string Label { get; set; } }
        void BuildEnvironmentPage() {
            var page = Page("Environments"); var bar = Bar(108);
            environmentName.Text = ""; environmentName.AccessibleName = "Name for a new environment";
            bar.Controls.AddRange(new Control[] { environments, ActionButton("Use selected environment", async delegate {
                var profile = prefs.Environments.Find(x => x.Name == environments.Text); if (profile == null) return;
                await engine.Apply(profile); prefs.ActiveEnvironment = profile.Name; PrivateStore.Save(prefs); LoadEnvironment(); Monitor.Dismiss(); InvalidateReview(); InvalidateProject(); status.Text = "You are now using " + profile.Name + ".";
            }), new Label { Text = "New name", AutoSize = true, Padding = new Padding(0, 8, 4, 0) }, environmentName, ActionButton("Create environment", async delegate {
                var name = environmentName.Text.Trim(); if (name.Length < 1 || name.Length > 60 || prefs.Environments.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Give your environment a unique name, up to 60 characters.");
                var profile = new EnvironmentProfile { Name = name }; await engine.Apply(profile); prefs.Environments.Add(profile); prefs.ActiveEnvironment = name; PrivateStore.Save(prefs); RefreshEnvironmentChoices(); LoadEnvironment(); InvalidateReview(); InvalidateProject();
            }), ActionButton("Save environment", SaveEnvironmentForm) });
            actionRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "category", HeaderText = "Information type", ReadOnly = true });
            var choices = new List<ActionChoice> { new ActionChoice { Value = "mask", Label = "Hide the value" }, new ActionChoice { Value = "generalize", Label = "Use a general description" }, new ActionChoice { Value = "block", Label = "Stop saving this copy" }, new ActionChoice { Value = "warn", Label = "Ask me to review" }, new ActionChoice { Value = "allow", Label = "Keep as it is" } };
            actionRules.Columns.Add(new DataGridViewComboBoxColumn { Name = "action", HeaderText = "What ShareSafe should do", DataSource = choices, DisplayMember = "Label", ValueMember = "Value", DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, FlatStyle = FlatStyle.Flat });
            var settings = new WarmTabs { Dock = DockStyle.Fill };
            AddTab(settings, "Information rules", Appearance.Section("What should stay private?", "Choose a response for each information type. Save to apply these rules.", actionRules));
            AddTab(settings, "Private words", Split(Appearance.Section("Always check these words", "Add one private term per line.", privateTerms), Appearance.Section("Words you have approved", "Add one allowed term per line. Credentials keep their safeguards.", allowedTerms)));
            AddTab(settings, "Replacements", Split(Appearance.Section("Readable replacements", "Add rows to replace private phrases in prompts and documents.", phraseRules), Appearance.Section("Code replacements", "Use safe code names, such as GenericClient, for project copies.", codeRules)));
            var apps = new Panel { Dock = DockStyle.Fill }; var appsBar = Bar(110); var running = new WarmComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
            appsBar.Controls.AddRange(new Control[] { running, ActionButton("Find running applications", delegate {
                running.Items.Clear();
                foreach (var process in Process.GetProcesses()) using (process) { try { if (process.MainWindowHandle != IntPtr.Zero && !new[] { "ShareSafe", "ShareSafe.Windows", "chrome", "msedge", "brave", "firefox" }.Contains(process.ProcessName)) running.Items.Add(process.ProcessName + ".exe"); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
                if (running.Items.Count > 0) running.SelectedIndex = 0; return Task.FromResult(0);
            }), ActionButton("Add application", delegate { var app = running.Text; if (app.Length > 0 && !Lines(processes.Text).Contains(app, StringComparer.OrdinalIgnoreCase)) processes.Text += (processes.Text.Length == 0 ? "" : Environment.NewLine) + app; return Task.FromResult(0); }) });
            apps.Controls.Add(processes); apps.Controls.Add(appsBar);
            AddTab(settings, "Applications", Appearance.Section("Where to offer desktop review", "Pick a running app above, or enter its executable below. Browsers use the extension. Save to apply.", apps));
            var modelPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
            modelPanel.Controls.AddRange(new Control[] { semanticReview, new Label { Text = "Installed local model", AutoSize = true }, modelName, new Label { Text = "Rules work without a model. Full document review can also use Ollama on this device.\r\nBrowser and desktop quick reviews always use rules and graph checks.", Size = new Size(700, 80) } });
            AddTab(settings, "Local AI", modelPanel);
            page.Controls.Add(settings); page.Controls.Add(Note("An environment saves your privacy preferences. To isolate code execution, prepare a project and create a sandbox.", 58)); page.Controls.Add(bar);
        }
        static void AddTab(TabControl tabs, string name, Control content) { var page = new TabPage(name); page.Controls.Add(content); tabs.TabPages.Add(page); }
        void LoadPolicyForm() {
            if (actionRules.Columns.Count == 0) return;
            var saved = Json.Read(policy.Text); actionRules.Rows.Clear(); phraseRules.Rows.Clear(); codeRules.Rows.Clear();
            var actions = saved.ContainsKey("actions") ? (Dictionary<string, object>)saved["actions"] : new Dictionary<string, object>();
            foreach (string category in new[] { "EMAIL", "PHONE", "AADHAAR", "PAN", "ACCOUNT", "CARD", "CREDENTIAL", "PERSON", "ADDRESS", "CONFIDENTIAL", "FINANCIAL" }) {
                var value = actions.ContainsKey(category) ? Convert.ToString(actions[category]) : category == "CREDENTIAL" ? "block" : new[] { "CONFIDENTIAL", "FINANCIAL" }.Contains(category) ? "generalize" : "mask";
                var row = actionRules.Rows[actionRules.Rows.Add(Friendly(category), value)]; row.Tag = category;
            }
            privateTerms.Text = string.Join(Environment.NewLine, Json.Array(saved, "confidential_terms").Select(Convert.ToString)); allowedTerms.Text = string.Join(Environment.NewLine, Json.Array(saved, "allow_terms").Select(Convert.ToString));
            if (saved.ContainsKey("replacements")) foreach (var pair in (Dictionary<string, object>)saved["replacements"]) phraseRules.Rows.Add(pair.Key, pair.Value);
            foreach (var pair in Json.Read(glossary.Text)) codeRules.Rows.Add(pair.Key, pair.Value);
            semanticReview.Checked = saved.ContainsKey("use_model") && Convert.ToBoolean(saved["use_model"]); modelName.Text = saved.ContainsKey("model") ? Json.Str(saved, "model") : "qwen2.5:3b";
            environmentName.Clear(); RefreshWorkflow();
        }
        async Task SaveEnvironmentForm() {
            actionRules.EndEdit(); var rules = Json.Read(policy.Text); var actions = new Dictionary<string, string>();
            foreach (DataGridViewRow row in actionRules.Rows) actions[(string)row.Tag] = Convert.ToString(row.Cells[1].Value);
            rules["actions"] = actions; rules["confidential_terms"] = Lines(privateTerms.Text); rules["allow_terms"] = Lines(allowedTerms.Text); rules["replacements"] = Pairs(phraseRules); rules["model"] = modelName.Text.Trim(); rules["use_model"] = semanticReview.Checked;
            var candidate = new EnvironmentProfile { Name = prefs.Active.Name, Policy = Json.Write(rules), ProjectGlossary = Json.Write(Pairs(codeRules)), Processes = processes.Text };
            await engine.Apply(candidate); var active = prefs.Active; active.Policy = candidate.Policy; active.ProjectGlossary = candidate.ProjectGlossary; active.Processes = candidate.Processes; PrivateStore.Save(prefs); LoadEnvironment(); Monitor.Dismiss(); InvalidateReview(); InvalidateProject(); status.Text = "Saved " + active.Name + ". These rules now apply to new reviews.";
        }
        static string Friendly(string value) {
            var names = new Dictionary<string, string> { { "EMAIL", "Email addresses" }, { "PHONE", "Phone numbers" }, { "AADHAAR", "Aadhaar candidates" }, { "PAN", "PAN candidates" }, { "ACCOUNT", "Bank accounts" }, { "CARD", "Card numbers" }, { "CREDENTIAL", "Passwords & API keys" }, { "PERSON", "People's names" }, { "ADDRESS", "Addresses" }, { "CONFIDENTIAL", "Confidential information" }, { "FINANCIAL", "Financial information" }, { "mask", "Hide the value" }, { "generalize", "Use a general description" }, { "block", "Stop export" }, { "warn", "Review first" }, { "allow", "Keep unchanged" } }; return names.ContainsKey(value ?? "") ? names[value] : value;
        }
        static string DisplayJson(object value) {
            var source = Json.Write(value); var result = new StringBuilder(); int depth = 0; bool quoted = false, escaped = false;
            foreach (char ch in source) {
                if (quoted) { result.Append(ch); if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == '"') quoted = false; continue; }
                if (ch == '"') { quoted = true; result.Append(ch); }
                else if (ch == '{' || ch == '[') { result.Append(ch); depth++; result.AppendLine(); result.Append(' ', depth * 2); }
                else if (ch == '}' || ch == ']') { depth--; result.AppendLine(); result.Append(' ', depth * 2); result.Append(ch); }
                else if (ch == ',') { result.Append(ch); result.AppendLine(); result.Append(' ', depth * 2); }
                else if (ch == ':') result.Append(": "); else result.Append(ch);
            }
            return result.ToString();
        }
        static string ReviewSummary(Dictionary<string, object> result) {
            var text = new StringBuilder("HOW YOUR REVIEW WORKS\r\n\r\n"); text.AppendLine(Json.Array(result, "findings").Length + " supported finding(s). Each suggestion is tied to the original source.");
            foreach (var warning in Json.Array(result, "warnings")) text.AppendLine("\r\nPlease note: " + warning);
            text.AppendLine("\r\nAsk a question above to retrieve the matching source evidence. Unknown private information may remain, so read the whole preview before saving."); return text.ToString();
        }
        string ExplainResult(Dictionary<string, object> result) {
            lastDetails = result;
            if (result.ContainsKey("output")) return "TASK OUTPUT\r\n\r\n" + Json.Str(result, "output") + "\r\n\r\n" + Verification(result);
            if (result.ContainsKey("passed")) return Convert.ToBoolean(result["passed"]) ? "COPY VERIFIED\r\n\r\nNo supplied private terms or supported risks were found. Review the copy before sharing it.\r\n\r\n" + Json.Array(result, "files").Length + " file(s) checked." : "REVIEW NEEDED\r\n\r\nThis copy did not pass verification. Execution and export stay paused. Open technical details for the verification report.";
            if (result.ContainsKey("workspaces")) return Json.Array(result, "workspaces").Length == 0 ? "NO SANDBOXES YET\r\n\r\nStart in Prepare a project. Choose your folder, review the safe copy, then create a sandbox." : "YOUR SANDBOXES\r\n\r\nChoose a prepared copy above. Verify it, then choose a task to run. Read the result before exporting.";
            if (result.ContainsKey("docker")) { var docker = (Dictionary<string, object>)result["docker"]; return "LOCAL TOOLS\r\n\r\nSandbox: " + Json.Str(docker, "message") + "\r\nLocal AI: " + Json.Str(result, "model_status"); }
            if (result.ContainsKey("steps")) return "LOCAL AI TASK\r\n\r\n" + Json.Array(result, "steps").Length + " step(s) recorded.\r\n" + Verification(result) + "\r\n\r\nOpen technical details to inspect every action and tool result.";
            return DisplayJson(result);
        }
        static string Verification(Dictionary<string, object> result) { var verification = result.ContainsKey("verification") ? result["verification"] as Dictionary<string, object> : null; return verification != null && verification.ContainsKey("passed") && Convert.ToBoolean(verification["passed"]) ? "The prepared copy passed its final verification." : "Check the verification report before using or sharing these results."; }
        static string ProjectSummary(Dictionary<string, object> result) {
            var report = (Dictionary<string, object>)result["report"]; var text = new StringBuilder("YOUR PREPARED COPY\r\n\r\n"); text.AppendLine(Json.Str(report, "included_files") + " file(s) included · " + Json.Str(report, "excluded_files") + " file(s) left out"); text.AppendLine("\r\nPrepared task:\r\n" + Json.Str(result, "prompt"));
            foreach (Dictionary<string, object> item in Json.Array(result, "excluded")) text.AppendLine("\r\nLeft out: " + Json.Str(item, "path") + " — " + Json.Str(item, "reason").Replace('_', ' '));
            return text.ToString();
        }
        void ShowTechnicalDetails() { using (var dialog = new Form { Text = "ShareSafe · Technical details", Size = new Size(850, 650), StartPosition = FormStartPosition.CenterParent, Font = Appearance.Body }) { var text = Editor(true); text.Text = Pretty(lastDetails ?? new { }); dialog.Controls.Add(Appearance.Section("Technical details", "Local diagnostic information for the last operation.", text)); Appearance.Apply(dialog); dialog.ShowDialog(this); } }
        void BuildSandboxPage() {
            var page = Page("Sandbox"); var selection = Bar(64); selection.Controls.AddRange(new Control[] { workspaces, ActionButton("Refresh list", RefreshWorkspaces), ActionButton("Verify copy", async delegate { sandboxOutput.Text = ExplainResult(await engine.Request("/v1/workspace/verify", new { workspace_id = WorkspaceId() })); }) });
            commandPreset.Items.AddRange(new object[] { "Check Python", "List project files", "Run a Python file", "Custom command (advanced)" }); commandPreset.SelectedIndex = 0;
            commandPreset.SelectedIndexChanged += delegate { pythonFile.Visible = commandPreset.SelectedIndex == 2; command.Visible = commandPreset.SelectedIndex == 3; };
            pythonFile.Visible = command.Visible = false;
            var tasks = Bar(116); tasks.Controls.AddRange(new Control[] { commandPreset, pythonFile, command, ActionButton("Run selected task", async delegate {
                object argv = commandPreset.SelectedIndex == 0 ? new[] { "python", "--version" } : commandPreset.SelectedIndex == 1 ? new[] { "python", "-c", "from pathlib import Path; print('\\n'.join(str(p) for p in Path('.').rglob('*') if p.is_file()))" } : commandPreset.SelectedIndex == 2 ? new[] { "python", pythonFile.Text.Trim() } : Json.Serializer().DeserializeObject(command.Text);
                sandboxOutput.Text = ExplainResult(await engine.Request("/v1/workspace/run", new { workspace_id = WorkspaceId(), argv = argv }));
            }), ActionButton("Run task with local AI", async delegate { var saved = Json.Read(policy.Text); sandboxOutput.Text = ExplainResult(await engine.Request("/v1/workspace/agent", new { workspace_id = WorkspaceId(), model = saved.ContainsKey("model") ? saved["model"] : "qwen2.5:3b" })); }) });
            var footer = Bar(106); footer.Dock = DockStyle.Bottom;
            footer.Controls.AddRange(new Control[] { ActionButton("Export reviewed result", async delegate { if (MessageBox.Show(this, "Have you reviewed the resulting files and task output?", "Review sandbox result", MessageBoxButtons.YesNo) != DialogResult.Yes) return; var result = await engine.Request("/v1/workspace/export", new { workspace_id = WorkspaceId(), reviewed = true }); SaveFile(Convert.FromBase64String(Json.Str(result, "data")), Json.Str(result, "name")); }), ActionButton("Check local tools", async delegate { sandboxOutput.Text = ExplainResult(await engine.Request("/v1/status")); }), ActionButton("Technical details", delegate { ShowTechnicalDetails(); return Task.FromResult(0); }), ActionButton("Delete sandbox", async delegate { var id = WorkspaceId(); if (MessageBox.Show(this, "Delete this prepared copy? Your original project stays in place.", "Delete sandbox", MessageBoxButtons.YesNo) != DialogResult.Yes) return; await engine.Request("/v1/workspace/delete", new { workspace_id = id, confirmed = true }); await RefreshWorkspaces(); }) });
            sandboxOutput.Text = "NO SANDBOX SELECTED\r\n\r\nChoose a prepared copy above, or start in Prepare a project. The sandbox runs only your reviewed copy, with no network access.";
            page.Controls.Add(Appearance.Section("Results", "Read what actually happened before saving or sharing the output.", sandboxOutput)); page.Controls.Add(footer); page.Controls.Add(tasks); page.Controls.Add(Note("1  Choose a copy     /     2  Verify and run     /     3  Review the result\r\nDocker Desktop and the ShareSafe image are required. Your original folder is never mounted.", 64)); page.Controls.Add(selection);
        }
        void BuildProjectPage() {
            var page = Page("Agent Workspace"); var actions = Bar(64);
            actions.Controls.AddRange(new Control[] { ActionButton("Choose folder", delegate { using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; return Task.FromResult(0); }), ActionButton("Prepare safe copy", async delegate {
                InvalidateProject(); plan = await engine.Request("/v1/project/prepare", new { source = folder.Text, prompt = task.Text, glossary = Json.Read(glossary.Text) }); projectId = Json.Str(plan, "project_id"); lastDetails = plan;
                foreach (Dictionary<string, object> file in Json.Array(plan, "files")) projectFiles.Items.Add(Json.Str(file, "path")); projectReport.Text = ProjectSummary(plan); if (projectFiles.Items.Count > 0) projectFiles.SelectedIndex = 0;
                status.Text = "Your copy is prepared. Read the files, task and exclusions, then confirm your review.";
            }) });
            task.Text = "Review this prepared project and report the checks you actually ran.";
            var input = new Panel { Dock = DockStyle.Fill }; folder.Dock = DockStyle.Top; folder.Height = 34;
            input.Controls.Add(Appearance.Section("What should the agent do?", "Private terms are replaced in this task and the copied files.", task)); input.Controls.Add(folder); input.Controls.Add(Note("Selected project folder", 34));
            var filePanel = new Panel { Dock = DockStyle.Fill }; var filesBar = Bar(64); projectFiles.Width = 360; filesBar.Controls.Add(projectFiles); filePanel.Controls.Add(projectText); filePanel.Controls.Add(filesBar);
            projectFiles.SelectedIndexChanged += delegate { if (plan == null) return; var selected = Json.Array(plan, "files").Cast<Dictionary<string, object>>().FirstOrDefault(x => Json.Str(x, "path") == projectFiles.Text); if (selected != null) projectText.Text = Json.Str(selected, "text"); };
            var top = Split(input, Appearance.Section("Inspect the prepared files", "Choose each file and review its contents.", filePanel)); top.SplitterDistance = 380;
            var content = Split(top, Appearance.Section("What changed?", "Review the prepared task and everything left out of this copy.", projectReport), Orientation.Horizontal); content.SplitterDistance = 310;
            var footer = Bar(116); footer.Dock = DockStyle.Bottom; projectReviewed.Text = "I reviewed all prepared files, the task and the exclusions"; footer.Controls.Add(projectReviewed); footer.SetFlowBreak(projectReviewed, true);
            footer.Controls.AddRange(new Control[] { ActionButton("Create sandbox", async delegate { RequireProject(); var result = await engine.Request("/v1/workspace/create", new { project_id = projectId, reviewed = projectReviewed.Checked }); sandboxOutput.Text = ExplainResult(result); InvalidateProject(); await RefreshWorkspaces(); tabs.SelectedIndex = 3; }), ActionButton("Export reviewed ZIP", async delegate { RequireProject(); var result = await engine.Request("/v1/project/export", new { project_id = projectId, reviewed = projectReviewed.Checked }); SaveFile(Convert.FromBase64String(Json.Str(result, "data")), Json.Str(result, "name")); }), ActionButton("Preparation details", delegate { lastDetails = plan; ShowTechnicalDetails(); return Task.FromResult(0); }) });
            page.Controls.Add(content); page.Controls.Add(footer); page.Controls.Add(Note("1  Choose the original     /     2  Inspect a separate safe copy     /     3  Create a sandbox\r\nYour original stays in place. Add private code names under Environments → Replacements.", 64)); page.Controls.Add(actions);
        }
        sealed class VaultItem { public string Path; public override string ToString() { return "Reviewed copy · " + System.IO.File.GetCreationTime(Path).ToString("dd MMM yyyy, HH:mm"); } }
        string ActivitySummary(Dictionary<string, object> result) {
            lastDetails = result; var events = Json.Array(result, "events"); if (events.Length == 0) return "NOTHING HERE YET\r\n\r\nYour completed reviews and exports will appear here. Your prompts are not saved in this history.";
            var text = new StringBuilder(); foreach (Dictionary<string, object> entry in events) { DateTime timestamp; var at = DateTime.TryParse(Json.Str(entry, "at"), out timestamp) ? timestamp.ToLocalTime().ToString("dd MMM · HH:mm") : Json.Str(entry, "at"); text.AppendLine(at + "   " + Json.Str(entry, "event").Replace('_', ' ')); var details = entry["details"] as Dictionary<string, object>; if (details != null) foreach (var item in details) text.AppendLine("    " + item.Key.Replace('_', ' ') + ": " + Convert.ToString(item.Value)); text.AppendLine(); } return text.ToString();
        }
    }
}
