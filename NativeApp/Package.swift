// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "ShareSafe", platforms: [.macOS(.v14)], products: [.executable(name: "ShareSafe", targets: ["ShareSafe"])], targets: [.executableTarget(name: "ShareSafe")], swiftLanguageVersions: [.v5])
