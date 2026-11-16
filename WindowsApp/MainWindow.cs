using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShareSafe.Windows {
    public sealed partial class MainWindow : Form {
        readonly Engine engine; readonly Preferences prefs;
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(12), Text = "Starting local engine..." };
        readonly TextBox source = Editor(), prepared = Editor(true), evidence = Editor(true), policy = Editor(), processes = Editor(), glossary = Editor();
        readonly DataGridView findings = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        readonly CheckBox acknowledge = new CheckBox { Text = "I reviewed any retained risks", AutoSize = true };
        readonly CheckBox optimize = new CheckBox { Text = "Add grounding instructions", AutoSize = true };
        readonly ComboBox format = new WarmComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        readonly ComboBox environments = new WarmComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        readonly TextBox environmentName = new TextBox { Width = 180 };
        readonly CheckBox monitoring = new CheckBox { Text = "Enable desktop prompt overlays", AutoSize = true };
        readonly CheckBox login = new CheckBox { Text = "Start in the background when I sign in", AutoSize = true };
        readonly TextBox folder = new TextBox { Dock = DockStyle.Fill }, task = Editor(), projectText = Editor(true), projectReport = Editor(true);
        readonly ComboBox projectFiles = new WarmComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        readonly CheckBox projectReviewed = new CheckBox { Text = "I reviewed every prepared file, prompt and exclusion report", AutoSize = true };
        readonly ComboBox workspaces = new WarmComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
        readonly TextBox command = new TextBox { Text = "[\"python\",\"--version\"]", Width = 390 };
        readonly TextBox sandboxOutput = Editor(true), activity = Editor(true), question = new TextBox { Width = 350 };
        readonly ListBox vault = new WarmListBox { Dock = DockStyle.Fill };
        string session, projectId, importedName; byte[] imported;
        Dictionary<string, object> analysis, plan;
        KnowledgeWindow knowledgeWindow;
        List<object> manualBoxes = new List<object>(); bool imageReviewed, changing, exiting; int generation;
        public PromptMonitor Monitor { get; private set; }
        public MainWindow(Engine bridge, Preferences settings) {
            engine = bridge; prefs = settings;
            Text = "ShareSafe for Windows"; Size = new Size(1180, 850); MinimumSize = new Size(900, 680); StartPosition = FormStartPosition.CenterScreen;
            Font = Appearance.Body; BackColor = Appearance.Eggshell; Icon = Appearance.CreateIcon();
            Controls.Add(tabs); Controls.Add(status);
            ReviewPage(); EnvironmentsPage(); ProjectPage(); SandboxPage(); VaultPage(); ActivityPage(); SettingsPage();
            source.TextChanged += delegate { if (!changing) { imported = null; importedName = null; InvalidateReview(); } };
            policy.TextChanged += delegate { if (!changing) InvalidateReview(); };
            acknowledge.CheckedChanged += delegate { prepared.Clear(); };
            optimize.CheckedChanged += delegate { prepared.Clear(); };
            findings.CellValueChanged += delegate { prepared.Clear(); imageReviewed = false; acknowledge.Checked = false; RefreshWorkflow(); };
            findings.CurrentCellDirtyStateChanged += delegate { if (findings.IsCurrentCellDirty) findings.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            folder.TextChanged += delegate { InvalidateProject(); }; task.TextChanged += delegate { InvalidateProject(); }; glossary.TextChanged += delegate { if (!changing) InvalidateProject(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); status.Text = "ShareSafe remains active in the notification area."; } };
            Monitor = new PromptMonitor(this, engine, prefs);
            source.AccessibleName = "Source input"; prepared.AccessibleName = "Prepared preview"; evidence.AccessibleName = "Graph and source evidence"; vault.AccessibleName = "Encrypted reviewed exports";
            RefreshEnvironmentChoices(); LoadEnvironment(); RefreshVault();
            BuildShell(); Appearance.RoundInputs(this); Appearance.Apply(this); RefreshWorkflow();
        }
        public async Task Initialize() {
            await engine.Start(); await engine.Apply(prefs.Active); Monitor.Start();
            status.Text = "Local engine ready. Closing this window keeps ShareSafe running in the tray.";
            RefreshWorkflow();
        }
        public void ExitApplication() { exiting = true; if(knowledgeWindow!=null)knowledgeWindow.Close(); Monitor.Dispose(); Close(); }
        public void ReviewClipboard() { tabs.SelectedIndex = 0; source.Text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        public void PauseMonitoring() { monitoring.Checked = false; prefs.Monitoring = false; PrivateStore.Save(prefs); Monitor.Dismiss(); }
        static TextBox Editor(bool readOnly = false) { return new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AcceptsTab = true, ReadOnly = readOnly, WordWrap = true, Font = Appearance.Body }; }
        TabPage Page(string name) { var page = new TabPage(name) { Padding = new Padding(20), BackColor = Appearance.Eggshell }; tabs.TabPages.Add(page); return page; }
        static FlowLayoutPanel Bar(int height = 58) { return new FlowLayoutPanel { Dock = DockStyle.Top, Height = height, Padding = new Padding(0, 8, 0, 4), WrapContents = true }; }
        Button ActionButton(string name, Func<Task> action) {
            var b = new WarmButton { Text = name, Name = name, Font = Appearance.Body, Primary = name == "Check for sensitive information" || name == "Create safe preview" || name == "Save a copy" || name == "Save environment" || name == "Prepare safe copy" };
            b.Click += async delegate { await Run(action); }; return b;
        }
        async Task Run(Func<Task> action) {
            tabs.Enabled = false; if (navigation != null) navigation.Enabled = false; UseWaitCursor = true; status.Text = "Working on your device...";
            try { await action(); if (status.Text == "Working on your device...") status.Text = "Done. Your changes are ready."; }
            catch (Exception error) { status.Text = "Please check: " + error.Message; }
            finally { if (!IsDisposed) { tabs.Enabled = true; if (navigation != null) navigation.Enabled = true; UseWaitCursor = false; RefreshWorkflow(); } }
        }
        static Label Note(string text, int height = 48) { return new Label { Text = text, Dock = DockStyle.Top, Height = height, Padding = new Padding(0, 5, 0, 5) }; }
        static SplitContainer Split(Control left, Control right, Orientation direction = Orientation.Vertical) { var split = new SplitContainer { Size = new Size(1000, 600), Dock = DockStyle.Fill, Orientation = direction }; split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right); return split; }
        void InvalidateReview() {
            if(knowledgeWindow!=null)knowledgeWindow.Close();
            generation++; session = null; analysis = null; prepared.Clear(); evidence.Clear(); findings.Rows.Clear(); manualBoxes.Clear(); imageReviewed = false; acknowledge.Checked = false;
            RefreshWorkflow();
        }
        void InvalidateProject() { projectId = null; plan = null; projectReviewed.Checked = false; projectFiles.Items.Clear(); projectText.Clear(); projectReport.Clear(); RefreshWorkflow(); }
        void ReviewPage() {
            var page = Page("Review"); var bar = Bar(132);
            format.Items.AddRange(new object[] { "txt", "docx", "pdf" }); format.SelectedIndex = 0;
            bar.Controls.AddRange(new Control[] {
                ActionButton("Choose a file", Import), ActionButton("Paste text", delegate { source.Text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; return Task.FromResult(0); }),
                ActionButton("Check for sensitive information", Analyze), ActionButton("Create safe preview", Preview), ActionButton("Review image", ImageReview),
                ActionButton("Start over", async delegate { if (session != null) await engine.Request("/v1/forget", new { session_id = session }); changing = true; source.Clear(); imported = null; changing = false; InvalidateReview(); }), ActionButton("Knowledge base",delegate { OpenKnowledgeBase(); return Task.FromResult(0); })
            });
            acknowledge.Text = "I accept the risks of anything I chose to keep"; optimize.Text = "Add instructions for a more grounded AI response";
            foreach (var column in new[] { "Category", "Action", "Value", "Reason" }) findings.Columns.Add(new DataGridViewTextBoxColumn { Name = column, HeaderText = column, ReadOnly = true });
            findings.Columns["Category"].HeaderText = "Information type"; findings.Columns["Action"].HeaderText = "Suggested change"; findings.Columns["Value"].HeaderText = "Found in your content"; findings.Columns["Reason"].HeaderText = "Why it was flagged";
            findings.Columns.Insert(0, new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "Change", FillWeight = 40 });
            var editors = Split(Appearance.Section("Your content", "Paste a prompt or choose a document or screenshot.", source), Appearance.Section("Your safe preview", "The exact content you will save appears here after review.", prepared)); editors.SplitterDistance = 480;
            var detail = new WarmTabs { Dock = DockStyle.Fill }; var f = new TabPage("Suggested changes"); f.Controls.Add(findings); detail.TabPages.Add(f);
            var evidenceTab = new TabPage("Why these changes?"); var qbar = Bar(); qbar.Controls.AddRange(new Control[] { question, ActionButton("Find source evidence", async delegate {
                RequireReview(); evidence.Text = Pretty(await engine.Request("/v1/query", new { session_id = session, question = question.Text, use_model = false }));
            }), ActionButton("Knowledge base", delegate { OpenKnowledgeBase(); return Task.FromResult(0); }) }); evidenceTab.Controls.Add(evidence); evidenceTab.Controls.Add(qbar); detail.TabPages.Add(evidenceTab);
            var split = Split(editors, detail, Orientation.Horizontal); split.SplitterDistance = 310;
            var save = Bar(106); save.Dock = DockStyle.Bottom;
            save.Controls.AddRange(new Control[] { acknowledge, optimize }); save.SetFlowBreak(optimize, true);
            save.Controls.AddRange(new Control[] { new Label { Text = "Save as", AutoSize = true, Padding = new Padding(0, 12, 8, 0) }, format, ActionButton("Save a copy", delegate { return Export(false); }), ActionButton("Keep in encrypted vault", delegate { return Export(true); }) });
            page.Controls.Add(split); page.Controls.Add(save); page.Controls.Add(bar);
            prepared.TextChanged += delegate { RefreshWorkflow(); };
            findings.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { if (e.RowIndex >= 0 && (e.ColumnIndex == findings.Columns["Category"].Index || e.ColumnIndex == findings.Columns["Action"].Index)) { e.Value = Friendly(Convert.ToString(e.Value)); e.FormattingApplied = true; } };
        }
        void OpenKnowledgeBase() {
            RequireReview();if(knowledgeWindow!=null&&!knowledgeWindow.IsDisposed){knowledgeWindow.Activate();return;}
            var graph=analysis.ContainsKey("graph")?analysis["graph"] as Dictionary<string,object>:null;
            knowledgeWindow=new KnowledgeWindow(graph??new Dictionary<string,object>(),Json.Str(analysis,"text"));knowledgeWindow.FormClosed+=delegate{knowledgeWindow=null;};if(previewMode){knowledgeWindow.StartPosition=FormStartPosition.Manual;knowledgeWindow.Location=new Point(-24000,-24000);}knowledgeWindow.Show(this);
        }
        async Task Import() {
            using (var dialog = new OpenFileDialog { Filter = "Supported inputs|*.txt;*.md;*.csv;*.json;*.docx;*.pdf;*.png;*.jpg;*.jpeg;*.webp" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (new FileInfo(dialog.FileName).Length > 12 * 1024 * 1024) throw new InvalidOperationException("Input exceeds 12 MiB.");
                imported = File.ReadAllBytes(dialog.FileName); importedName = Path.GetFileName(dialog.FileName);
                changing = true; source.Text = "File ready: " + importedName + "\r\n\r\nChoose Check for sensitive information to review this file."; changing = false; InvalidateReview();
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
                if (new[] { "block", "allow" }.Contains(Json.Str(finding, "action"))) { row.Cells["Apply"].ReadOnly = true; row.Cells["Apply"].ToolTipText = "This action is controlled by the active environment's policy."; }
            }
            evidence.Text = ReviewSummary(result); status.Text = findings.Rows.Count + " item(s) to review. Check the suggested changes, then create your safe preview.";
        }
        static string Pretty(object obj) { // JSON remains complete and copyable for source/graph inspection.
            return DisplayJson(obj);
        }
        void RequireReview() { if (session == null) throw new InvalidOperationException("Analyze the current input first. Changes invalidate the previous review."); }
        Dictionary<string, object> ReviewBody() {
            RequireReview(); var decisions = new Dictionary<string, bool>();
            foreach (DataGridViewRow row in findings.Rows) decisions[(string)row.Tag] = Convert.ToBoolean(row.Cells["Apply"].Value);
            return new Dictionary<string, object> { { "session_id", session }, { "decisions", decisions }, { "acknowledged", acknowledge.Checked }, { "optimize", optimize.Checked }, { "format", format.Text }, { "image_reviewed", imageReviewed }, { "boxes", manualBoxes } };
        }
        async Task Preview() { prepared.Text = Json.Str(await engine.Request("/v1/preview", ReviewBody()), "text"); status.Text = "Your preview is ready. Read it, then save a copy or store it in the vault."; }
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

        void EnvironmentsPage() { BuildEnvironmentPage(); }
        void RefreshEnvironmentChoices() { changing = true; environments.Items.Clear(); foreach (var item in prefs.Environments) environments.Items.Add(item.Name); environments.SelectedItem = prefs.ActiveEnvironment; changing = false; }
        void LoadEnvironment() {
            changing = true; policy.Text = prefs.Active.Policy; processes.Text = prefs.Active.Processes; glossary.Text = prefs.Active.ProjectGlossary; environmentName.Text = prefs.Active.Name; monitoring.Checked = prefs.Monitoring; changing = false;
            Text = "ShareSafe · " + prefs.Active.Name; LoadPolicyForm();
        }

        void ProjectPage() { BuildProjectPage(); }
        void RequireProject() { if (projectId == null) throw new InvalidOperationException("Prepare and review a project first. Changes invalidate the prepared plan."); }
        void SandboxPage() { BuildSandboxPage(); }
        string WorkspaceId() { if (workspaces.SelectedItem == null) throw new InvalidOperationException("Create or select a sandbox first."); return workspaces.Text; }
        async Task RefreshWorkspaces() {
            var result = await engine.Request("/v1/workspaces"); workspaces.Items.Clear(); foreach (Dictionary<string, object> workspace in Json.Array(result, "workspaces")) workspaces.Items.Add(Json.Str(workspace, "id"));
            if (workspaces.Items.Count > 0) workspaces.SelectedIndex = 0; sandboxOutput.Text = ExplainResult(result);
        }
        void VaultPage() {
            var page = Page("Vault"); var bar = Bar(); bar.Controls.AddRange(new Control[] { ActionButton("Refresh vault", delegate { RefreshVault(); return Task.FromResult(0); }), ActionButton("Save decrypted copy", delegate {
                var path = VaultPath(); var result = Json.Read(System.Text.Encoding.UTF8.GetString(PrivateStore.LoadBytes(path))); SaveFile(Convert.FromBase64String(Json.Str(result, "data")), Json.Str(result, "name")); return Task.FromResult(0);
            }), ActionButton("Delete vault entry", delegate {
                var path = VaultPath(); if (MessageBox.Show(this, "Permanently delete this encrypted export?", "Delete vault entry", MessageBoxButtons.YesNo) == DialogResult.Yes) { File.Delete(path); RefreshVault(); } return Task.FromResult(0);
            }) }); page.Controls.Add(vault); page.Controls.Add(Note("Reviewed exports are encrypted with Windows DPAPI for this Windows account. Decrypting creates a normal file at the location you choose. Other applications running as you remain inside the same trust boundary.", 60)); page.Controls.Add(bar);
        }
        void RefreshVault() { vault.Items.Clear(); var directory = Path.Combine(PrivateStore.Root, "vault"); if (Directory.Exists(directory)) foreach (var path in Directory.GetFiles(directory, "*.dpapi").OrderByDescending(x => x)) vault.Items.Add(new VaultItem { Path = path }); }
        string VaultPath() { if (vault.SelectedItem == null) throw new InvalidOperationException("Select a vault entry."); return ((VaultItem)vault.SelectedItem).Path; }
        void ActivityPage() {
            var page = Page("Activity"); var bar = Bar(); bar.Controls.AddRange(new Control[] { ActionButton("Refresh activity", async delegate { activity.Text = ActivitySummary(await engine.Request("/v1/audit")); }), ActionButton("Clear in-memory reviews", async delegate { await engine.Request("/v1/clear", new { }); InvalidateReview(); InvalidateProject(); }) });
            page.Controls.Add(activity); page.Controls.Add(Note("Local audit entries record operation types and counts. They do not record source prompts or original values. Clearing reviews does not delete sandbox files or vault entries.", 60)); page.Controls.Add(bar);
        }
        void SettingsPage() {
            var page = Page("Settings"); var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            layout.Controls.Add(new Label { Text = "Always within reach", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            layout.Controls.Add(monitoring);
            monitoring.CheckedChanged += delegate { if (changing) return; prefs.Monitoring = monitoring.Checked; PrivateStore.Save(prefs); if (!prefs.Monitoring) Monitor.Dismiss(); };
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) login.Checked = key != null && key.GetValue("ShareSafeWindows") != null; }
            catch (System.Security.SecurityException) { login.Enabled = false; login.Text = "Sign-in startup is managed by Windows"; }
            catch (UnauthorizedAccessException) { login.Enabled = false; login.Text = "Sign-in startup is managed by Windows"; }
            login.CheckedChanged += delegate {
                try { using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (login.Checked) key.SetValue("ShareSafeWindows", Engine.Quote(Application.ExecutablePath) + " --background"); else key.DeleteValue("ShareSafeWindows", false); } }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Startup setting"); }
            };
            layout.Controls.Add(login);
            layout.Controls.Add(new Label { Text = "Closing the window leaves the tray app and engine running. Use the tray menu to Quit. To get desktop suggestions, turn them on above and add applications in Environments.", Size = new Size(900, 70) });
            layout.Controls.Add(ActionButton("Copy browser pairing token", delegate { Clipboard.SetText(prefs.BrowserToken); return Task.FromResult(0); }));
            layout.Controls.Add(new Label { Text = "Chrome / Edge: load the BrowserExtension folder as an unpacked extension, then paste the pairing token into its popup. Enable each additional HTTPS site explicitly. Browser pairing can review prompts only; it cannot read host projects or control sandboxes.", Size = new Size(900, 85) });
            layout.Controls.Add(ActionButton("Check local dependencies", async delegate { activity.Text = Pretty(await engine.Request("/v1/status")); tabs.SelectedIndex = 5; }));
            layout.Controls.Add(new Label { Text = "Environment changes must be saved to apply to browser and app overlays. Inline reviews use local rules and graph checks. Full document review can request Ollama. Nothing is sent to cloud AI by ShareSafe.", Size = new Size(900, 75) });
            page.Controls.Add(layout);
        }
    }
}
