using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShareSafe.Windows {
    public enum ActionIcon { None, Reset, Folder, Clipboard, Save, Shield, Document, Graph, Settings, Play, Refresh, Lock, Trash, Plus, Search, ZoomIn, ZoomOut, Fit }
    public static class ActionIcons {
        public static ActionIcon For(string text) {
            var name=(text ?? "").ToLowerInvariant();
            if(name=="zoom in")return ActionIcon.ZoomIn;if(name=="zoom out")return ActionIcon.ZoomOut;if(name.StartsWith("fit "))return ActionIcon.Fit;
            if(name.Contains("branch"))return ActionIcon.Graph;
            if (name.Contains("start over") || name.Contains("not now") || name == "reset view") return ActionIcon.Reset;
            if (name.Contains("knowledge") || name.Contains("graph")) return ActionIcon.Graph;
            if (name.Contains("delete") || name.Contains("remove")) return ActionIcon.Trash;
            if (name.Contains("refresh")) return ActionIcon.Refresh;
            if (name.Contains("folder") || name.Contains("choose a file") || name.Contains("project")) return ActionIcon.Folder;
            if (name.Contains("clipboard") || name.Contains("paste")) return ActionIcon.Clipboard;
            if (name.Contains("vault") || name.Contains("encrypted")) return ActionIcon.Lock;
            if (name.Contains("save") || name.Contains("export")) return ActionIcon.Save;
            if (name.Contains("preview") || name.Contains("details") || name.Contains("activity")) return ActionIcon.Document;
            if (name.Contains("check") || name.Contains("verify") || name.Contains("review")) return ActionIcon.Shield;
            if (name.Contains("run") || name.Contains("sandbox")) return ActionIcon.Play;
            if (name.Contains("setting") || name.Contains("environment") || name.Contains("rules")) return ActionIcon.Settings;
            if (name.Contains("create") || name.Contains("add")) return ActionIcon.Plus;
            if (name.Contains("find") || name.Contains("search")) return ActionIcon.Search;
            return ActionIcon.None;
        }
        public static void Draw(Graphics graphics, RectangleF bounds, ActionIcon kind, Color color) {
            if (kind == ActionIcon.None) return;
            var state=graphics.Save();graphics.TranslateTransform(bounds.X,bounds.Y);graphics.ScaleTransform(bounds.Width/24,bounds.Height/24);graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(color,1.7f) { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round }) {
                switch(kind) {
                    case ActionIcon.Reset: case ActionIcon.Refresh: graphics.DrawArc(pen,4,4,16,16,215,300);graphics.DrawLines(pen,new[]{new PointF(3,4),new PointF(3,10),new PointF(9,10)});break;
                    case ActionIcon.Folder: graphics.DrawLines(pen,new[]{new PointF(3,7),new PointF(3,20),new PointF(21,20),new PointF(21,7),new PointF(11,7),new PointF(8,4),new PointF(3,4),new PointF(3,7)});break;
                    case ActionIcon.Clipboard: graphics.DrawRectangle(pen,5,5,14,16);graphics.DrawRectangle(pen,9,3,6,4);graphics.DrawLine(pen,9,12,15,12);graphics.DrawLine(pen,9,16,15,16);break;
                    case ActionIcon.Save: graphics.DrawLine(pen,12,3,12,15);graphics.DrawLines(pen,new[]{new PointF(7,10),new PointF(12,15),new PointF(17,10)});graphics.DrawLines(pen,new[]{new PointF(4,16),new PointF(4,21),new PointF(20,21),new PointF(20,16)});break;
                    case ActionIcon.Shield: graphics.DrawLines(pen,new[]{new PointF(12,3),new PointF(21,7),new PointF(19,16),new PointF(12,22),new PointF(5,16),new PointF(3,7),new PointF(12,3)});graphics.DrawLines(pen,new[]{new PointF(8,12),new PointF(11,15),new PointF(17,9)});break;
                    case ActionIcon.Document: graphics.DrawLines(pen,new[]{new PointF(5,3),new PointF(14,3),new PointF(20,9),new PointF(20,21),new PointF(5,21),new PointF(5,3)});graphics.DrawLines(pen,new[]{new PointF(14,3),new PointF(14,9),new PointF(20,9)});graphics.DrawLine(pen,8,14,16,14);graphics.DrawLine(pen,8,17,14,17);break;
                    case ActionIcon.Graph: graphics.DrawLine(pen,7,8,17,5);graphics.DrawLine(pen,7,8,12,18);graphics.DrawLine(pen,17,5,12,18);graphics.DrawEllipse(pen,3,4,7,7);graphics.DrawEllipse(pen,15,2,6,6);graphics.DrawEllipse(pen,9,16,6,6);break;
                    case ActionIcon.Settings: graphics.DrawLine(pen,3,6,21,6);graphics.DrawLine(pen,3,12,21,12);graphics.DrawLine(pen,3,18,21,18);graphics.DrawEllipse(pen,7,4,4,4);graphics.DrawEllipse(pen,15,10,4,4);graphics.DrawEllipse(pen,5,16,4,4);break;
                    case ActionIcon.Play: graphics.DrawPolygon(pen,new[]{new PointF(8,4),new PointF(20,12),new PointF(8,20)});break;
                    case ActionIcon.Lock: graphics.DrawArc(pen,7,2,10,13,180,180);graphics.DrawRectangle(pen,5,10,14,12);graphics.DrawLine(pen,12,14,12,18);break;
                    case ActionIcon.Trash: graphics.DrawLine(pen,4,6,20,6);graphics.DrawLine(pen,9,3,15,3);graphics.DrawLines(pen,new[]{new PointF(6,6),new PointF(7,21),new PointF(17,21),new PointF(18,6)});graphics.DrawLine(pen,10,10,10,17);graphics.DrawLine(pen,14,10,14,17);break;
                    case ActionIcon.Plus: graphics.DrawLine(pen,12,4,12,20);graphics.DrawLine(pen,4,12,20,12);break;
                    case ActionIcon.Search: graphics.DrawEllipse(pen,3,3,13,13);graphics.DrawLine(pen,15,15,21,21);break;
                    case ActionIcon.ZoomIn: case ActionIcon.ZoomOut: graphics.DrawEllipse(pen,3,3,14,14);graphics.DrawLine(pen,16,16,21,21);graphics.DrawLine(pen,6,10,14,10);if(kind==ActionIcon.ZoomIn)graphics.DrawLine(pen,10,6,10,14);break;
                    case ActionIcon.Fit: graphics.DrawLines(pen,new[]{new PointF(3,9),new PointF(3,3),new PointF(9,3)});graphics.DrawLines(pen,new[]{new PointF(15,3),new PointF(21,3),new PointF(21,9)});graphics.DrawLines(pen,new[]{new PointF(21,15),new PointF(21,21),new PointF(15,21)});graphics.DrawLines(pen,new[]{new PointF(9,21),new PointF(3,21),new PointF(3,15)});break;
                }
            }graphics.Restore(state);
        }
    }
}
