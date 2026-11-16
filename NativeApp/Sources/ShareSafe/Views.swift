import SwiftUI
import AppKit

@MainActor struct HighlightedSource: NSViewRepresentable {
    let text: String; let findings: [JSON]
    func makeNSView(context:Context) -> NSScrollView {
        let scroll=NSScrollView();scroll.hasVerticalScroller=true
        let editor=NSTextView();editor.isEditable=false;editor.isSelectable=true;editor.isRichText=true
        editor.isVerticallyResizable=true;editor.isHorizontallyResizable=false;editor.autoresizingMask=[.width]
        editor.textContainer?.widthTracksTextView=true;editor.textContainerInset=NSSize(width:12,height:12)
        scroll.documentView=editor;return scroll
    }
    func updateNSView(_ scroll:NSScrollView,context:Context) {
        guard let editor=scroll.documentView as? NSTextView else { return }
        let storage=NSMutableAttributedString(string:text,attributes:[.font:NSFont.monospacedSystemFont(ofSize:13,weight:.regular),.foregroundColor:SSPalette.nsBrown])
        var offsets=[0];var offset=0
        for scalar in text.unicodeScalars { offset += scalar.value > 0xffff ? 2 : 1; offsets.append(offset) }
        for finding in findings {
            guard let start=finding["start"] as? Int, let end=finding["end"] as? Int, start>=0,end>start,end<offsets.count else { continue }
            let range=NSRange(location:offsets[start],length:offsets[end]-offsets[start])
            let color=(finding["action"] as? String == "block" ? SSPalette.nsBrown : SSPalette.nsBrown).withAlphaComponent(0.14)
            storage.addAttributes([.backgroundColor:color,.toolTip:"\(finding["category"] as? String ?? "") · \(finding["action"] as? String ?? "")"],range:range)
        }
        editor.backgroundColor=SSPalette.nsEggshell;scroll.backgroundColor=SSPalette.nsEggshell
        editor.textStorage?.setAttributedString(storage)
    }
}
@MainActor struct GraphView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:12) {
                Text("See where each suggestion came from. Ask a question to retrieve matching text from your source.").font(.caption).foregroundStyle(SSPalette.brown)
                let graph = model.result["graph"] as? JSON ?? [:]
                let nodes = graph["nodes"] as? [JSON] ?? []
                let edges = graph["edges"] as? [JSON] ?? []
                GraphCanvas(nodes:nodes,edges:edges).frame(height:200)
                DisclosureGroup("Technical graph details") { Text(pretty(graph)).font(.system(.caption,design:.monospaced)).textSelection(.enabled) }
                HStack { TextField("Ask about your source",text:$model.question); Button("Retrieve evidence") { Task { await model.query() } } }
                Text(model.evidence).font(.system(.caption,design:.monospaced)).textSelection(.enabled)
            }.padding()
        }
    }
}
@MainActor struct GraphCanvas: View {
    let nodes: [JSON]; let edges: [JSON]
    var body: some View {
        GeometryReader { geometry in
            Canvas { context, size in
                let limited = Array(nodes.prefix(24))
                func point(_ index:Int) -> CGPoint { let angle = Double(index) / Double(max(limited.count,1)) * .pi * 2; return CGPoint(x:size.width/2+cos(angle)*(size.width/2-85),y:size.height/2+sin(angle)*(size.height/2-30)) }
                let ids = limited.map { String(describing:$0["id"] ?? "") }
                for edge in edges { if let a = ids.firstIndex(of:String(describing:edge["source"] ?? "")), let b = ids.firstIndex(of:String(describing:edge["target"] ?? "")) { var path=Path(); path.move(to:point(a)); path.addLine(to:point(b)); context.stroke(path,with:.color(SSPalette.brown),lineWidth:1) } }
                for (index,node) in limited.enumerated() { let p=point(index); context.fill(Path(ellipseIn:CGRect(x:p.x-5,y:p.y-5,width:10,height:10)),with:.color(SSPalette.brown)); context.draw(Text(String(describing:node["label"] ?? node["name"] ?? node["id"] ?? "Entity")).font(.caption2).foregroundStyle(SSPalette.brown),at:CGPoint(x:p.x,y:p.y+15)) }
            }
        }
    }
}
@MainActor struct RedactionCanvas: View {
    @ObservedObject var model: AppModel
    let image: NSImage
    @State private var drag: CGRect?
    var body: some View {
        GeometryReader { geometry in
            let width = (model.result["width"] as? NSNumber)?.doubleValue ?? 1
            let height = (model.result["height"] as? NSNumber)?.doubleValue ?? 1
            let scale = min(geometry.size.width/width,geometry.size.height/height)
            ZStack(alignment:.topLeading) {
                Image(nsImage:image).resizable().frame(width:width*scale,height:height*scale)
                let changed=Set(model.findings.filter { finding in ["mask","generalize"].contains(finding["action"] as? String ?? "") && (model.decisions[finding["id"] as? String ?? ""] ?? true) }.compactMap { $0["id"] as? String })
                let auto = (model.result["boxes"] as? [JSON] ?? []).filter { changed.contains($0["finding_id"] as? String ?? "") }
                ForEach(Array((auto+model.manualBoxes).enumerated()),id:\.offset) { _,box in
                    let x=(box["x"] as? NSNumber)?.doubleValue ?? 0; let y=(box["y"] as? NSNumber)?.doubleValue ?? 0
                    let w=(box["width"] as? NSNumber)?.doubleValue ?? 0; let h=(box["height"] as? NSNumber)?.doubleValue ?? 0
                    Rectangle().fill(SSPalette.brown).frame(width:w*scale,height:h*scale).offset(x:x*scale,y:y*scale)
                }
                if let drag { Rectangle().stroke(SSPalette.brown,lineWidth:2).frame(width:drag.width,height:drag.height).offset(x:drag.minX,y:drag.minY) }
            }.frame(width:width*scale,height:height*scale).contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance:2).onChanged { value in
                    let a = CGPoint(x:max(0,min(width*scale,value.startLocation.x)),y:max(0,min(height*scale,value.startLocation.y)))
                    let b = CGPoint(x:max(0,min(width*scale,value.location.x)),y:max(0,min(height*scale,value.location.y)))
                    drag = CGRect(x:min(a.x,b.x),y:min(a.y,b.y),width:abs(a.x-b.x),height:abs(a.y-b.y))
                }.onEnded { _ in
                    if let rect=drag, rect.width>1, rect.height>1 { model.manualBoxes.append(["x":rect.minX/scale,"y":rect.minY/scale,"width":rect.width/scale,"height":rect.height/scale]) }; drag=nil
                })
        }
    }
}
