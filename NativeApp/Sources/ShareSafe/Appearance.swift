import SwiftUI
import AppKit

enum SSPalette {
    static let eggshell = Color(red:247/255, green:242/255, blue:231/255)
    static let brown = Color(red:59/255, green:37/255, blue:28/255)
    static let nsEggshell = NSColor(srgbRed:247/255, green:242/255, blue:231/255, alpha:1)
    static let nsBrown = NSColor(srgbRed:59/255, green:37/255, blue:28/255, alpha:1)
    static func label(_ value:String) -> String {
        ["EMAIL":"Email addresses", "PHONE":"Phone numbers", "AADHAAR":"Aadhaar candidates", "PAN":"PAN candidates", "ACCOUNT":"Bank accounts", "CARD":"Card numbers", "CREDENTIAL":"Passwords & API keys", "PERSON":"People's names", "ADDRESS":"Addresses", "CONFIDENTIAL":"Confidential information", "FINANCIAL":"Financial information", "mask":"Hide the value", "generalize":"Use a general description", "block":"Stop export", "warn":"Review first", "allow":"Keep unchanged"][value] ?? value
    }
}
struct SSButtonStyle: ButtonStyle {
    func makeBody(configuration:Configuration) -> some View {
        configuration.label.font(.system(size:13,weight:.medium)).padding(.horizontal,16).padding(.vertical,10)
            .foregroundStyle(SSPalette.brown).background(SSPalette.eggshell)
            .clipShape(RoundedRectangle(cornerRadius:8))
            .overlay(RoundedRectangle(cornerRadius:8).stroke(SSPalette.brown,lineWidth:configuration.isPressed ? 2 : 1))
    }
}
struct SSPrimaryButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var enabled
    func makeBody(configuration:Configuration) -> some View {
        configuration.label.font(.system(size:13,weight:.semibold)).padding(.horizontal,18).padding(.vertical,11)
            .foregroundStyle(enabled ? SSPalette.eggshell : SSPalette.brown)
            .background(enabled ? SSPalette.brown : SSPalette.eggshell)
            .clipShape(RoundedRectangle(cornerRadius:8))
            .overlay(RoundedRectangle(cornerRadius:8).stroke(SSPalette.brown,style:StrokeStyle(lineWidth:1,dash:enabled ? [] : [3,3])))
    }
}
struct SSInput: ViewModifier {
    func body(content:Content) -> some View {
        content.scrollContentBackground(.hidden).padding(10).background(SSPalette.eggshell)
            .clipShape(RoundedRectangle(cornerRadius:8))
            .overlay(RoundedRectangle(cornerRadius:8).stroke(SSPalette.brown,lineWidth:1))
    }
}
struct SSTextFieldStyle: TextFieldStyle {
    func _body(configuration:TextField<Self._Label>) -> some View {
        configuration.textFieldStyle(.plain).padding(9).background(SSPalette.eggshell)
            .clipShape(RoundedRectangle(cornerRadius:8)).overlay(RoundedRectangle(cornerRadius:8).stroke(SSPalette.brown,lineWidth:1))
    }
}
extension View {
    func ssInput() -> some View { modifier(SSInput()) }
    func ssPrimary() -> some View { buttonStyle(SSPrimaryButtonStyle()) }
}
struct SSCard<Content:View>: View {
    let title:String
    let subtitle:String
    let content:Content
    init(_ title:String, subtitle:String = "", @ViewBuilder content:() -> Content) { self.title=title;self.subtitle=subtitle;self.content=content() }
    var body: some View {
        VStack(alignment:.leading,spacing:14) {
            Text(title).font(.system(size:21,weight:.medium,design:.serif))
            if !subtitle.isEmpty { Text(subtitle).font(.callout).fixedSize(horizontal:false,vertical:true) }
            content
        }.padding(20).frame(maxWidth:.infinity,alignment:.leading).background(SSPalette.eggshell)
            .clipShape(RoundedRectangle(cornerRadius:12)).overlay(RoundedRectangle(cornerRadius:12).stroke(SSPalette.brown,lineWidth:1))
    }
}
struct SSReviewSteps: View {
    let step:Int
    var body: some View {
        HStack(spacing:16) {
            ForEach(Array(["Add content", "Review suggestions", "Save a copy"].enumerated()),id:\.offset) { index,title in
                HStack(spacing:8) { Text("\(index+1)").font(.caption.bold()).frame(width:25,height:25).background(index == step ? SSPalette.brown : SSPalette.eggshell).foregroundStyle(index == step ? SSPalette.eggshell : SSPalette.brown).clipShape(Circle()).overlay(Circle().stroke(SSPalette.brown,lineWidth:1)); Text(title).font(.callout) }
                if index < 2 { Rectangle().fill(SSPalette.brown).frame(width:24,height:1) }
            }
            Spacer(minLength:0)
        }.accessibilityElement(children:.combine)
    }
}
