using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShareSafe.Windows {
    public sealed class MainWindow : Form {
        readonly Engine engine; readonly Preferences prefs;
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(12), Text = "Starting local engine..." };
        readonly TextBox source = Editor(), prepared = Editor(true), evidence = Editor(true), policy = Editor(), processes = Editor(), glossary = Editor();
        readonly DataGridView findings = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        readonly CheckBox acknowledge = new CheckBox { Text = "I reviewed any retained risks", AutoSize = true };
        readonly CheckBox optimize = new CheckBox { Text = "Add grounding instructions", AutoSize = true };
        readonly ComboBox format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        readonly ComboBox environments = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        readonly TextBox environmentName = new TextBox { Width = 180 };
        readonly CheckBox monitoring = new CheckBox { Text = "Enable desktop prompt overlays", AutoSize = true };
        readonly CheckBox login = new CheckBox { Text = "Start in the background when I sign in", AutoSize = true };
        readonly TextBox folder = new TextBox { Dock = DockStyle.Fill }, task = Editor(), projectText = Editor(true), projectReport = Editor(true);
        readonly ComboBox projectFiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        readonly CheckBox projectReviewed = new CheckBox { Text = "I reviewed every prepared file, prompt and exclusion report", AutoSize = true };
        readonly ComboBox workspaces = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
        readonly TextBox command = new TextBox { Text = "[\"python\",\"--version\"]", Width = 390 };
        readonly TextBox sandboxOutput = Editor(true), activity = Editor(true), question = new TextBox { Width = 350 };
        readonly ListBox vault = new ListBox { Dock = DockStyle.Fill };
        string session, projectId, importedName; byte[] imported;
        Dictionary<string, object> analysis, plan;
        List<object> manualBoxes = new List<object>(); bool imageReviewed, changing, exiting; int generation;
        public PromptMonitor Monitor { get; private set; }
        public MainWindow(Engine bridge, Preferences settings) {
            engine = bridge; prefs = settings;
            Text = "ShareSafe for Windows"; Size = new Size(1180, 850); MinimumSize = new Size(900, 680); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(244, 247, 251);
            Controls.Add(tabs); Controls.Add(status);
            ReviewPage(); EnvironmentsPage(); ProjectPage(); SandboxPage(); VaultPage(); ActivityPage(); SettingsPage();
            source.TextChanged += delegate { if (!changing) { imported = null; importedName = null; InvalidateReview(); } };
            policy.TextChanged += delegate { if (!changing) InvalidateReview(); };
            acknowledge.CheckedChanged += delegate { prepared.Clear(); };
            optimize.CheckedChanged += delegate { prepared.Clear(); };
            findings.CellValueChanged += delegate { prepared.Clear(); imageReviewed = false; acknowledge.Checked = false; };
            findings.CurrentCellDirtyStateChanged += delegate { if (findings.IsCurrentCellDirty) findings.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            folder.TextChanged += delegate { InvalidateProject(); }; task.TextChanged += delegate { InvalidateProject(); }; glossary.TextChanged += delegate { if (!changing) InvalidateProject(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); status.Text = "ShareSafe remains active in the notification area."; } };
            Monitor = new PromptMonitor(this, engine, prefs);
            source.AccessibleName = "Source input"; prepared.AccessibleName = "Prepared preview"; evidence.AccessibleName = "Graph and source evidence"; vault.AccessibleName = "Encrypted reviewed exports";
            RefreshEnvironmentChoices(); LoadEnvironment(); RefreshVault();
        }
        public async Task Initialize() {
            await engine.Start(); await engine.Apply(prefs.Active); Monitor.Start();
            status.Text = "Local engine ready. Closing this window keeps ShareSafe running in the tray.";
        }
        public void ExitApplication() { exiting = true; Monitor.Dispose(); Close(); }
        public void ReviewClipboard() { tabs.SelectedIndex = 0; source.Text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        public void PauseMonitoring() { monitoring.Checked = false; prefs.Monitoring = false; PrivateStore.Save(prefs); Monitor.Dismiss(); }
        static TextBox Editor(bool readOnly = false) { return new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, AcceptsReturn = true, AcceptsTab = true, ReadOnly = readOnly, WordWrap = true, Font = new Font("Consolas", 10) }; }
        TabPage Page(string name) { var page = new TabPage(name) { Padding = new Padding(12), BackColor = Color.White }; tabs.TabPages.Add(page); return page; }
        static FlowLayoutPanel Bar(int height = 48) { return new FlowLayoutPanel { Dock = DockStyle.Top, Height = height, Padding = new Padding(0, 5, 0, 5), WrapContents = true }; }
        Button ActionButton(string name, Func<Task> action) {
            var b = new Button { Text = name, AutoSize = true, Height = 32, Margin = new Padding(3) };
            b.Click += async delegate { await Run(action); }; return b;
        }
        async Task Run(Func<Task> action) {
            tabs.Enabled = false; UseWaitCursor = true; status.Text = "Working locally...";
            try { await action(); status.Text = "Local operation complete. Review the result before sharing."; }
            catch (Exception error) { status.Text = error.Message; MessageBox.Show(this, error.Message, "ShareSafe", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { if (!IsDisposed) { tabs.Enabled = true; UseWaitCursor = false; } }
        }
        static Label Note(string text, int height = 48) { return new Label { Text = text, Dock = DockStyle.Top, Height = height, Padding = new Padding(0, 5, 0, 5) }; }
        static SplitContainer Split(Control left, Control right, Orientation direction = Orientation.Vertical) { var split = new SplitContainer { Size = new Size(1000, 600), Dock = DockStyle.Fill, Orientation = direction }; split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right); return split; }
        void InvalidateReview() {
            generation++; session = null; analysis = null; prepared.Clear(); evidence.Clear(); findings.Rows.Clear(); manualBoxes.Clear(); imageReviewed = false; acknowledge.Checked = false;
        }
        void InvalidateProject() { projectId = null; plan = null; projectReviewed.Checked = false; projectFiles.Items.Clear(); projectText.Clear(); projectReport.Clear(); }
        void ReviewPage() {
            var page = Page("Review"); var bar = Bar(88);
            format.Items.AddRange(new object[] { "txt", "docx", "pdf" }); format.SelectedIndex = 0;
            bar.Controls.AddRange(new Control[] {
                ActionButton("Import file", Import), ActionButton("Paste clipboard", delegate { source.Text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; return Task.FromResult(0); }),
                ActionButton("Analyze", Analyze), ActionButton("Prepare preview", Preview), ActionButton("Review image", ImageReview),
                format, ActionButton("Export reviewed copy", delegate { return Export(false); }), ActionButton("Save to vault", delegate { return Export(true); }),
                ActionButton("Clear review", async delegate { if (session != null) await engine.Request("/v1/forget", new { session_id = session }); changing = true; source.Clear(); imported = null; changing = false; InvalidateReview(); }), acknowledge, optimize
            });
            foreach (var column in new[] { "Category", "Action", "Value", "Reason" }) findings.Columns.Add(new DataGridViewTextBoxColumn { Name = column, HeaderText = column, ReadOnly = true });
            findings.Columns.Insert(0, new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "Apply", FillWeight = 35 });
            var editors = Split(source, prepared); editors.SplitterDistance = 480;
            var detail = new TabControl { Dock = DockStyle.Fill }; var f = new TabPage("Findings"); f.Controls.Add(findings); detail.TabPages.Add(f);
            var e = new TabPage("Graph and evidence"); var qbar = Bar(); qbar.Controls.AddRange(new Control[] { question, ActionButton("Ask source", async delegate {
                RequireReview(); evidence.Text = Pretty(await engine.Request("/v1/query", new { session_id = session, question = question.Text, use_model = false }));
            }) }); e.Controls.Add(evidence); e.Controls.Add(qbar); detail.TabPages.Add(e);
            var split = Split(editors, detail, Orientation.Horizontal); split.SplitterDistance = 340;
            page.Controls.Add(split); page.Controls.Add(bar);
        }
        async Task Import() {
            using (var dialog = new OpenFileDialog { Filter = "Supported inputs|*.txt;*.md;*.csv;*.json;*.docx;*.pdf;*.png;*.jpg;*.jpeg;*.webp" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (new FileInfo(dialog.FileName).Length > 12 * 1024 * 1024) throw new InvalidOperationException("Input exceeds 12 MiB.");
                imported = File.ReadAllBytes(dialog.FileName); importedName = Path.GetFileName(dialog.FileName);
                changing = true; source.Text = "Imported: " + importedName + "\r\nChoose Analyze to extract and review this file."; changing = false; InvalidateReview();
                // OCR uses a normalized temporary image; it never changes the source file.
                importedPath = dialog.FileName;
            }
            await Task.FromResult(0);
        }
        string importedPath;
        async Task Analyze() {
            InvalidateReview(); var current = generation;
            var body = new Dictionary<string, object> { { "policy", Json.Read(policy.Text) } };
            if (imported != null) {
                body["file"] = Convert.ToBase64String(imported); body["name"] = importedName;
                if (new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(importedName).ToLowerInvariant())) {
                    try { body["ocr"] = await WindowsOcr.Read(imported, AppDomain.CurrentDomain.BaseDirectory); }
                    catch (Exception) { /* The engine requires manual full-image review when Windows OCR is unavailable. */ }
                }
            } else body["text"] = source.Text;
            var result = await engine.Request("/v1/analyze", body);
            if (generation != current) return;
            analysis = result; session = Json.Str(result, "session_id"); changing = true; source.Text = Json.Str(result, "text"); changing = false;
            foreach (Dictionary<string, object> finding in Json.Array(result, "findings")) {
                var row = findings.Rows[findings.Rows.Add(true, Json.Str(finding, "category"), Json.Str(finding, "action"), Json.Str(finding, "value"), Json.Str(finding, "reason"))]; row.Tag = Json.Str(finding, "id");
            }
            evidence.Text = Pretty(result);
        }
        static string Pretty(object obj) { // JSON remains complete and copyable for source/graph inspection.
            return Json.Write(obj);
        }
        void RequireReview() { if (session == null) throw new InvalidOperationException("Analyze the current input first. Changes invalidate the previous review."); }
        Dictionary<string, object> ReviewBody() {
            RequireReview(); var decisions = new Dictionary<string, bool>();
            foreach (DataGridViewRow row in findings.Rows) decisions[(string)row.Tag] = Convert.ToBoolean(row.Cells["Apply"].Value);
            return new Dictionary<string, object> { { "session_id", session }, { "decisions", decisions }, { "acknowledged", acknowledge.Checked }, { "optimize", optimize.Checked }, { "format", format.Text }, { "image_reviewed", imageReviewed }, { "boxes", manualBoxes } };
        }
        async Task Preview() { prepared.Text = Json.Str(await engine.Request("/v1/preview", ReviewBody()), "text"); }
        async Task ImageReview() {
            RequireReview(); if (Json.Str(analysis, "kind") != "image") throw new InvalidOperationException("Import and analyze an image first.");
            var selected = new HashSet<string>();
            foreach (DataGridViewRow row in findings.Rows) if (Convert.ToBoolean(row.Cells["Apply"].Value) && new[] { "mask", "generalize" }.Contains(Convert.ToString(row.Cells["Action"].Value))) selected.Add((string)row.Tag);
            var boxes = Json.Array(analysis, "boxes").Cast<Dictionary<string, object>>().Where(x => selected.Contains(Json.Str(x, "finding_id"))).Cast<object>().ToArray();
            using (var review = new ImageReviewWindow(Convert.FromBase64String(Json.Str(analysis, "image")), boxes, manualBoxes)) {
                if (review.ShowDialog(this) == DialogResult.OK) { manualBoxes = review.Boxes; imageReviewed = true; }
            }
            await Task.FromResult(0);
        }
        async Task Export(bool toVault) {
            var result = await engine.Request("/v1/export", ReviewBody());
            var bytes = Convert.FromBase64String(Json.Str(result, "data")); var name = Json.Str(result, "name");
            if (toVault) {
                var directory = Path.Combine(PrivateStore.Root, "vault"); PrivateStore.SecureDirectory(directory);
                PrivateStore.SaveBytes(Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N") + ".dpapi"), System.Text.Encoding.UTF8.GetBytes(Json.Write(new { name = name, data = Convert.ToBase64String(bytes) })));
                RefreshVault();
            } else SaveFile(bytes, name);
        }
        void SaveFile(byte[] bytes, string name) { using (var dialog = new SaveFileDialog { FileName = name, OverwritePrompt = true }) if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(dialog.FileName, bytes); }

        void EnvironmentsPage() {
            var page = Page("Environments"); var bar = Bar(90);
            environments.SelectedIndexChanged += delegate { if (!changing && environments.SelectedItem != null) environmentName.Text = environments.Text; };
            bar.Controls.AddRange(new Control[] { environments, ActionButton("Activate", async delegate {
                var profile = prefs.Environments.Find(x => x.Name == environments.Text); if (profile == null) return;
                await engine.Apply(profile); prefs.ActiveEnvironment = profile.Name; PrivateStore.Save(prefs); LoadEnvironment(); Monitor.Dismiss(); InvalidateReview(); InvalidateProject();
            }), environmentName, ActionButton("Create environment", async delegate {
                var name = environmentName.Text.Trim(); if (name.Length < 1 || name.Length > 60 || prefs.Environments.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Use a unique environment name with 1 to 60 characters.");
                var profile = new EnvironmentProfile { Name = name }; await engine.Apply(profile); prefs.Environments.Add(profile); prefs.ActiveEnvironment = name; PrivateStore.Save(prefs); RefreshEnvironmentChoices(); LoadEnvironment(); InvalidateReview(); InvalidateProject();
            }), ActionButton("Save active environment", async delegate {
                var candidate = new EnvironmentProfile { Name = prefs.Active.Name, Policy = policy.Text, Processes = processes.Text, ProjectGlossary = glossary.Text };
                Json.Read(candidate.ProjectGlossary); await engine.Apply(candidate);
                var active = prefs.Active; active.Policy = candidate.Policy; active.Processes = candidate.Processes; active.ProjectGlossary = candidate.ProjectGlossary;
                PrivateStore.Save(prefs); InvalidateReview(); InvalidateProject(); Monitor.Dismiss();
            }) });
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            content.Controls.Add(Note("Review policy (JSON). Actions: allow, warn, mask, generalize, block. Add private terms, allowed terms, readable replacements and optional local-model review.", 80), 0, 0);
            content.Controls.Add(Note("Desktop applications to monitor: one exact executable name per line, for example Claude.exe. Browsers use the extension. Only enable apps whose text you want reviewed.", 80), 1, 0);
            content.Controls.Add(policy, 0, 1); content.Controls.Add(processes, 1, 1);
            content.Controls.Add(Note("Project glossary (JSON): private source terms mapped to safe code identifiers, for example {\"PrivateClient\":\"GenericClient\"}.", 65), 0, 2);
            content.Controls.Add(Note("An environment is a saved privacy profile. Actual execution isolation is created separately in Agent Workspace and Sandbox.", 65), 1, 2);
            content.Controls.Add(glossary, 0, 3);
            page.Controls.Add(content); page.Controls.Add(bar);
        }
        void RefreshEnvironmentChoices() { changing = true; environments.Items.Clear(); foreach (var item in prefs.Environments) environments.Items.Add(item.Name); environments.SelectedItem = prefs.ActiveEnvironment; changing = false; }
        void LoadEnvironment() {
            changing = true; policy.Text = prefs.Active.Policy; processes.Text = prefs.Active.Processes; glossary.Text = prefs.Active.ProjectGlossary; environmentName.Text = prefs.Active.Name; monitoring.Checked = prefs.Monitoring; changing = false;
            Text = "ShareSafe for Windows - " + prefs.Active.Name;
        }

        void ProjectPage() {
            var page = Page("Agent Workspace"); var bar = Bar(100); var browse = ActionButton("Choose project folder", delegate {
                using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; return Task.FromResult(0);
            });
            folder.Dock = DockStyle.None; folder.Width = 500;
            bar.Controls.AddRange(new Control[] { folder, browse, ActionButton("Prepare separate copy", async delegate {
                InvalidateProject(); plan = await engine.Request("/v1/project/prepare", new { source = folder.Text, prompt = task.Text, glossary = Json.Read(glossary.Text) }); projectId = Json.Str(plan, "project_id");
                foreach (Dictionary<string, object> file in Json.Array(plan, "files")) projectFiles.Items.Add(Json.Str(file, "path"));
                projectReport.Text = Pretty(plan); if (projectFiles.Items.Count > 0) projectFiles.SelectedIndex = 0;
            }), projectReviewed, ActionButton("Export reviewed ZIP", async delegate {
                RequireProject(); var result = await engine.Request("/v1/project/export", new { project_id = projectId, reviewed = projectReviewed.Checked }); SaveFile(Convert.FromBase64String(Json.Str(result, "data")), Json.Str(result, "name"));
            }), ActionButton("Create sandbox", async delegate {
                RequireProject(); var result = await engine.Request("/v1/workspace/create", new { project_id = projectId, reviewed = projectReviewed.Checked }); sandboxOutput.Text = Pretty(result); InvalidateProject(); await RefreshWorkspaces(); tabs.SelectedIndex = 3;
            }) });
            task.Text = "Review this prepared project and report the checks you actually ran.";
            var left = new Panel { Dock = DockStyle.Fill }; left.Controls.Add(task); left.Controls.Add(Note("Agent task. Private terms are replaced consistently in files, paths and this task. Original project files are never modified.", 60));
            var right = new Panel { Dock = DockStyle.Fill }; var filesBar = Bar(); filesBar.Controls.Add(projectFiles); right.Controls.Add(projectText); right.Controls.Add(filesBar);
            projectFiles.SelectedIndexChanged += delegate {
                if (plan == null) return;
                var file = Json.Array(plan, "files").Cast<Dictionary<string, object>>().FirstOrDefault(x => Json.Str(x, "path") == projectFiles.Text); if (file != null) projectText.Text = Json.Str(file, "text");
            };
            var upper = Split(left, right); var split = Split(upper, projectReport, Orientation.Horizontal); split.SplitterDistance = 330;
            page.Controls.Add(split); page.Controls.Add(bar);
        }
        void RequireProject() { if (projectId == null) throw new InvalidOperationException("Prepare and review a project first. Changes invalidate the prepared plan."); }
        void SandboxPage() {
            var page = Page("Sandbox"); var bar = Bar(115);
            bar.Controls.AddRange(new Control[] { workspaces, ActionButton("Refresh sandboxes", RefreshWorkspaces), ActionButton("Verify", async delegate { sandboxOutput.Text = Pretty(await engine.Request("/v1/workspace/verify", new { workspace_id = WorkspaceId() })); }), command,
                ActionButton("Run command in container", async delegate { sandboxOutput.Text = Pretty(await engine.Request("/v1/workspace/run", new { workspace_id = WorkspaceId(), argv = Json.Serializer().DeserializeObject(command.Text) })); }),
                ActionButton("Run local coding agent", async delegate { var model = Json.Read(policy.Text); sandboxOutput.Text = Pretty(await engine.Request("/v1/workspace/agent", new { workspace_id = WorkspaceId(), model = model.ContainsKey("model") ? model["model"] : "qwen2.5:3b" })); }),
                ActionButton("Export reviewed sandbox result", async delegate {
                    if (MessageBox.Show(this, "Have you reviewed the resulting sandbox files and command output?", "Review sandbox result", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    var result = await engine.Request("/v1/workspace/export", new { workspace_id = WorkspaceId(), reviewed = true }); SaveFile(Convert.FromBase64String(Json.Str(result, "data")), Json.Str(result, "name"));
                }),
                ActionButton("Delete sandbox", async delegate { var id = WorkspaceId(); if (MessageBox.Show(this, "Delete this prepared copy and its private verification record?", "Delete sandbox", MessageBoxButtons.YesNo) != DialogResult.Yes) return; await engine.Request("/v1/workspace/delete", new { workspace_id = id, confirmed = true }); await RefreshWorkspaces(); }),
                ActionButton("Engine, Docker and model status", async delegate { sandboxOutput.Text = Pretty(await engine.Request("/v1/status")); }) });
            page.Controls.Add(sandboxOutput); page.Controls.Add(Note("Docker Desktop must run Linux containers and have sharesafe-agent:local built. Commands are argument arrays. No host-shell fallback. The container has no network and mounts only the reviewed copy.", 60)); page.Controls.Add(bar);
        }
        string WorkspaceId() { if (workspaces.SelectedItem == null) throw new InvalidOperationException("Create or select a sandbox first."); return workspaces.Text; }
        async Task RefreshWorkspaces() {
            var result = await engine.Request("/v1/workspaces"); workspaces.Items.Clear(); foreach (Dictionary<string, object> workspace in Json.Array(result, "workspaces")) workspaces.Items.Add(Json.Str(workspace, "id"));
            if (workspaces.Items.Count > 0) workspaces.SelectedIndex = 0; sandboxOutput.Text = Pretty(result);
        }
        void VaultPage() {
            var page = Page("Vault"); var bar = Bar(); bar.Controls.AddRange(new Control[] { ActionButton("Refresh vault", delegate { RefreshVault(); return Task.FromResult(0); }), ActionButton("Decrypt and save copy", delegate {
                var path = VaultPath(); var result = Json.Read(System.Text.Encoding.UTF8.GetString(PrivateStore.LoadBytes(path))); SaveFile(Convert.FromBase64String(Json.Str(result, "data")), Json.Str(result, "name")); return Task.FromResult(0);
            }), ActionButton("Delete vault entry", delegate {
                var path = VaultPath(); if (MessageBox.Show(this, "Permanently delete this encrypted export?", "Delete vault entry", MessageBoxButtons.YesNo) == DialogResult.Yes) { File.Delete(path); RefreshVault(); } return Task.FromResult(0);
            }) }); page.Controls.Add(vault); page.Controls.Add(Note("Reviewed exports are encrypted with Windows DPAPI for this Windows account. Decrypting creates a normal file at the location you choose. Other applications running as you remain inside the same trust boundary.", 60)); page.Controls.Add(bar);
        }
        void RefreshVault() { vault.Items.Clear(); var directory = Path.Combine(PrivateStore.Root, "vault"); if (Directory.Exists(directory)) foreach (var path in Directory.GetFiles(directory, "*.dpapi").OrderByDescending(x => x)) vault.Items.Add(Path.GetFileName(path)); }
        string VaultPath() { if (vault.SelectedItem == null) throw new InvalidOperationException("Select a vault entry."); return Path.Combine(PrivateStore.Root, "vault", Path.GetFileName(vault.Text)); }
        void ActivityPage() {
            var page = Page("Activity"); var bar = Bar(); bar.Controls.AddRange(new Control[] { ActionButton("Refresh activity", async delegate { activity.Text = Pretty(await engine.Request("/v1/audit")); }), ActionButton("Clear in-memory reviews", async delegate { await engine.Request("/v1/clear", new { }); InvalidateReview(); InvalidateProject(); }) });
            page.Controls.Add(activity); page.Controls.Add(Note("Local audit entries record operation types and counts. They do not record source prompts or original values. Clearing reviews does not delete sandbox files or vault entries.", 60)); page.Controls.Add(bar);
        }
        void SettingsPage() {
            var page = Page("Settings"); var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            layout.Controls.Add(new Label { Text = "Background operation", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            layout.Controls.Add(monitoring);
            monitoring.CheckedChanged += delegate { if (changing) return; prefs.Monitoring = monitoring.Checked; PrivateStore.Save(prefs); if (!prefs.Monitoring) Monitor.Dismiss(); };
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) login.Checked = key != null && key.GetValue("ShareSafeWindows") != null;
            login.CheckedChanged += delegate {
                try { using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (login.Checked) key.SetValue("ShareSafeWindows", Engine.Quote(Application.ExecutablePath) + " --background"); else key.DeleteValue("ShareSafeWindows", false); } }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Startup setting"); }
            };
            layout.Controls.Add(login);
            layout.Controls.Add(new Label { Text = "Closing the window leaves the tray app and engine running. Use the tray menu to Quit. Desktop overlays start only after you enable monitoring and save allowed applications in the active environment.", Size = new Size(900, 70) });
            layout.Controls.Add(ActionButton("Copy browser pairing token", delegate { Clipboard.SetText(prefs.BrowserToken); return Task.FromResult(0); }));
            layout.Controls.Add(new Label { Text = "Chrome / Edge: load the BrowserExtension folder as an unpacked extension, then paste the pairing token into its popup. Enable each additional HTTPS site explicitly. Browser pairing can review prompts only; it cannot read host projects or control sandboxes.", Size = new Size(900, 85) });
            layout.Controls.Add(ActionButton("Check local dependencies", async delegate { activity.Text = Pretty(await engine.Request("/v1/status")); tabs.SelectedIndex = 5; }));
            layout.Controls.Add(new Label { Text = "Environment changes must be saved to apply to browser and app overlays. Inline reviews use local rules and graph checks. Full document review can request Ollama. Nothing is sent to cloud AI by ShareSafe.", Size = new Size(900, 75) });
            page.Controls.Add(layout);
        }
    }
}
