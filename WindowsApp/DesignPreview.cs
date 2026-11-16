using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

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
            var fixturePath=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","tests","fixtures","knowledge-graph.json"));
            var fixture=Json.Read(File.ReadAllText(fixturePath));var graph=(Dictionary<string,object>)fixture["graph"];
            analysis["graph"]=graph;analysis["text"]=Json.Str(fixture,"source");OpenKnowledgeBase();
            var map=knowledgeWindow.Canvas.Map;
            if(map.EntityCount!=Json.Array(graph,"nodes").Length||map.RelationshipCount!=Json.Array(graph,"edges").Length)throw new InvalidOperationException("The graph must preserve every extracted entity and recorded relationship.");
            var originals=new HashSet<string>(Json.Array(graph,"edges").Cast<Dictionary<string,object>>().Select(x=>Json.Str(x,"source")+"|"+Json.Str(x,"target")+"|"+Json.Str(x,"type")+"|"+Json.Str(x,"evidence")));
            foreach(var edge in map.Edges.Where(x=>!x.Organizer&&x.Kind!="source mention"))if(!originals.Contains(edge.Source.Substring(7)+"|"+edge.Target.Substring(7)+"|"+edge.Kind+"|"+edge.Evidence))throw new InvalidOperationException("The visual graph introduced an unsupported relationship.");
            if(map.Nodes.Select(x=>x.Id).Distinct().Count()!=map.Nodes.Count||map.Nodes.Any(x=>float.IsNaN(x.Position.X)||float.IsNaN(x.Position.Y)))throw new InvalidOperationException("Graph layout identifiers and coordinates must remain valid.");
            var group=map.Nodes.First(x=>x.Kind=="group");knowledgeWindow.Canvas.Select(group.Id,false);knowledgeWindow.Canvas.ToggleBranch(group.Id);
            if(map.Nodes.Any(x=>x.Kind=="entity"&&x.Group==group.Id&&map.Visible(x)))throw new InvalidOperationException("Collapsed branches must hide their member entities.");
            var child=map.Nodes.First(x=>x.Kind=="entity"&&x.Group==group.Id);knowledgeWindow.Canvas.Select(child.Id,true);
            if(!map.Visible(child))throw new InvalidOperationException("Searching for a hidden entity must reveal its branch.");
            var sourceNode=map.Nodes.First(x=>x.Kind=="source");knowledgeWindow.Canvas.Select(sourceNode.Id,true);
            if(!map.Visible(sourceNode)||!sourceNode.Description.Contains("Project"))throw new InvalidOperationException("Source nodes must reveal exact review evidence.");
            map.IncludeSources=false;knowledgeWindow.Canvas.Select("review",false);knowledgeWindow.Canvas.Fit();Application.DoEvents();
            using(var image=new Bitmap(knowledgeWindow.Width,knowledgeWindow.Height)){knowledgeWindow.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(destination,"knowledge-base.png"),System.Drawing.Imaging.ImageFormat.Png);}
            if(new KnowledgeMap(new Dictionary<string,object>(),"").Nodes.Count!=1)throw new InvalidOperationException("Empty graphs must still show the review center.");
            if(KnowledgeMap.ScalarSlice("A\uD835\uDC00B",1,2)!="\uD835\uDC00")throw new InvalidOperationException("Source excerpts must preserve Unicode scalar offsets.");
            if (actionRules.Rows.Count != 11) throw new InvalidOperationException("All supported policy categories must be editable.");
            analysis["blocked"] = true; RefreshWorkflow(); if (save.Enabled) throw new InvalidOperationException("Blocking findings must disable saving even if a preview exists.");
            analysis["blocked"] = false; analysis["kind"] = "image"; imageReviewed = false; RefreshWorkflow(); if (save.Enabled) throw new InvalidOperationException("Images require full-image review.");
            imageReviewed = true; RefreshWorkflow(); if (!save.Enabled) throw new InvalidOperationException("Reviewed images should enable saving.");
            phraseRules.Rows.Clear(); phraseRules.Rows.Add("ProjectCedar", "general project"); if (Pairs(phraseRules)["ProjectCedar"] != "general project") throw new InvalidOperationException("Replacement form round trip failed.");
            phraseRules.Rows.Add("projectcedar", "different name"); bool rejected = false; try { Pairs(phraseRules); } catch (InvalidOperationException) { rejected = true; } if (!rejected) throw new InvalidOperationException("Ambiguous replacements must be rejected.");
            source.Text += " Changed.";
            if (save.Enabled || session != null || knowledgeWindow!=null) throw new InvalidOperationException("Editing a source must invalidate the preview, close the graph and disable saving.");
            ExitApplication();
            return new[] { "Seven rounded native pages rendered with synthetic data", "Empty reviews cannot save", "A completed preview enables saving", "Blocked findings disable saving", "Images require full-image review", "Reviewed images can save", "Editing source invalidates review, closes the graph and disables saving", "All eleven policy categories remain editable", "Replacement fields preserve their values", "Case-colliding replacements are rejected", "Knowledge graph window opens and renders", "All extracted entities and recorded relationships are preserved", "No unsupported relationship is invented", "Graph node identifiers and layout coordinates remain valid", "Collapsed branches hide members", "Selecting hidden entities reveals their branch", "Source nodes show review evidence", "Empty graphs retain a center node", "Source excerpts preserve Unicode scalar offsets" };
        }
    }
}
