import SwiftUI
import AppKit

struct KnowledgeNode:Identifiable {
    let id:String
    let label:String
    let kind:String
    var group=""
    var position=CGPoint.zero
    var radius:CGFloat=30
    var detail=""
}
struct KnowledgeLink {
    let source:String
    let target:String
    let kind:String
    var evidence=""
    var organizer=false
}
@MainActor final class KnowledgeGraphState:ObservableObject {
    @Published var nodes:[KnowledgeNode]=[]
    let links:[KnowledgeLink]
    let entityCount:Int
    let relationshipCount:Int
    @Published var selected="review"
    @Published var collapsed:Set<String>=[]
    @Published var zoom:CGFloat=0.6
    @Published var showSources=false
    @Published var pan=CGSize.zero
    private var indices:[String:Int]=[:]
    private var touching:[String:[Int]]=[:]
    private var sourceGroups:[String:Set<String>]=[:]
    init(graph:JSON,source:String) {
        let entities=(graph["nodes"] as? [JSON] ?? []).sorted { ($0["id"] as? String ?? "") < ($1["id"] as? String ?? "") }
        entityCount=entities.count
        var values=[KnowledgeNode(id:"review",label:"Knowledge base",kind:"root",radius:70,detail:"This review contains \(entities.count) extracted entities. Dashed branches organize the display. Solid links represent recorded relationships or exact source mentions.")]
        var connections:[KnowledgeLink]=[]
        let groups=Dictionary(grouping:entities,by:{$0["category"] as? String ?? "entity"})
        let entityRing=max(CGFloat(470),CGFloat(entities.count)*38 / .pi)
        var cursor = -Double.pi/2
        for category in groups.keys.sorted() {
            let members=groups[category] ?? []
            let sector=2*Double.pi*Double(members.count)/Double(max(entities.count,1))
            let mid=cursor+sector/2,id="group:"+category
            values.append(KnowledgeNode(id:id,label:Self.groupName(category),kind:"group",group:id,position:Self.at(240,mid),radius:49,detail:"\(members.count) entities in this display branch. This grouping helps you navigate; it does not assert a detected relationship."))
            connections.append(KnowledgeLink(source:"review",target:id,kind:"display group",organizer:true))
            for (index,entity) in members.enumerated() {
                let angle=cursor+sector*(Double(index)+0.5)/Double(max(members.count,1))
                let id="entity:"+(entity["id"] as? String ?? "")
                let sources=Set((entity["mentions"] as? [JSON] ?? []).compactMap { $0["source"] as? String }).sorted().joined(separator:", ")
                let detail=Self.groupName(category)+"\n\nSources: "+sources+((entity["sensitive"] as? Bool ?? false) ? "\nFlagged as confidential by supported evidence." : "")
                values.append(KnowledgeNode(id:id,label:entity["label"] as? String ?? "Entity",kind:"entity",group:"group:"+category,position:Self.at(entityRing,angle),radius:31,detail:detail))
                connections.append(KnowledgeLink(source:"group:"+category,target:id,kind:"display group",organizer:true))
            }
            cursor += sector
        }
        let ids=Set(values.map(\.id))
        for edge in graph["edges"] as? [JSON] ?? [] {
            let a="entity:"+(edge["source"] as? String ?? ""),b="entity:"+(edge["target"] as? String ?? "")
            if ids.contains(a),ids.contains(b) { connections.append(KnowledgeLink(source:a,target:b,kind:edge["type"] as? String ?? "relationship",evidence:edge["evidence"] as? String ?? "")) }
        }
        relationshipCount=connections.filter{!$0.organizer}.count
        var chunks:[String:JSON]=[:]
        for chunk in graph["chunks"] as? [JSON] ?? [] { if let id=chunk["id"] as? String { chunks[id]=chunk } }
        var parents:[String:Set<String>]=[:]
        for entity in entities {
            let id="entity:"+(entity["id"] as? String ?? "")
            for mention in entity["mentions"] as? [JSON] ?? [] { if let source=mention["source"] as? String,chunks[source] != nil { parents[source,default:[]].insert(id) } }
        }
        for (index,id) in parents.keys.sorted().enumerated() {
            let chunk=chunks[id] ?? [:]
            let excerpt=Self.slice(source,start:chunk["start"] as? Int ?? 0,end:chunk["end"] as? Int ?? 0)
            let sourceID="source:"+id,angle = -Double.pi/2+2*Double.pi*(Double(index)+0.5)/Double(max(parents.count,1))
            values.append(KnowledgeNode(id:sourceID,label:"Source "+id,kind:"source",position:Self.at(entityRing+280+CGFloat(index%3)*90,angle),radius:23,detail:"Exact source excerpt\n\n"+excerpt))
            for parent in (parents[id] ?? []).sorted() { connections.append(KnowledgeLink(source:parent,target:sourceID,kind:"source mention",evidence:id)) }
        }
        links=connections;nodes=values;indices=Dictionary(uniqueKeysWithValues:values.enumerated().map { ($0.element.id,$0.offset) })
        for (index,link) in connections.enumerated() { touching[link.source,default:[]].append(index);touching[link.target,default:[]].append(index);if link.kind == "source mention",let group=node(link.source)?.group { sourceGroups[link.target,default:[]].insert(group) } }
    }
    private static func at(_ radius:CGFloat,_ angle:Double) -> CGPoint { CGPoint(x:radius*CGFloat(cos(angle)),y:radius*CGFloat(sin(angle))) }
    private static func groupName(_ category:String) -> String { category == "entity" ? "Projects & entities" : SSPalette.label(category) }
    private static func slice(_ text:String,start:Int,end:Int) -> String {
        let scalars=text.unicodeScalars
        guard start>=0,end>=start,let a=scalars.index(scalars.startIndex,offsetBy:start,limitedBy:scalars.endIndex),let b=scalars.index(scalars.startIndex,offsetBy:end,limitedBy:scalars.endIndex) else { return "" }
        return String(scalars[a..<b])
    }
    func node(_ id:String) -> KnowledgeNode? { indices[id].map { nodes[$0] } }
    func visible(_ node:KnowledgeNode) -> Bool {
        if node.kind == "entity" { return !collapsed.contains(node.group) }
        if node.kind == "source" { return showSources && (sourceGroups[node.id] ?? []).contains { !collapsed.contains($0) } }
        return true
    }
    var visibleNodes:[KnowledgeNode] { nodes.filter(visible) }
    func linked(_ id:String) -> [KnowledgeLink] { (touching[id] ?? []).map { links[$0] } }
    func related(_ id:String) -> [KnowledgeLink] { linked(id).filter{!$0.organizer} }
    func point(_ node:KnowledgeNode,in size:CGSize) -> CGPoint { CGPoint(x:size.width/2+pan.width+node.position.x*zoom,y:size.height/2+pan.height+node.position.y*zoom) }
    func hit(_ point:CGPoint,in size:CGSize) -> KnowledgeNode? { visibleNodes.reversed().first { node in let p=self.point(node,in:size);return hypot(p.x-point.x,p.y-point.y)<=max(12,node.radius*zoom) } }
    func select(_ id:String,focus:Bool=false) {
        guard let node=node(id) else { return };if node.kind == "entity" { collapsed.remove(node.group) };selected=id
        if node.kind == "source" { showSources=true;for link in related(id) { let other=link.source == id ? link.target : link.source;if let group=self.node(other)?.group { collapsed.remove(group) } } }
        if focus { zoom=max(zoom,0.85);pan=CGSize(width:-node.position.x*zoom,height:-node.position.y*zoom) }
    }
    func move(_ id:String,to point:CGPoint) { if let index=indices[id] { nodes[index].position=point } }
    func toggleBranch() { guard node(selected)?.kind == "group" else { return };if collapsed.contains(selected) { collapsed.remove(selected) } else { collapsed.insert(selected) } }
    func zoomBy(_ factor:CGFloat) { let next=min(3.5,max(0.015,zoom*factor));pan=CGSize(width:pan.width*next/zoom,height:pan.height*next/zoom);zoom=next }
    func fit(_ size:CGSize) {
        let visible=visibleNodes
        let left=visible.map{$0.position.x-$0.radius-70}.min() ?? -100,right=visible.map{$0.position.x+$0.radius+70}.max() ?? 100
        let top=visible.map{$0.position.y-$0.radius-45}.min() ?? -100,bottom=visible.map{$0.position.y+$0.radius+45}.max() ?? 100
        zoom=max(0.015,min(1.05,min(max(size.width-80,80)/(right-left),max(size.height-80,80)/(bottom-top))))
        pan=CGSize(width:-(left+right)/2*zoom,height:-(top+bottom)/2*zoom)
    }
}
@MainActor struct KnowledgeCanvas:View {
    @ObservedObject var state:KnowledgeGraphState
    @Binding var viewport:CGSize
    @State private var dragPan:CGSize?
    @State private var dragNode:String?
    @State private var nodeOrigin:CGPoint?
    @State private var pinchZoom:CGFloat?
    var body: some View {
        GeometryReader { geometry in
            Canvas { context,size in
                for x in stride(from:CGFloat(20),to:size.width,by:32) { for y in stride(from:CGFloat(20),to:size.height,by:32) { context.fill(Path(ellipseIn:CGRect(x:x,y:y,width:1.4,height:1.4)),with:.color(SSPalette.brown.opacity(0.12))) } }
                let visible=state.visibleNodes,ids=Set(visible.map(\.id)),related=Set(state.related(state.selected).map{$0.source == state.selected ? $0.target : $0.source})
                let priority=state.linked(state.selected).filter { ids.contains($0.source) && ids.contains($0.target) }
                let rest=state.links.lazy.filter{$0.source != state.selected && $0.target != state.selected && ids.contains($0.source) && ids.contains($0.target)}.prefix(max(0,2500-priority.count))
                for link in (priority+Array(rest)).prefix(2500) {
                    guard let source=state.node(link.source),let target=state.node(link.target) else { continue }
                    let a=state.point(source,in:size),b=state.point(target,in:size)
                    let active=link.source == state.selected || link.target == state.selected
                    var path=Path();path.move(to:a)
                    if link.organizer { path.addLine(to:b) } else { path.addQuadCurve(to:b,control:CGPoint(x:(a.x+b.x)/2+(a.y-b.y)*0.1,y:(a.y+b.y)/2+(b.x-a.x)*0.1)) }
                    context.stroke(path,with:.color(SSPalette.brown.opacity(active ? 0.85 : link.organizer ? 0.4 : 0.22)),style:StrokeStyle(lineWidth:active ? 1.8 : 1,dash:link.organizer ? [5,5] : []))
                }
                for node in visible {
                    let point=state.point(node,in:size),radius=max(node.kind == "root" ? 28 : 9,node.radius*state.zoom),solid=node.kind == "root" || node.id == state.selected
                    let rect=CGRect(x:point.x-radius,y:point.y-radius,width:radius*2,height:radius*2)
                    if node.id == state.selected || related.contains(node.id) { context.stroke(Path(ellipseIn:rect.insetBy(dx:-5,dy:-5)),with:.color(SSPalette.brown),lineWidth:node.id == state.selected ? 2 : 1) }
                    context.fill(Path(ellipseIn:rect),with:.color(solid ? SSPalette.brown : SSPalette.eggshell));context.stroke(Path(ellipseIn:rect),with:.color(SSPalette.brown),lineWidth:node.kind == "group" ? 1.6 : 1)
                    if node.kind == "root" { context.draw(Text("Knowledge\nbase").font(.system(size:max(11,min(18,22*state.zoom)),weight:.semibold,design:.serif)).foregroundColor(SSPalette.eggshell),at:point) }
                    else {
                        context.fill(Path(ellipseIn:CGRect(x:point.x-3,y:point.y-3,width:6,height:6)),with:.color(solid ? SSPalette.eggshell : SSPalette.brown))
                        let label=node.label.count>30 ? String(node.label.prefix(29))+"…" : node.label
                        context.draw(Text(label).font(.system(size:max(8,min(10.5,10*state.zoom)))).foregroundColor(SSPalette.brown),at:CGPoint(x:point.x,y:point.y+radius+15))
                    }
                }
            }.background(SSPalette.eggshell).contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance:2).onChanged { value in
                    if dragPan == nil { dragPan=state.pan;let node=state.hit(value.startLocation,in:geometry.size);dragNode=node?.id;nodeOrigin=node?.position }
                    if let id=dragNode,let origin=nodeOrigin { state.move(id,to:CGPoint(x:origin.x+value.translation.width/state.zoom,y:origin.y+value.translation.height/state.zoom)) }
                    else if let origin=dragPan { state.pan=CGSize(width:origin.width+value.translation.width,height:origin.height+value.translation.height) }
                }.onEnded { _ in dragPan=nil;dragNode=nil;nodeOrigin=nil })
                .simultaneousGesture(SpatialTapGesture().onEnded { value in if let node=state.hit(value.location,in:geometry.size) { state.select(node.id) } })
                .simultaneousGesture(SpatialTapGesture(count:2).onEnded { value in if let node=state.hit(value.location,in:geometry.size) { state.select(node.id,focus:node.kind != "group");if node.kind == "group" { state.toggleBranch() } } })
                .simultaneousGesture(MagnificationGesture().onChanged { value in if pinchZoom == nil { pinchZoom=state.zoom };state.zoom=min(3.5,max(0.015,(pinchZoom ?? state.zoom)*value)) }.onEnded { _ in pinchZoom=nil })
                .onAppear { viewport=geometry.size;state.fit(geometry.size) }.onChange(of:geometry.size) { viewport=$0 }
                .accessibilityLabel("Knowledge graph. Use the searchable node list to inspect entities and sources.")
        }
    }
}
@MainActor struct KnowledgeGraphView:View {
    @StateObject private var state:KnowledgeGraphState
    @State private var query=""
    @State private var viewport=CGSize(width:900,height:650)
    init(graph:JSON,source:String) { _state=StateObject(wrappedValue:KnowledgeGraphState(graph:graph,source:source)) }
    var body: some View {
        VStack(alignment:.leading,spacing:16) {
            Text("Your knowledge, connected").font(.system(size:30,weight:.medium,design:.serif))
            HStack {
                TextField("Find a node",text:$query).frame(width:220)
                Button { if let node=state.nodes.first(where:{$0.label.localizedCaseInsensitiveContains(query)}) { state.select(node.id,focus:true) } } label:{Label("Find node",systemImage:"magnifyingglass")}
                Button { state.zoomBy(1.25) } label:{Label("Zoom in",systemImage:"plus.magnifyingglass")}
                Button { state.zoomBy(0.8) } label:{Label("Zoom out",systemImage:"minus.magnifyingglass")}
                Button { state.fit(viewport) } label:{Label("Fit graph",systemImage:"arrow.up.left.and.arrow.down.right")}
                Button { state.toggleBranch() } label:{Label(state.collapsed.contains(state.selected) ? "Expand branch" : "Collapse branch",systemImage:"point.3.connected.trianglepath.dotted")}.disabled(state.node(state.selected)?.kind != "group")
                Toggle("Source nodes",isOn:$state.showSources).onChange(of:state.showSources) { shown in if !shown { state.select("review");state.fit(viewport) } else if state.node(state.selected)?.kind != "source" { state.fit(viewport) } }
            }
            HStack(alignment:.top,spacing:18) {
                KnowledgeCanvas(state:state,viewport:$viewport).clipShape(RoundedRectangle(cornerRadius:24)).overlay(RoundedRectangle(cornerRadius:24).stroke(SSPalette.brown,lineWidth:1))
                VStack(alignment:.leading,spacing:12) {
                    Text("Explore nodes").font(.headline)
                    ScrollView { LazyVStack(alignment:.leading,spacing:4) {
                        ForEach(state.nodes.filter { query.isEmpty || $0.label.localizedCaseInsensitiveContains(query) }) { node in
                            Button { state.select(node.id,focus:true) } label:{Text(node.label).lineLimit(2).frame(maxWidth:.infinity,alignment:.leading)}
                        }
                    } }.frame(height:160)
                    if let node=state.node(state.selected) {
                        ScrollView {
                            VStack(alignment:.leading,spacing:12) {
                                Text(node.label).font(.system(size:22,weight:.medium,design:.serif)).textSelection(.enabled)
                                Text(node.detail).textSelection(.enabled)
                                ForEach(Array(state.related(node.id).prefix(120).enumerated()),id:\.offset) { _,link in
                                    let other=link.source == node.id ? link.target : link.source
                                    Text((state.node(other)?.label ?? "")+"\n"+link.kind.replacingOccurrences(of:"_",with:" ")+(link.evidence.isEmpty ? "" : " · "+link.evidence)).font(.caption).textSelection(.enabled)
                                }
                            }.frame(maxWidth:.infinity,alignment:.leading)
                        }
                    }
                }.frame(width:280)
            }
            Text("\(state.entityCount) entities / \(state.relationshipCount) detected relationships / \(Int(state.zoom*100))% zoom").font(.callout)
            Text("Solid links: recorded relationships and source mentions. Dashed branches: display groups, not new evidence. Drag nodes to arrange them; drag empty space to pan. Pinch or use zoom controls. Up to 2,500 links are drawn at once, with selected-node links first.").font(.caption)
            if state.entityCount == 0 { Text("No entities were extracted. Analyze named projects or content with supported findings to build a graph.").font(.callout) }
        }.padding(24).frame(minWidth:1120,minHeight:760).background(SSPalette.eggshell).foregroundStyle(SSPalette.brown).tint(SSPalette.brown).buttonStyle(SSButtonStyle()).textFieldStyle(SSTextFieldStyle()).preferredColorScheme(.light)
    }
}
@MainActor final class KnowledgeWindowController:NSWindowController {
    init(graph:JSON,source:String) {
        let window=NSWindow(contentRect:NSRect(x:0,y:0,width:1380,height:900),styleMask:[.titled,.closable,.miniaturizable,.resizable],backing:.buffered,defer:false)
        window.title="ShareSafe · Knowledge base · Current review";window.backgroundColor=SSPalette.nsEggshell;window.isReleasedWhenClosed=false
        window.contentView=NSHostingView(rootView:KnowledgeGraphView(graph:graph,source:source));super.init(window:window);window.center()
    }
    required init?(coder:NSCoder) { fatalError("Storyboard initialization is not supported") }
}
@MainActor extension AppModel {
    func openKnowledgeBase() {
        guard session != nil else { error="Check your content first to build its knowledge graph.";return }
        if let controller=knowledgeWindow,controller.window?.isVisible == true { controller.window?.makeKeyAndOrderFront(nil);return }
        let controller=KnowledgeWindowController(graph:result["graph"] as? JSON ?? [:],source:result["text"] as? String ?? "")
        knowledgeWindow=controller;controller.showWindow(nil);controller.window?.makeKeyAndOrderFront(nil)
    }
}
