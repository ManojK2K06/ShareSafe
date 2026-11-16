using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareSafe.Windows {
    public static class WindowsOcr {
        public static async Task<Dictionary<string, object>> Read(byte[] data, string baseDirectory) {
            var scripts = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "scripts"));
            var temporary = Path.Combine(PrivateStore.Root, "ocr-" + Guid.NewGuid().ToString("N") + ".png");
            try {
                using (var input = new MemoryStream(data)) using (var image = Image.FromStream(input)) {
                    if ((long)image.Width * image.Height > 16000000) throw new InvalidOperationException("Image exceeds 16 million pixels.");
                    // Match Pillow's EXIF-normalized geometry before collecting rectangles.
                    if (Array.IndexOf(image.PropertyIdList, 0x112) >= 0) {
                        var orientation = BitConverter.ToUInt16(image.GetPropertyItem(0x112).Value, 0);
                        var transforms = new[] { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
                        if (orientation <= 8) image.RotateFlip(transforms[orientation]);
                    }
                    image.Save(temporary, System.Drawing.Imaging.ImageFormat.Png);
                }
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Engine.Quote(Path.Combine(scripts, "windows-ocr.ps1")) + " -ImagePath " + Engine.Quote(temporary)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var process = Process.Start(start)) {
                    var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                    if (!await Task.Run(() => process.WaitForExit(25000))) { process.Kill(); throw new InvalidOperationException("Windows OCR timed out. Manual image review is still available."); }
                    var text = await output; await errors;
                    if (process.ExitCode != 0) throw new InvalidOperationException("Windows OCR is unavailable. Review the full image manually.");
                    return Json.Read(text);
                }
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public sealed class ImageReviewWindow : Form {
        readonly Image image; readonly PictureBox canvas; readonly List<object> automatic;
        Point start; Rectangle drag; bool drawing;
        public List<object> Boxes { get; private set; }
        public ImageReviewWindow(byte[] bytes, object[] detected, List<object> existing) {
            using (var stream = new MemoryStream(bytes)) using (var original = Image.FromStream(stream)) image = new Bitmap(original);
            Boxes = new List<object>(existing); automatic = new List<object>(detected);
            Text = "Review complete image - drag to add redaction rectangles"; Size = new Size(1100, 820); MinimumSize = new Size(650, 500); StartPosition = FormStartPosition.CenterParent;
            canvas = new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Appearance.Eggshell };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58 };
            var undo = new WarmButton { Text = "Undo rectangle" }; var done = new WarmButton { Text = "I reviewed the full image", Primary = true };
            var cancel = new WarmButton { Text = "Cancel" };
            undo.Click += delegate { if (Boxes.Count > 0) Boxes.RemoveAt(Boxes.Count - 1); canvas.Invalidate(); };
            done.Click += delegate { DialogResult = DialogResult.OK; Close(); }; cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            bar.Controls.AddRange(new Control[] { undo, done, cancel }); Controls.Add(canvas); Controls.Add(bar);
            canvas.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button != MouseButtons.Left || !ImageRect().Contains(e.Location)) return; start = e.Location; drag = Rectangle.Empty; drawing = true; };
            canvas.MouseMove += delegate(object sender, MouseEventArgs e) { if (!drawing) return; drag = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y)); canvas.Invalidate(); };
            canvas.MouseUp += delegate {
                if (!drawing) return; drawing = false; var box = Rectangle.Intersect(drag, ImageRect());
                if (box.Width > 2 && box.Height > 2) { var r = ImageRect(); var scale = (double)image.Width / r.Width; Boxes.Add(new Dictionary<string, object> { { "x", (box.X - r.X) * scale }, { "y", (box.Y - r.Y) * scale }, { "width", box.Width * scale }, { "height", box.Height * scale } }); }
                drag = Rectangle.Empty; canvas.Invalidate();
            };
            canvas.Paint += delegate(object sender, PaintEventArgs e) {
                var rect = ImageRect(); var scale = (float)rect.Width / image.Width;
                using (var brush = new SolidBrush(Appearance.Brown)) {
                    foreach (Dictionary<string, object> box in automatic) Draw(e.Graphics, box, rect, scale, brush);
                    foreach (Dictionary<string, object> box in Boxes) Draw(e.Graphics, box, rect, scale, brush);
                }
                if (drawing) using (var pen = new Pen(Appearance.Brown, 2)) e.Graphics.DrawRectangle(pen, drag);
            };
            FormClosed += delegate { canvas.Image = null; image.Dispose(); };
            Font = Appearance.Body; Appearance.Apply(this);
        }
        Rectangle ImageRect() { var scale = Math.Min((double)canvas.ClientSize.Width / image.Width, (double)canvas.ClientSize.Height / image.Height); var w = (int)(image.Width * scale); var h = (int)(image.Height * scale); return new Rectangle((canvas.Width - w) / 2, (canvas.Height - h) / 2, Math.Max(w, 1), Math.Max(h, 1)); }
        static void Draw(Graphics graphics, Dictionary<string, object> box, Rectangle rect, float scale, Brush brush) {
            graphics.FillRectangle(brush, rect.X + Convert.ToSingle(box["x"]) * scale, rect.Y + Convert.ToSingle(box["y"]) * scale, Convert.ToSingle(box["width"]) * scale, Convert.ToSingle(box["height"]) * scale);
        }
    }
}
