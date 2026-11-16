using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace ShareSafe.Windows {
    public sealed class PromptMonitor : IDisposable {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        readonly MainWindow owner; readonly Engine engine; readonly Preferences prefs;
        volatile bool stopped; Thread worker; Overlay panel;
        string previous = "", shown = ""; DateTime stable, snooze;
        public PromptMonitor(MainWindow window, Engine bridge, Preferences settings) { owner = window; engine = bridge; prefs = settings; }
        public void Start() { worker = new Thread(Poll) { IsBackground = true, Name = "ShareSafe UI Automation" }; worker.SetApartmentState(ApartmentState.STA); worker.Start(); }
        void Post(Action action) { if (!stopped && owner.IsHandleCreated) try { owner.BeginInvoke(action); } catch (InvalidOperationException) { } }
        void Poll() {
            while (!stopped) {
                Thread.Sleep(600);
                try {
                    if (!prefs.Monitoring || !engine.Ready) { Post(Dismiss); continue; }
                    uint pid; var foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out pid);
                    if (pid == Process.GetCurrentProcess().Id) continue;
                    var profile = prefs.Active; var process = Process.GetProcessById((int)pid);
                    var allowed = profile.Processes.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
                    if (!allowed.Contains(process.ProcessName + ".exe", StringComparer.OrdinalIgnoreCase)) { Post(Dismiss); continue; }
                    // Chromium is handled by the scoped extension, not broad UIA observation.
                    if (new[] { "chrome", "msedge", "firefox", "brave" }.Contains(process.ProcessName.ToLowerInvariant())) { Post(Dismiss); continue; }
                    var element = AutomationElement.FocusedElement;
                    if (element == null || element.Current.ProcessId != pid || element.Current.IsPassword || !element.Current.IsEnabled || element.Current.IsOffscreen || (element.Current.ControlType != ControlType.Edit && element.Current.ControlType != ControlType.Document)) { Post(Dismiss); continue; }
                    object pattern; string draft = null;
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) draft = ((ValuePattern)pattern).Current.Value;
                    else if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) draft = ((TextPattern)pattern).DocumentRange.GetText(200001);
                    if (draft == null || draft.Length < 8 || draft.Length > 200000) { Post(Dismiss); continue; }
                    var identity = pid + ":" + string.Join(".", element.GetRuntimeId()) + ":" + profile.Name + ":" + profile.Policy + ":" + draft;
                    if (identity != previous) { previous = identity; stable = DateTime.UtcNow; Post(Dismiss); continue; }
                    if ((DateTime.UtcNow - stable).TotalSeconds < 1 || identity == shown || DateTime.UtcNow < snooze) continue;
                    shown = identity; var captured = draft; var capturedProfile = profile.Name + profile.Policy;
                    var rect = element.Current.BoundingRectangle;
                    Post(delegate {
                        if (!prefs.Monitoring || panel != null) return;
                        panel = new Overlay(engine, element, captured, capturedProfile, prefs, foreground, (int)pid);
                        var area = Screen.FromPoint(new Point((int)rect.Left, (int)rect.Top)).WorkingArea;
                        panel.Location = new Point(Math.Max(area.Left, Math.Min((int)rect.Right - panel.Width, area.Right - panel.Width)), Math.Max(area.Top, Math.Min((int)rect.Bottom + 8, area.Bottom - panel.Height)));
                        panel.FormClosed += delegate { panel = null; snooze = DateTime.UtcNow.AddSeconds(15); };
                        panel.Show();
                    });
                } catch (Exception) { /* A provider may disappear or deny access; never scrape another app as fallback. */ }
            }
        }
        public void Dismiss() { if (panel != null) panel.Close(); }
        public void Dispose() { stopped = true; Dismiss(); }

        sealed class Overlay : Form {
            readonly TextBox clean = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            readonly Label message = new Label { AutoSize = false, Height = 58, Dock = DockStyle.Top, Text = "Review this draft locally before sending. Desktop monitoring is advisory and does not block Send." };
            public Overlay(Engine engine, AutomationElement target, string draft, string profile, Preferences prefs, IntPtr window, int pid) {
                Text = "ShareSafe · Review before you share"; TopMost = true; ShowInTaskbar = false; Size = new Size(490, 390); StartPosition = FormStartPosition.Manual; Font = Appearance.Body; Padding = new Padding(18);
                message.Text = "A quick privacy check for your draft. Nothing is sent automatically."; message.Height = 64;
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 100, Padding = new Padding(0, 12, 0, 0) };
                var review = new WarmButton { Text = "Review locally", Primary = true };
                var replace = new WarmButton { Text = "Replace draft", Primary = true, Enabled = false };
                var copy = new WarmButton { Text = "Copy prepared", Enabled = false };
                var close = new WarmButton { Text = "Not now" };
                bar.Controls.AddRange(new Control[] { review, replace, copy, close }); Controls.Add(clean); Controls.Add(message); Controls.Add(bar);
                review.Click += async delegate {
                    review.Enabled = false;
                    try {
                        var result = await engine.Request("/v1/browser/review", new { text = draft }, true);
                        if (IsDisposed) return;
                        if (prefs.Active.Name + prefs.Active.Policy != profile) throw new InvalidOperationException("The environment changed. Review the latest draft again.");
                        clean.Text = Json.Str(result, "text"); replace.Enabled = copy.Enabled = true; message.Text = "Review the prepared text. Replacement never sends the prompt.";
                    } catch (Exception error) { if (!IsDisposed) message.Text = error.Message; }
                    finally { if (!IsDisposed) review.Enabled = true; }
                };
                replace.Click += delegate {
                    try {
                        uint currentPid; var foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out currentPid);
                        if (!prefs.Monitoring || prefs.Active.Name + prefs.Active.Policy != profile || !IsWindow(window) || target.Current.ProcessId != pid || target.Current.IsPassword || (currentPid != Process.GetCurrentProcess().Id && foreground != window)) throw new InvalidOperationException("The target or environment changed. Review again.");
                        object value;
                        if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out value) || ((ValuePattern)value).Current.IsReadOnly) throw new InvalidOperationException("This editor does not support direct replacement. Copy the prepared draft and paste it yourself.");
                        var pattern = (ValuePattern)value;
                        if (pattern.Current.Value != draft) throw new InvalidOperationException("The draft changed. Review the latest prompt again.");
                        pattern.SetValue(clean.Text);
                        if (pattern.Current.Value != clean.Text) throw new InvalidOperationException("The editor did not accept the replacement. Use Copy prepared.");
                        Close();
                    } catch (Exception error) { message.Text = error.Message; replace.Enabled = false; }
                };
                copy.Click += delegate { Clipboard.SetText(clean.Text); message.Text = "Prepared text copied. Paste and review it before sending."; };
                close.Click += delegate { Close(); };
                Appearance.Apply(this);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
        }
    }
}
