using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ShareSafe.Windows {
    public sealed class KnowledgeNode {
        public string Id, Label, Kind, Group, Description; public PointF Position; public float Radius; public Dictionary<string,object> Data;
        public override string ToString() { return Label; }
    }
    public sealed class KnowledgeEdge {
        public string Source,Target,Kind,Evidence; public bool Organizer;
    }
    public sealed class KnowledgeMap {
        public readonly List<KnowledgeNode> Nodes=new List<KnowledgeNode>();
        public readonly List<KnowledgeEdge> Edges=new List<KnowledgeEdge>();
        public readonly Dictionary<string,KnowledgeNode> Lookup=new Dictionary<string,KnowledgeNode>();
        public readonly HashSet<string> Collapsed=new HashSet<string>();
        readonly Dictionary<string,List<KnowledgeEdge>> touching=new Dictionary<string,List<KnowledgeEdge>>();
        readonly Dictionary<string,string[]> sourceGroups=new Dictionary<string,string[]>();
        public int EntityCount, RelationshipCount;
        public bool IncludeSources;
        void Add(KnowledgeNode node) { Nodes.Add(node);Lookup[node.Id]=node; }
        public KnowledgeMap(Dictionary<string,object> graph,string source) {
            var entities=Json.Array(graph,"nodes").Cast<Dictionary<string,object>>().OrderBy(x=>Json.Str(x,"id"),StringComparer.Ordinal).ToArray();EntityCount=entities.Length;
            Add(new KnowledgeNode { Id="review",Label="Knowledge base",Kind="root",Radius=70,Description="This review\r\n\r\n"+EntityCount+" extracted entities. Dashed branches group the display; solid links come from the actual review graph. Select a node to inspect its source and relationships." });
            var groups=entities.GroupBy(x=>Json.Str(x,"category")).OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray();double cursor=-Math.PI/2;
            float entityRing=Math.Max(470,entities.Length*38/(float)Math.PI),sourceRing=entityRing+280;
            foreach(var group in groups) {
                double sector=2*Math.PI*group.Count()/Math.Max(entities.Length,1),middle=cursor+sector/2;string id="group:"+group.Key;
                Add(new KnowledgeNode { Id=id,Label=GroupName(group.Key),Kind="group",Group=id,Radius=49,Position=At(240,middle),Description=GroupName(group.Key)+"\r\n\r\n"+group.Count()+" entities in this display branch. This grouping is a navigation aid, not a detected relationship. Double-click the node or use Collapse branch to hide its members." });
                Edges.Add(new KnowledgeEdge { Source="review",Target=id,Kind="display group",Organizer=true });int index=0;
                foreach(var entity in group) {
                    double angle=cursor+sector*(index+.5)/group.Count();string eid="entity:"+Json.Str(entity,"id");
                    var mentions=Json.Array(entity,"mentions").Cast<Dictionary<string,object>>().Select(x=>Json.Str(x,"source")).Distinct().ToArray();
                    Add(new KnowledgeNode { Id=eid,Label=Json.Str(entity,"label"),Kind="entity",Group=id,Radius=31,Position=At(entityRing,angle),Data=entity,Description=GroupName(group.Key)+"\r\n\r\nSources: "+string.Join(", ",mentions)+(entity.ContainsKey("sensitive")&&Convert.ToBoolean(entity["sensitive"])?"\r\nFlagged as confidential by supported evidence.":"") });
                    Edges.Add(new KnowledgeEdge { Source=id,Target=eid,Kind="display group",Organizer=true });index++;
                }cursor+=sector;
            }
            foreach(Dictionary<string,object> edge in Json.Array(graph,"edges")) {
                string a="entity:"+Json.Str(edge,"source"),b="entity:"+Json.Str(edge,"target");
                if(Lookup.ContainsKey(a)&&Lookup.ContainsKey(b))Edges.Add(new KnowledgeEdge { Source=a,Target=b,Kind=Json.Str(edge,"type"),Evidence=Json.Str(edge,"evidence") });
            }
            RelationshipCount=Edges.Count(x=>!x.Organizer);
            var chunks=Json.Array(graph,"chunks").Cast<Dictionary<string,object>>().ToDictionary(x=>Json.Str(x,"id"),x=>x);
            var sourceParents=new Dictionary<string,List<KnowledgeNode>>();
            foreach(var entity in Nodes.Where(x=>x.Kind=="entity").ToArray())foreach(Dictionary<string,object> mention in Json.Array(entity.Data,"mentions")) {
                string sid=Json.Str(mention,"source");if(!chunks.ContainsKey(sid))continue;
                if(!sourceParents.ContainsKey(sid))sourceParents[sid]=new List<KnowledgeNode>();if(!sourceParents[sid].Contains(entity))sourceParents[sid].Add(entity);
            }
            int sourceIndex=0;
            foreach(var entry in sourceParents.OrderBy(x=>x.Key,StringComparer.Ordinal)) {
                var chunk=chunks[entry.Key];string excerpt="";if(chunk.ContainsKey("start")&&chunk.ContainsKey("end"))excerpt=ScalarSlice(source,Convert.ToInt32(chunk["start"]),Convert.ToInt32(chunk["end"]));
                double angle=-Math.PI/2+2*Math.PI*(sourceIndex+.5)/Math.Max(sourceParents.Count,1);
                string id="source:"+entry.Key;
                Add(new KnowledgeNode { Id=id,Label="Source "+entry.Key,Kind="source",Radius=23,Position=At(sourceRing+sourceIndex%3*90,angle),Description="Exact source excerpt\r\n\r\n"+excerpt,Data=chunk });
                sourceGroups[id]=entry.Value.Select(x=>x.Group).Distinct().ToArray();
                foreach(var parent in entry.Value)Edges.Add(new KnowledgeEdge { Source=parent.Id,Target=id,Kind="source mention",Evidence=entry.Key });sourceIndex++;
            }
            foreach(var edge in Edges)foreach(var id in new[]{edge.Source,edge.Target}){if(!touching.ContainsKey(id))touching[id]=new List<KnowledgeEdge>();touching[id].Add(edge);}
        }
        static PointF At(float radius,double angle) { return new PointF(radius*(float)Math.Cos(angle),radius*(float)Math.Sin(angle)); }
        public static string ScalarSlice(string source,int start,int end) {
            int scalar=0,a=-1,b=source.Length;for(int index=0;index<source.Length;index++){if(scalar==start)a=index;if(scalar==end){b=index;break;}if(char.IsHighSurrogate(source[index])&&index+1<source.Length&&char.IsLowSurrogate(source[index+1]))index++;scalar++;}return a<0?"":source.Substring(a,Math.Max(0,b-a));
        }
        public static string GroupName(string category) {
            var labels=new Dictionary<string,string> { {"entity","Projects & entities"},{"EMAIL","Email addresses"},{"PERSON","People"},{"CREDENTIAL","Credentials"},{"CONFIDENTIAL","Confidential terms"},{"PHONE","Phone numbers"},{"ACCOUNT","Accounts"},{"ADDRESS","Addresses"},{"FINANCIAL","Financial values"},{"CARD","Cards"},{"PAN","PAN candidates"},{"AADHAAR","Aadhaar candidates"} };return labels.ContainsKey(category)?labels[category]:category.Replace('_',' ');
        }
        public bool Visible(KnowledgeNode node) {
            if(node.Kind=="entity")return !Collapsed.Contains(node.Group);
            if(node.Kind=="source")return IncludeSources && sourceGroups.ContainsKey(node.Id) && sourceGroups[node.Id].Any(x=>!Collapsed.Contains(x));
            return true;
        }
        public IEnumerable<KnowledgeEdge> Touching(string id) { return touching.ContainsKey(id)?touching[id]:Enumerable.Empty<KnowledgeEdge>(); }
        public IEnumerable<KnowledgeEdge> Related(string id) { return Touching(id).Where(x=>!x.Organizer); }
    }
    public sealed class KnowledgeCanvas : Control {
        public readonly KnowledgeMap Map; public string Selected="review"; public Action<KnowledgeNode> SelectionChanged; public Action ViewChanged;
        float zoom=.6f;PointF pan;Point last;KnowledgeNode dragging;bool moving,dragged;readonly ToolTip tip=new ToolTip();string hovered;
        public KnowledgeCanvas(KnowledgeMap map) {
            Map=map;DoubleBuffered=true;Dock=DockStyle.Fill;BackColor=Appearance.Eggshell;AccessibleName="Interactive knowledge graph. Use the node list to select nodes with the keyboard.";TabStop=true;
            MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;Focus();last=e.Location;dragging=Hit(e.Location);moving=true;dragged=false;Capture=true;};
            MouseMove+=delegate(object sender,MouseEventArgs e){
                if(moving){var dx=e.X-last.X;var dy=e.Y-last.Y;dragged|=Math.Abs(dx)+Math.Abs(dy)>2;if(dragging!=null)dragging.Position=new PointF(dragging.Position.X+dx/zoom,dragging.Position.Y+dy/zoom);else pan=new PointF(pan.X+dx,pan.Y+dy);last=e.Location;Invalidate();return;}
                var node=Hit(e.Location);Cursor=node==null?Cursors.SizeAll:Cursors.Hand;if(node==null&&hovered!=null){tip.SetToolTip(this,null);hovered=null;}if(node!=null&&hovered!=node.Id){tip.SetToolTip(this,node.Label+"\r\n"+(node.Kind=="group"?"Double-click to collapse or expand this branch":"Click for details. Drag to move. Double-click to focus."));hovered=node.Id;}
            };
            MouseUp+=delegate(object sender,MouseEventArgs e){if(moving&&!dragged){var node=Hit(e.Location);if(node!=null)Select(node.Id,false);}moving=false;dragging=null;Capture=false;};
            MouseDoubleClick+=delegate(object sender,MouseEventArgs e){var node=Hit(e.Location);if(node!=null){if(node.Kind=="group")ToggleBranch(node.Id);else Select(node.Id,true);}};
            MouseWheel+=delegate(object sender,MouseEventArgs e){ZoomBy(e.Delta>0?1.15f:1/1.15f,e.Location);};
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Add||e.KeyCode==Keys.Oemplus)ZoomBy(1.2f);if(e.KeyCode==Keys.Subtract||e.KeyCode==Keys.OemMinus)ZoomBy(1/1.2f);if(e.KeyCode==Keys.Home)Fit();};
        }
        PointF ScreenPoint(PointF point) { return new PointF(Width/2f+pan.X+point.X*zoom,Height/2f+pan.Y+point.Y*zoom); }
        KnowledgeNode Hit(Point point) { return Map.Nodes.Where(Map.Visible).Reverse().FirstOrDefault(n=>{var p=ScreenPoint(n.Position);return Math.Pow(point.X-p.X,2)+Math.Pow(point.Y-p.Y,2)<=Math.Pow(Math.Max(12,n.Radius*zoom),2);}); }
        public void Select(string id,bool focus) {
            if(!Map.Lookup.ContainsKey(id))return;var node=Map.Lookup[id];if(node.Kind=="entity")Map.Collapsed.Remove(node.Group);if(node.Kind=="source"){Map.IncludeSources=true;foreach(var link in Map.Related(id)){var other=Map.Lookup[link.Source==id?link.Target:link.Source];if(other.Group!=null)Map.Collapsed.Remove(other.Group);}}Selected=id;
            if(focus){zoom=Math.Max(zoom,.85f);pan=new PointF(-node.Position.X*zoom,-node.Position.Y*zoom);}if(SelectionChanged!=null)SelectionChanged(node);Invalidate();
        }
        public void ToggleBranch(string id) { if(!Map.Lookup.ContainsKey(id)||Map.Lookup[id].Kind!="group")return;if(!Map.Collapsed.Add(id))Map.Collapsed.Remove(id);Invalidate();if(ViewChanged!=null)ViewChanged(); }
        public void ZoomBy(float factor) { ZoomBy(factor,new Point(Width/2,Height/2)); }
        void ZoomBy(float factor,Point origin) {float next=Math.Max(.015f,Math.Min(3.5f,zoom*factor));float ratio=next/zoom;pan=new PointF(origin.X-Width/2f-(origin.X-Width/2f-pan.X)*ratio,origin.Y-Height/2f-(origin.Y-Height/2f-pan.Y)*ratio);zoom=next;Invalidate();if(ViewChanged!=null)ViewChanged();}
        public void Fit() {
            var nodes=Map.Nodes.Where(Map.Visible).ToArray();float left=nodes.Min(x=>x.Position.X-x.Radius-70),right=nodes.Max(x=>x.Position.X+x.Radius+70),top=nodes.Min(x=>x.Position.Y-x.Radius-45),bottom=nodes.Max(x=>x.Position.Y+x.Radius+45);
            zoom=Math.Max(.015f,Math.Min(1.05f,Math.Min(Math.Max(Width-80,80)/(right-left),Math.Max(Height-80,80)/(bottom-top))));pan=new PointF(-(left+right)/2*zoom,-(top+bottom)/2*zoom);Invalidate();if(ViewChanged!=null)ViewChanged();
        }
        public string Statistics { get { return Map.EntityCount+" entities  /  "+Map.RelationshipCount+" detected relationships  /  "+Math.Round(zoom*100)+"% zoom"; } }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var brush=new SolidBrush(Color.FromArgb(32,Appearance.Brown)))for(int x=20;x<Width;x+=32)for(int y=20;y<Height;y+=32)g.FillEllipse(brush,x,y,1.4f,1.4f);
            var visible=new HashSet<string>(Map.Nodes.Where(Map.Visible).Select(x=>x.Id));var related=new HashSet<string>(Map.Related(Selected).Select(x=>x.Source==Selected?x.Target:x.Source));
            var priority=Map.Touching(Selected).Where(x=>visible.Contains(x.Source)&&visible.Contains(x.Target));var links=priority.Concat(Map.Edges.Where(x=>x.Source!=Selected&&x.Target!=Selected&&visible.Contains(x.Source)&&visible.Contains(x.Target))).Take(2500);
            foreach(var edge in links) {
                bool active=edge.Source==Selected||edge.Target==Selected;using(var pen=new Pen(Color.FromArgb(active?220:edge.Organizer?90:55,Appearance.Brown),active?1.8f:1)){if(edge.Organizer)pen.DashStyle=DashStyle.Dash;
                    var a=ScreenPoint(Map.Lookup[edge.Source].Position);var b=ScreenPoint(Map.Lookup[edge.Target].Position);if(edge.Source==edge.Target)continue;
                    if(edge.Organizer)g.DrawLine(pen,a,b);else {var bend=new PointF((a.X+b.X)/2+(a.Y-b.Y)*.1f,(a.Y+b.Y)/2+(b.X-a.X)*.1f);using(var path=new GraphicsPath()){path.AddBezier(a,new PointF((a.X+bend.X)/2,(a.Y+bend.Y)/2),new PointF((b.X+bend.X)/2,(b.Y+bend.Y)/2),b);g.DrawPath(pen,path);}}
                }
            }
            foreach(var node in Map.Nodes.Where(Map.Visible)) {
                var point=ScreenPoint(node.Position);float radius=Math.Max(node.Kind=="root"?28:9,node.Radius*zoom);bool selected=node.Id==Selected,solid=node.Kind=="root"||selected;
                var rectangle=new RectangleF(point.X-radius,point.Y-radius,radius*2,radius*2);
                if(selected||related.Contains(node.Id))using(var halo=new Pen(Appearance.Brown,selected?2:1))g.DrawEllipse(halo,rectangle.X-5,rectangle.Y-5,rectangle.Width+10,rectangle.Height+10);
                using(var fill=new SolidBrush(solid?Appearance.Brown:Appearance.Eggshell))using(var pen=new Pen(Appearance.Brown,node.Kind=="group"?1.6f:1)){g.FillEllipse(fill,rectangle);g.DrawEllipse(pen,rectangle);}
                if(node.Kind=="root") {using(var font=new Font("Segoe UI Semibold",9))using(var brush=new SolidBrush(Appearance.Eggshell))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString("Knowledge\nbase",font,brush,new RectangleF(point.X-radius+2,point.Y-radius+2,radius*2-4,radius*2-4),format);}
                else {
                    ActionIcons.Draw(g,new RectangleF(point.X-9,point.Y-9,18,18),node.Kind=="source"?ActionIcon.Document:node.Kind=="group"?ActionIcon.Folder:ActionIcon.Graph,solid?Appearance.Eggshell:Appearance.Brown);
                    using(var font=new Font("Segoe UI",Math.Max(8,Math.Min(10.5f,10*zoom))))TextRenderer.DrawText(g,node.Label,font,Rectangle.Round(new RectangleF(point.X-90,point.Y+radius+7,180,42)),Appearance.Brown,Appearance.Eggshell,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                }
            }
            if(Map.EntityCount==0)TextRenderer.DrawText(g,"No entities were extracted from this review.\r\nAnalyze content containing named projects or supported findings to build a graph.",Appearance.Body,new Rectangle(30,Height-110,Math.Max(Width-60,100),90),Appearance.Brown,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak);
        }
        protected override void Dispose(bool disposing) { if(disposing)tip.Dispose();base.Dispose(disposing); }
    }
    public sealed class KnowledgeWindow : Form {
        public readonly KnowledgeCanvas Canvas;readonly TextBox details;readonly Label statistics;readonly ListBox nodes;readonly WarmButton branch;
        public KnowledgeWindow(Dictionary<string,object> graph,string source) {
            Text="ShareSafe · Knowledge base";Size=new Size(1370,900);MinimumSize=new Size(1050,700);StartPosition=FormStartPosition.CenterParent;Font=Appearance.Body;Icon=Appearance.CreateIcon();
            var map=new KnowledgeMap(graph,source);Canvas=new KnowledgeCanvas(map);details=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};statistics=new Label{Dock=DockStyle.Bottom,Height=44,Padding=new Padding(18,12,0,0)};
            var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=112,Padding=new Padding(18,12,18,0)};
            var search=new TextBox{Width=230,AccessibleName="Find a knowledge node"};
            toolbar.Controls.AddRange(new Control[]{search,Tool("Find node",delegate{var match=map.Nodes.FirstOrDefault(x=>x.Label.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0);if(match!=null)Canvas.Select(match.Id,true);else details.Text="No matching node. Try a project, person, source or information type.";}),Tool("Zoom in",delegate{Canvas.ZoomBy(1.25f);}),Tool("Zoom out",delegate{Canvas.ZoomBy(.8f);}),Tool("Fit graph",delegate{Canvas.Fit();})});
            branch=Tool("Collapse branch",delegate{Canvas.ToggleBranch(Canvas.Selected);});branch.Enabled=false;toolbar.Controls.Add(branch);
            bool syncingSources=false;var sources=new CheckBox{Text="Show source nodes",AutoSize=true,Padding=new Padding(4,9,4,0)};sources.CheckedChanged+=delegate{map.IncludeSources=sources.Checked;if(!syncingSources)Canvas.Fit();};toolbar.Controls.Add(sources);
            var heading=new Label{Text="Your knowledge, connected",Font=new Font("Georgia",24),Dock=DockStyle.Top,Height=64,Padding=new Padding(18,15,0,0)};
            var inspector=new Panel{Dock=DockStyle.Right,Width=315,Padding=new Padding(10,0,18,12)};nodes=new WarmListBox{Dock=DockStyle.Top,Height=170,AccessibleName="Knowledge graph nodes",DisplayMember="Label"};foreach(var node in map.Nodes)nodes.Items.Add(node);
            nodes.SelectedIndexChanged+=delegate{var node=nodes.SelectedItem as KnowledgeNode;if(node!=null&&node.Id!=Canvas.Selected)Canvas.Select(node.Id,true);};
            inspector.Controls.Add(Appearance.Section("Node details","Select a node or choose one from the list.",details));inspector.Controls.Add(nodes);
            var legend=new Label{Text="Solid links: source-backed relationships and mentions. Dashed branches: display groups.\r\nDrag nodes to arrange them. Drag empty space to pan. Scroll to zoom. Up to 2,500 links are drawn at once; selecting a node prioritizes its links.",Dock=DockStyle.Bottom,Height=72,Padding=new Padding(18,8,15,0)};
            Controls.Add(Canvas);Controls.Add(inspector);Controls.Add(legend);Controls.Add(statistics);Controls.Add(toolbar);Controls.Add(heading);
            Canvas.SelectionChanged=delegate(KnowledgeNode node){syncingSources=true;sources.Checked=map.IncludeSources;syncingSources=false;var text=new StringBuilder(node.Label.ToUpperInvariant()+"\r\n\r\n"+node.Description);foreach(var edge in map.Related(node.Id).Take(120)){string other=edge.Source==node.Id?edge.Target:edge.Source;text.AppendLine("\r\n"+map.Lookup[other].Label+"\r\n"+edge.Kind.Replace('_',' ')+(string.IsNullOrEmpty(edge.Evidence)?"":" · "+edge.Evidence));}details.Text=text.ToString();branch.Enabled=node.Kind=="group";branch.Text=map.Collapsed.Contains(node.Id)?"Expand branch":"Collapse branch";nodes.SelectedItem=node;};
            Canvas.ViewChanged=delegate{statistics.Text=Canvas.Statistics;branch.Text=map.Collapsed.Contains(Canvas.Selected)?"Expand branch":"Collapse branch";};
            Appearance.RoundInputs(this);Appearance.Apply(this);Shown+=delegate{Canvas.Select("review",false);Canvas.Fit();};
        }
        static WarmButton Tool(string text,Action action) {var button=new WarmButton{Text=text};button.Click+=delegate{action();};return button;}
    }
}
