using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareSafe.Windows {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static int Main(string[] args) {
            SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            if (args.Length > 1 && args[0] == "--self-test") return SelfTest(root, args[1]).GetAwaiter().GetResult();
            if (args.Length > 1 && args[0] == "--data-dir") PrivateStore.Root = Path.GetFullPath(args[1]);
            var name = "Local\\ShareSafeWindows-" + WindowsIdentity.GetCurrent().User.Value;
            bool created;
            using (var single = new Mutex(true, name, out created)) using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show")) {
                if (!created) { wake.Set(); return 0; }
                try {
                    var prefs = PrivateStore.Load(); PrivateStore.Save(prefs);
                    using (var engine = new Engine(root, prefs)) using (var context = new TrayContext(engine, prefs, Array.IndexOf(args, "--background") >= 0, wake)) Application.Run(context);
                    return 0;
                } catch (Exception error) { MessageBox.Show(error.Message, "ShareSafe could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
        }
        static async Task<int> SelfTest(string root, string reportPath) {
            var checks = new List<string>();
            PrivateStore.Root = Path.Combine(root, "tests", "tmp", "windows-native-" + Guid.NewGuid().ToString("N"));
            try {
                var prefs = PrivateStore.Load(); PrivateStore.Save(prefs); var reloaded = PrivateStore.Load();
                if (prefs.NativeToken != reloaded.NativeToken || prefs.NativeToken == prefs.BrowserToken) throw new Exception("Credential round trip failed"); checks.Add("DPAPI settings round trip and distinct bearer tokens");
                var vault = Path.Combine(PrivateStore.Root, "export.dpapi"); var value = System.Text.Encoding.UTF8.GetBytes("synthetic reviewed export"); PrivateStore.SaveBytes(vault, value);
                if (System.Text.Encoding.UTF8.GetString(PrivateStore.LoadBytes(vault)) != "synthetic reviewed export") throw new Exception("Vault round trip failed"); checks.Add("DPAPI vault round trip");
                if (File.ReadAllText(vault).Contains("synthetic reviewed export")) throw new Exception("Vault plaintext leak"); checks.Add("Encrypted file excludes source plaintext");
                prefs.Environments.Add(new EnvironmentProfile { Name = "Work", Processes = "SyntheticEditor.exe" }); prefs.ActiveEnvironment = "Work"; PrivateStore.Save(prefs);
                if (PrivateStore.Load().Active.Name != "Work") throw new Exception("Environment persistence failed"); checks.Add("Named environment persistence");
                using (var engine = new Engine(root, prefs)) {
                    await engine.Start(); checks.Add("Hidden engine starts and authenticates"); await engine.Apply(prefs.Active);
                    var review = await engine.Request("/v1/analyze", new { text = "Contact alice@example.org", policy = new { } });
                    var result = await engine.Request("/v1/preview", new { session_id = Json.Str(review, "session_id") });
                    if (Json.Str(result, "text").Contains("alice@example.org")) throw new Exception("Redaction failed"); checks.Add("Native transport analyzes and redacts");
                    try { await engine.Request("/v1/project/prepare", new { source = root }, true); throw new Exception("Browser token escaped scope"); } catch (InvalidOperationException) { checks.Add("Browser token cannot prepare host projects"); }
                }
                checks.Add("Graceful engine shutdown completed");
                File.WriteAllText(reportPath, Json.Write(new { passed = true, checks = checks, data_directory = PrivateStore.Root, utc = DateTime.UtcNow.ToString("o") })); return 0;
            } catch (Exception error) { File.WriteAllText(reportPath, Json.Write(new { passed = false, checks = checks, error = error.ToString() })); return 1; }
        }
    }
    sealed class TrayContext : ApplicationContext {
        readonly MainWindow window; readonly NotifyIcon tray; readonly RegisteredWaitHandle listener;
        public TrayContext(Engine engine, Preferences prefs, bool background, EventWaitHandle wake) {
            window = new MainWindow(engine, prefs); var handle = window.Handle;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open ShareSafe", null, delegate { Show(); });
            menu.Items.Add("Review clipboard", null, delegate { Show(); window.ReviewClipboard(); });
            menu.Items.Add("Pause desktop overlays", null, delegate { window.PauseMonitoring(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Quit ShareSafe", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Shield, Text = "ShareSafe - local privacy review", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Show(); };
            listener = ThreadPool.RegisterWaitForSingleObject(wake, delegate { try { window.BeginInvoke((Action)Show); } catch (InvalidOperationException) { } }, null, Timeout.Infinite, false);
            if (!background) Show();
            var once = new System.Windows.Forms.Timer { Interval = 100 };
            once.Tick += async delegate { once.Stop(); once.Dispose(); try { await window.Initialize(); } catch (Exception error) { Show(); MessageBox.Show(window, error.Message, "ShareSafe engine unavailable"); } }; once.Start();
        }
        void Show() { window.Show(); window.WindowState = FormWindowState.Normal; window.Activate(); }
        protected override void ExitThreadCore() { listener.Unregister(null); tray.Visible = false; tray.Dispose(); window.ExitApplication(); window.Dispose(); base.ExitThreadCore(); }
    }
}
