using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ShareSafe.Windows {
    // Shared product palette: eggshell #F7F2E7 and dark brown #3B251C.
    public static class Appearance {
        public static readonly Color Eggshell = Color.FromArgb(247, 242, 231);
        public static readonly Color Brown = Color.FromArgb(59, 37, 28);
        public static readonly Font Body = new Font("Segoe UI", 10.5f);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Icon CreateIcon() {
            using (var bitmap = new Bitmap(32, 32)) using (var canvas = Graphics.FromImage(bitmap)) {
                canvas.Clear(Eggshell); canvas.SmoothingMode = SmoothingMode.AntiAlias;
                var points = new[] { new Point(16, 3), new Point(27, 8), new Point(25, 21), new Point(16, 29), new Point(7, 21), new Point(5, 8) };
                using (var brush = new SolidBrush(Brown)) canvas.FillPolygon(brush, points);
                using (var pen = new Pen(Eggshell, 2)) canvas.DrawLines(pen, new[] { new Point(10, 16), new Point(14, 20), new Point(22, 12) });
                var handle = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
            }
        }
        public static void Apply(Control root) {
            root.BackColor = Eggshell; root.ForeColor = Brown;
            var text = root as TextBox;
            if (text != null) { text.BorderStyle = BorderStyle.FixedSingle; text.Font = Body; }
            var check = root as CheckBox; if (check != null) check.FlatStyle = FlatStyle.Flat;
            var combo = root as ComboBox;
            if (combo != null) {
                combo.FlatStyle = FlatStyle.Flat; combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = 26;
                if (combo.Tag as string != "appearance") {
                    combo.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                        bool selected = (e.State & DrawItemState.Selected) != 0;
                        using (var brush = new SolidBrush(selected ? Brown : Eggshell)) e.Graphics.FillRectangle(brush, e.Bounds);
                        if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, e.Bounds, selected ? Eggshell : Brown, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }; combo.Tag = "appearance";
                }
            }
            var grid = root as DataGridView;
            if (grid != null) {
                grid.EnableHeadersVisualStyles = false; grid.BackgroundColor = Eggshell; grid.GridColor = Brown; grid.BorderStyle = BorderStyle.None;
                grid.DefaultCellStyle.BackColor = Eggshell; grid.DefaultCellStyle.ForeColor = Brown;
                grid.DefaultCellStyle.SelectionBackColor = Brown; grid.DefaultCellStyle.SelectionForeColor = Eggshell;
                grid.DefaultCellStyle.Padding = new Padding(8); grid.RowTemplate.Height = 40;
                foreach (DataGridViewRow row in grid.Rows) row.Height = 42;
                foreach (DataGridViewColumn column in grid.Columns) { var checkbox = column as DataGridViewCheckBoxColumn; if (checkbox != null) checkbox.FlatStyle = FlatStyle.Flat; }
                grid.ColumnHeadersDefaultCellStyle.BackColor = Brown; grid.ColumnHeadersDefaultCellStyle.ForeColor = Eggshell;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Brown; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Eggshell;
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8); grid.ColumnHeadersHeight = 42;
            }
            foreach (Control child in root.Controls) Apply(child);
            var surface = root as WarmSurface; if (surface != null && surface.Inverted) { root.BackColor = Brown; root.ForeColor = Eggshell; foreach (Control child in root.Controls) InvertLabels(child); }
            var button = root as WarmButton; if (button != null) button.Invalidate();
        }
        static void InvertLabels(Control control) { if (control is Label || control is FlowLayoutPanel) { control.BackColor = Brown; control.ForeColor = Eggshell; } foreach (Control child in control.Controls) InvertLabels(child); }
        public static Label Caption(string text, int height = 42) { return new Label { Text = text, AutoSize = false, Dock = DockStyle.Top, Height = height, ForeColor = Brown, Padding = new Padding(0, 8, 0, 4) }; }
        public static Panel Section(string title, string description, Control content) {
            var panel = new WarmSurface { Dock = DockStyle.Fill, Padding = new Padding(18) };
            panel.Controls.Add(content);
            if (!string.IsNullOrEmpty(description)) panel.Controls.Add(Caption(description, 46));
            panel.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 15), Dock = DockStyle.Top, Height = 34 });
            return panel;
        }
    }
    public class WarmSurface : Panel {
        public bool Inverted;
        public WarmSurface() { DoubleBuffered = true; BackColor = Appearance.Eggshell; ForeColor = Appearance.Brown; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Appearance.Brown)) e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(Width - 1, 0), Math.Max(Height - 1, 0)); }
    }
    public class WarmButton : Button {
        public bool Primary, Selected, Inverted;
        public WarmButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; UseMnemonic = false; AutoSize = true; Padding = new Padding(14, 8, 14, 8); Margin = new Padding(0, 0, 10, 8); MinimumSize = new Size(90, 38); Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e) {
            var solid = Enabled && (Primary || Selected);
            var background = solid ? Appearance.Brown : Appearance.Eggshell; var foreground = solid ? Appearance.Eggshell : Appearance.Brown;
            if (Inverted) { background = Selected ? Appearance.Eggshell : Appearance.Brown; foreground = Selected ? Appearance.Brown : Appearance.Eggshell; }
            e.Graphics.Clear(background);
            using (var pen = new Pen(Appearance.Brown)) { if (!Enabled) pen.DashStyle = DashStyle.Dot; e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(Width - 1, 0), Math.Max(Height - 1, 0)); }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused) using (var pen = new Pen(foreground) { DashStyle = DashStyle.Dot }) e.Graphics.DrawRectangle(pen, 4, 4, Math.Max(Width - 9, 0), Math.Max(Height - 9, 0));
        }
    }
    public class WarmComboBox : ComboBox {
        protected override void WndProc(ref Message message) {
            base.WndProc(ref message);
            if (DropDownStyle != ComboBoxStyle.DropDownList || !IsHandleCreated) return;
            if (message.Msg == 0x000F) { using (var graphics = Graphics.FromHwnd(Handle)) DrawLabel(graphics); }
            else if ((message.Msg == 0x0318 || message.Msg == 0x0317) && message.WParam != IntPtr.Zero) { using (var graphics = Graphics.FromHdc(message.WParam)) DrawLabel(graphics); }
        }
        void DrawLabel(Graphics graphics) {
            var bounds = new Rectangle(1, 1, Math.Max(Width - 2, 1), Math.Max(Height - 2, 1));
            using (var brush = new SolidBrush(Appearance.Eggshell)) graphics.FillRectangle(brush, bounds);
            using (var pen = new Pen(Appearance.Brown)) graphics.DrawRectangle(pen, 0, 0, Math.Max(Width - 1, 0), Math.Max(Height - 1, 0));
            var text = SelectedItem == null ? Text : GetItemText(SelectedItem);
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(9, 1, Math.Max(Width - 30, 1), Math.Max(Height - 2, 1)), Appearance.Brown, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var pen = new Pen(Appearance.Brown, 1.5f)) graphics.DrawLines(pen, new[] { new Point(Width - 18, Height / 2 - 2), new Point(Width - 14, Height / 2 + 2), new Point(Width - 10, Height / 2 - 2) });
        }
    }
    public class WarmMenuRenderer : ToolStripProfessionalRenderer {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { using (var brush = new SolidBrush(e.Item.Selected ? Appearance.Brown : Appearance.Eggshell)) e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size)); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Selected ? Appearance.Eggshell : Appearance.Brown; base.OnRenderItemText(e); }
    }
    public class WarmTabs : TabControl {
        public WarmTabs() {
            DrawMode = TabDrawMode.OwnerDrawFixed; SizeMode = TabSizeMode.Fixed; ItemSize = new Size(160, 34);
            DrawItem += delegate(object sender, DrawItemEventArgs e) {
                var selected = SelectedIndex == e.Index; var rect = GetTabRect(e.Index);
                using (var brush = new SolidBrush(selected ? ShareSafe.Windows.Appearance.Brown : ShareSafe.Windows.Appearance.Eggshell)) e.Graphics.FillRectangle(brush, rect);
                TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, ShareSafe.Windows.Appearance.Body, rect, selected ? ShareSafe.Windows.Appearance.Eggshell : ShareSafe.Windows.Appearance.Brown, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }
    }
}
