import Foundation
import AppKit
import CryptoKit
import Security
import CFNetwork

typealias JSON = [String: Any]
enum ShareSafeError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let value) = self { return value }; return nil }
}
func pretty(_ value: Any) -> String {
    guard JSONSerialization.isValidJSONObject(value), let data = try? JSONSerialization.data(withJSONObject: value, options: [.prettyPrinted, .sortedKeys]), let text = String(data: data, encoding: .utf8) else { return String(describing: value) }
    return text
}
func object(_ text: String) throws -> JSON {
    guard let data = text.data(using: .utf8), let value = try JSONSerialization.jsonObject(with: data) as? JSON else { throw ShareSafeError.message("Enter a JSON object of private terms and replacements.") }
    return value
}

@MainActor final class Bridge {
    let directory: URL
    let nativeToken: String
    let browserToken: String
    private var process: Process?
    private var logHandle: FileHandle?
    private let session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.connectionProxyDictionary = [kCFNetworkProxiesHTTPEnable as String: 0, kCFNetworkProxiesHTTPSEnable as String: 0, kCFNetworkProxiesSOCKSEnable as String: 0]
        config.httpCookieStorage = nil; config.urlCache = nil
        return URLSession(configuration: config)
    }()
    init() throws {
        directory = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true).appendingPathComponent("ShareSafeMac", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let configURL = directory.appendingPathComponent("bridge.json")
        if let data = try? Data(contentsOf: configURL), let config = try? JSONSerialization.jsonObject(with: data) as? JSON,
           let native = config["native_token"] as? String, let browser = config["browser_token"] as? String, native.count >= 32, browser.count >= 32, native != browser {
            nativeToken = native; browserToken = browser
        } else {
            nativeToken = UUID().uuidString + UUID().uuidString
            browserToken = UUID().uuidString + UUID().uuidString
            let data = try JSONSerialization.data(withJSONObject: ["native_token": nativeToken, "browser_token": browserToken, "port": 8766, "model_port": 11434])
            try data.write(to: configURL, options: .atomic)
        }
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: configURL.path)
    }
    func start() async throws {
        // Reuse an already authenticated service owned by this installation.
        if let health = try? await request("/v1/health"), health["ready"] as? Bool == true { return }
        guard let resources = Bundle.main.resourceURL else { throw ShareSafeError.message("Build the app bundle with scripts/build-macos.sh first.") }
        let binary = resources.appendingPathComponent("Engine/sharesafe-engine/sharesafe-engine")
        guard FileManager.default.isExecutableFile(atPath: binary.path) else { throw ShareSafeError.message("The packaged local engine is missing. Run scripts/build-macos.sh.") }
        let task = Process(); task.executableURL = binary
        task.arguments = ["--config", directory.appendingPathComponent("bridge.json").path, "--data-dir", directory.path, "--parent-pid", String(ProcessInfo.processInfo.processIdentifier)]
        task.environment = ["PATH": NSHomeDirectory() + "/.docker/bin:/Applications/Docker.app/Contents/Resources/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin", "HOME": NSHomeDirectory(), "LANG": "en_US.UTF-8"]
        let log = directory.appendingPathComponent("engine.log")
        FileManager.default.createFile(atPath: log.path, contents: Data(), attributes: [.posixPermissions: 0o600])
        logHandle = try FileHandle(forWritingTo: log); task.standardOutput = logHandle; task.standardError = logHandle
        try task.run(); process = task
        for _ in 0..<40 {
            try await Task.sleep(for: .milliseconds(250))
            if let response = try? await request("/v1/health"), response["ready"] as? Bool == true { return }
            if !task.isRunning { break }
        }
        throw ShareSafeError.message("The local service could not start. Check engine.log in Application Support/ShareSafeMac and whether port 8766 is available.")
    }
    func stop() { process?.terminate(); process = nil; try? logHandle?.close() }
    func request(_ path: String, _ body: JSON? = nil, browser: Bool = false) async throws -> JSON {
        var request = URLRequest(url: URL(string: "http://127.0.0.1:8766" + path)!)
        request.timeoutInterval = path.hasSuffix("/agent") ? 900 : 150
        request.setValue("Bearer " + (browser ? browserToken : nativeToken), forHTTPHeaderField: "Authorization")
        if let body {
            request.httpMethod = "POST"; request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
        }
        let (data, response) = try await session.data(for: request)
        guard let json = try JSONSerialization.jsonObject(with: data) as? JSON else { throw ShareSafeError.message("Invalid response from local service.") }
        guard let response = response as? HTTPURLResponse, response.statusCode == 200 else { throw ShareSafeError.message(json["error"] as? String ?? "Local operation failed.") }
        return json
    }
}

@MainActor final class Vault {
    let directory: URL
    private let key: SymmetricKey
    init(root: URL) throws {
        directory = root.appendingPathComponent("vault", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "org.sharesafe.mac.vault", kSecAttrAccount as String: "device-key", kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecSuccess, let data = result as? Data, data.count == 32 { key = SymmetricKey(data: data) }
        else if status == errSecItemNotFound {
            let generated = SymmetricKey(size: .bits256)
            let data = generated.withUnsafeBytes { Data($0) }
            let add: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "org.sharesafe.mac.vault", kSecAttrAccount as String: "device-key", kSecValueData as String: data, kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
            guard SecItemAdd(add as CFDictionary, nil) == errSecSuccess else { throw ShareSafeError.message("Keychain could not save the vault key.") }
            key = generated
        } else { throw ShareSafeError.message("Unlock the login Keychain to open the vault.") }
    }
    func save(data: Data, name: String) throws {
        let payload = try JSONSerialization.data(withJSONObject: ["name": name, "data": data.base64EncodedString()])
        let id = UUID().uuidString
        let sealed = try AES.GCM.seal(payload, using: key, authenticating: Data(id.utf8))
        guard let combined = sealed.combined else { throw ShareSafeError.message("Encryption failed.") }
        let url = directory.appendingPathComponent(id + ".enc")
        try combined.write(to: url, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
    func list() -> [URL] { ((try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []).filter { $0.pathExtension == "enc" }.sorted { $0.lastPathComponent > $1.lastPathComponent } }
    func read(_ url: URL) throws -> (String, Data) {
        let id = url.deletingPathExtension().lastPathComponent
        let box = try AES.GCM.SealedBox(combined: Data(contentsOf: url))
        let plain = try AES.GCM.open(box, using: key, authenticating: Data(id.utf8))
        guard let json = try JSONSerialization.jsonObject(with: plain) as? JSON, let name = json["name"] as? String, let encoded = json["data"] as? String, let data = Data(base64Encoded: encoded) else { throw ShareSafeError.message("Vault entry is invalid.") }
        return (name, data)
    }
}
