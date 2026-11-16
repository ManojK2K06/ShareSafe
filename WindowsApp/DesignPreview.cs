using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ShareSafe.Windows {
    static class DesignPreview {
        public static int Create(string root, string destination) {
            Directory.CreateDirectory(destination); PrivateStore.Root = Path.Combine(destination, "synthetic-data"); Directory.CreateDirectory(PrivateStore.Root);
            try {
                var preferences = new Preferences();
                string[] checks;
                using (var engine = new Engine(root, preferences)) using (var window = new MainWindow(engine, preferences)) checks = window.RenderPreviews(destination);
                File.WriteAllText(Path.Combine(destination, "result.json"), Json.Write(new { passed = true, checks = checks, scope = "Offscreen native form rendering and review action gates; synthetic data only; no engine or external UI interaction" }));
                return 0;
            } catch (Exception error) { File.WriteAllText(Path.Combine(destination, "result.json"), Json.Write(new { passed = false, error = error.ToString() })); return 1; }
        }
    }
    public sealed partial class MainWindow {
        internal string[] RenderPreviews(string destination) {
            previewMode = true; StartPosition = FormStartPosition.Manual; Location = new Point(-24000, -24000); Show();
            var save = AllControls(tabs).OfType<Button>().First(x => x.Name == "Save a copy");
            if (save.Enabled) throw new InvalidOperationException("Save must be unavailable before a review.");
            source.Text = "Please summarize the project update for our next meeting.\r\n\r\nContact name: Alex Morgan\r\nEmail: alex@example.org\r\n\r\nKeep the summary clear and easy to follow.";
            session = "synthetic-preview-session"; analysis = Json.Read("{\"blocked\":false,\"kind\":\"text\"}");
            var row = findings.Rows[findings.Rows.Add(true, "EMAIL", "mask", "alex@example.org", "Email address pattern")]; row.Tag = "preview-email";
            var person = findings.Rows[findings.Rows.Add(true, "PERSON", "mask", "Alex Morgan", "Explicit person label")]; person.Tag = "preview-person";
            prepared.Text = "Please summarize the project update for our next meeting.\r\n\r\nContact name: [PERSON_1]\r\nEmail: [EMAIL_1]\r\n\r\nKeep the summary clear and easy to follow.";
            status.Text = "Your preview is ready. Read it, then save a copy or keep it in your vault."; RefreshWorkflow();
            if (!save.Enabled) throw new InvalidOperationException("A completed preview must enable saving.");
            string[] pages = { "review", "environments", "project", "sandbox", "vault", "activity", "settings" };
            for (var index = 0; index < pages.Length; index++) {
                tabs.SelectedIndex = index; PerformLayout(); Application.DoEvents();
                using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(Path.Combine(destination, pages[index] + ".png"), System.Drawing.Imaging.ImageFormat.Png); }
            }
            if (actionRules.Rows.Count != 11) throw new InvalidOperationException("All supported policy categories must be editable.");
            analysis["blocked"] = true; RefreshWorkflow(); if (save.Enabled) throw new InvalidOperationException("Blocking findings must disable saving even if a preview exists.");
            analysis["blocked"] = false; analysis["kind"] = "image"; imageReviewed = false; RefreshWorkflow(); if (save.Enabled) throw new InvalidOperationException("Images require full-image review.");
            imageReviewed = true; RefreshWorkflow(); if (!save.Enabled) throw new InvalidOperationException("Reviewed images should enable saving.");
            phraseRules.Rows.Clear(); phraseRules.Rows.Add("ProjectCedar", "general project"); if (Pairs(phraseRules)["ProjectCedar"] != "general project") throw new InvalidOperationException("Replacement form round trip failed.");
            phraseRules.Rows.Add("projectcedar", "different name"); bool rejected = false; try { Pairs(phraseRules); } catch (InvalidOperationException) { rejected = true; } if (!rejected) throw new InvalidOperationException("Ambiguous replacements must be rejected.");
            source.Text += " Changed.";
            if (save.Enabled || session != null) throw new InvalidOperationException("Editing a source must invalidate the preview and disable saving.");
            ExitApplication();
            return new[] { "Seven native pages rendered with synthetic data", "Empty reviews cannot save", "A completed preview enables saving", "Blocked findings disable saving", "Images require full-image review", "Reviewed images can save", "Editing source invalidates review and disables saving", "All eleven policy categories remain editable", "Replacement fields preserve their values", "Case-colliding replacements are rejected" };
        }
    }
}
