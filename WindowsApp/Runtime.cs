using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ShareSafe.Windows {
    public static class Json {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 36 * 1024 * 1024, RecursionLimit = 100 }; }
        public static string Write(object value) { return Serializer().Serialize(value); }
        public static Dictionary<string, object> Read(string value) { return Serializer().Deserialize<Dictionary<string, object>>(value); }
        public static string Str(Dictionary<string, object> obj, string key) { return obj.ContainsKey(key) ? Convert.ToString(obj[key]) : ""; }
        public static object[] Array(Dictionary<string, object> obj, string key) { return obj.ContainsKey(key) ? ((System.Collections.IEnumerable)obj[key]).CastArray() : new object[0]; }
        private static object[] CastArray(this System.Collections.IEnumerable seq) { var list = new List<object>(); foreach (var item in seq) list.Add(item); return list.ToArray(); }
    }

    public sealed class EnvironmentProfile {
        public string Name { get; set; }
        public string Policy { get; set; }
        public string Processes { get; set; }
        public string ProjectGlossary { get; set; }
        public EnvironmentProfile() { Name = "Personal"; Policy = "{\"actions\":{\"CREDENTIAL\":\"block\"},\"confidential_terms\":[],\"allow_terms\":[],\"replacements\":{},\"use_model\":false,\"model\":\"qwen2.5:3b\"}"; Processes = ""; ProjectGlossary = "{}"; }
    }
    public sealed class Preferences {
        public string NativeToken { get; set; }
        public string BrowserToken { get; set; }
        public bool Monitoring { get; set; }
        public string ActiveEnvironment { get; set; }
        public List<EnvironmentProfile> Environments { get; set; }
        public Preferences() { NativeToken = Token(); BrowserToken = Token(); ActiveEnvironment = "Personal"; Environments = new List<EnvironmentProfile> { new EnvironmentProfile() }; }
        static string Token() { var bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes); return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public EnvironmentProfile Active { get { return Environments.Find(x => x.Name == ActiveEnvironment) ?? Environments[0]; } }
    }
    public static class PrivateStore {
        static readonly byte[] entropy = Encoding.UTF8.GetBytes("ShareSafe.Windows.v1");
        public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShareSafeWindows");
        public static void SecureDirectory(string path) {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Private storage cannot be a link or junction.");
            Directory.CreateDirectory(path);
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            var user = WindowsIdentity.GetCurrent().User;
            security.SetOwner(user);
            foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path, security);
        }
        public static void SaveBytes(string path, byte[] value) {
            var encrypted = ProtectedData.Protect(value, entropy, DataProtectionScope.CurrentUser);
            var temp = path + ".new"; File.WriteAllBytes(temp, encrypted);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static byte[] LoadBytes(string path) { return ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser); }
        public static Preferences Load() {
            SecureDirectory(Root); var path = Path.Combine(Root, "preferences.dpapi");
            var prefs = File.Exists(path) ? Json.Serializer().Deserialize<Preferences>(Encoding.UTF8.GetString(LoadBytes(path))) : new Preferences();
            if (prefs.Environments == null || prefs.Environments.Count == 0) throw new IOException("The environment settings are invalid.");
            return prefs;
        }
        public static void Save(Preferences value) { SaveBytes(Path.Combine(Root, "preferences.dpapi"), Encoding.UTF8.GetBytes(Json.Write(value))); }
    }

    public sealed class Engine : IDisposable {
        readonly string root; readonly Preferences prefs; readonly HttpClient http;
        Process process;
        public bool Ready { get; private set; }
        public Engine(string repository, Preferences settings) {
            root = repository; prefs = settings;
            http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(9), MaxResponseContentBufferSize = 36 * 1024 * 1024 };
        }
        public async Task Start() {
            var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
            var engine = Path.Combine(root, "build", "windows", "engine", "sharesafe-engine.exe");
            if (!File.Exists(engine) && !File.Exists(python)) throw new IOException("Run scripts\\build-windows.ps1 first to install the local Python engine.");
            var config = Path.Combine(PrivateStore.Root, "engine-startup.json");
            File.WriteAllText(config, Json.Write(new { native_token = prefs.NativeToken, browser_token = prefs.BrowserToken, port = 8766, model_port = 11434 }), new UTF8Encoding(false));
            try {
                // The packaged engine takes precedence; source builds use the repository venv.
                var args = (File.Exists(engine) ? "" : Quote(Path.Combine(root, "backend", "bridge_entry.py")) + " ") + "--config " + Quote(config) + " --data-dir " + Quote(PrivateStore.Root) + " --parent-pid " + Process.GetCurrentProcess().Id;
                var start = new ProcessStartInfo(File.Exists(engine) ? engine : python, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
                start.EnvironmentVariables["PYTHONPATH"] = Path.Combine(root, "backend");
                process = Process.Start(start);
                // Drain without persisting prompts, traces or bearer tokens.
                process.OutputDataReceived += delegate { }; process.ErrorDataReceived += delegate { }; process.BeginOutputReadLine(); process.BeginErrorReadLine();
                for (var i = 0; i < 80; i++) {
                    if (process.HasExited) throw new IOException("The local engine could not start. Check dependencies and whether port 8766 is already occupied.");
                    try { await Request("/v1/health"); Ready = true; return; } catch (HttpRequestException) { } catch (TaskCanceledException) { }
                    await Task.Delay(150);
                }
                throw new IOException("The local engine did not become ready.");
            } finally { if (File.Exists(config)) File.Delete(config); }
        }
        public async Task<Dictionary<string, object>> Request(string path, object body = null, bool browser = false) {
            using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, "http://127.0.0.1:8766" + path)) {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", browser ? prefs.BrowserToken : prefs.NativeToken);
                if (body != null) request.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request)) {
                    var data = Json.Read(await response.Content.ReadAsStringAsync());
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Json.Str(data, "error"));
                    return data;
                }
            }
        }
        public Task<Dictionary<string, object>> Apply(EnvironmentProfile profile) {
            var policy = Json.Read(profile.Policy);
            // Full profile actions are honored by the inline bridge, including Block.
            policy["use_model"] = false;
            return Request("/v1/inline/policy", policy);
        }
        public void Dispose() {
            Ready = false;
            if (process != null) {
                try { if (!process.HasExited) { Request("/v1/shutdown", new { }).Wait(2000); if (!process.WaitForExit(12000)) process.Kill(); } } catch { try { if (!process.HasExited) process.Kill(); } catch { } }
                process.Dispose(); process = null;
            }
            http.Dispose();
        }
        public static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    }
}
