import Foundation

/// Talks to the Rev Sync add-on in Anki on your computer over your home Wi-Fi
/// (the add-on's side is anki-addon/rev_sync/wifi.py).
///
/// iCloud can take many minutes to move the sync files. On the same Wi-Fi, Rev hands its file to the
/// add-on and gets Anki's file back in one request, so a sync takes a second or two. The files and the
/// sync rules are exactly the same as through iCloud. If the computer doesn't answer, SyncService uses
/// the iCloud folder instead, as before.
///
/// Every request carries the pairing code shown in Anki (Tools → Rev Sync Wi-Fi…).
struct WifiSync {
    struct WifiError: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    /// What the add-on says when Rev checks it's there.
    struct Hello: Decodable {
        let app: String
        let computer: String
    }

    private struct SyncAnswer: Decodable { let anki: SyncSnapshot }
    private struct ErrorAnswer: Decodable { let error: String }

    static let defaultPort = 8765

    /// What you typed in Settings: "192.168.0.7:8765" or "DESKTOP-1234.local:8765".
    let address: String
    let code: String

    private static let session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.waitsForConnectivity = false   // no Wi-Fi: fail at once, so iCloud can take over
        configuration.timeoutIntervalForResource = 180
        return URLSession(configuration: configuration)
    }()

    /// "http://192.168.0.7:8765/rev-sync/v1" for what you typed (without a port, 8765 is used).
    static func baseURL(for address: String) -> URL? {
        var text = address.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.lowercased().hasPrefix("http://") { text.removeFirst(7) }
        while text.hasSuffix("/") { text.removeLast() }
        guard !text.isEmpty, !text.contains("/"), !text.contains(" "), !text.contains("@") else { return nil }
        let parts = text.split(separator: ":", omittingEmptySubsequences: false)
        if parts.count > 2 { return nil }
        if parts.count == 2 {
            guard let port = Int(parts[1]), (1...65535).contains(port) else { return nil }
        }
        return URL(string: "http://\(text)\(parts.count == 2 ? "" : ":\(defaultPort)")/rev-sync/v1")
    }

    /// The same computer by its name ("DESKTOP-1234.local:8765"), for when its number changes.
    func byName(_ computer: String) -> WifiSync? {
        guard let base = Self.baseURL(for: address), let port = base.port, !computer.isEmpty else { return nil }
        let candidate = WifiSync(address: "\(computer).local:\(port)", code: code)
        return candidate.address == address ? nil : candidate
    }

    // MARK: Requests

    /// Checks that this is Anki's Rev Sync and the code is right. Gives up after a few seconds.
    func hello() async throws -> Hello {
        let data = try await send(request(["hello"], timeout: 4))
        let hello = try JSONDecoder().decode(Hello.self, from: data)
        guard hello.app == "rev-sync" else { throw WifiError(message: "That address isn't Anki's Rev Sync.") }
        return hello
    }

    /// Sends Rev's file and returns Anki's file (the add-on applies Rev's changes first).
    func sync(_ revFile: Data) async throws -> SyncSnapshot {
        let data = try await send(request(["sync"], method: "POST", body: revFile, timeout: 120))
        return try JSONDecoder().decode(SyncAnswer.self, from: data).anki
    }

    /// Gives Anki a picture a Rev card uses (Anki never replaces a picture it already has).
    func sendImage(_ image: Data, named name: String) async throws {
        _ = try await send(request(["media", name], method: "PUT", body: image, timeout: 60))
    }

    /// A picture an Anki card uses, or nil if Anki doesn't have it.
    func image(named name: String) async -> Data? {
        guard let imageRequest = try? request(["media", name], timeout: 60) else { return nil }
        return try? await send(imageRequest)
    }

    private func request(_ path: [String], method: String = "GET", body: Data? = nil,
                         timeout: TimeInterval) throws -> URLRequest {
        guard var url = Self.baseURL(for: address) else {
            throw WifiError(message: "“\(address)” isn't a computer address. It looks like 192.168.0.7:8765.")
        }
        for part in path { url.appendPathComponent(part) }
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: timeout)
        request.httpMethod = method
        request.httpBody = body
        request.setValue(code, forHTTPHeaderField: "X-Rev-Code")
        if body != nil { request.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type") }
        return request
    }

    private func send(_ request: URLRequest) async throws -> Data {
        let (data, response) = try await Self.session.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw WifiError(message: "No answer from the computer.") }
        switch http.statusCode {
        case 200..<300:
            return data
        case 401:
            throw WifiError(message: "The pairing code is wrong. You can see it in Anki: Tools → Rev Sync Wi-Fi…")
        case 429:
            throw WifiError(message: "Too many wrong pairing codes. Wait a minute, then try again.")
        default:
            let reason = (try? JSONDecoder().decode(ErrorAnswer.self, from: data))?.error ?? "error \(http.statusCode)"
            throw WifiError(message: "Anki couldn't sync over Wi-Fi: \(reason).")
        }
    }

    /// A short explanation for Settings.
    static func explain(_ error: Error) -> String {
        if let error = error as? WifiError { return error.message }
        if error is URLError {
            return "Rev couldn't reach the computer. Check that Anki is open with Wi-Fi sync switched on "
                + "(Tools → Rev Sync Wi-Fi…), that the iPhone is on the same Wi-Fi, and that Rev may use the "
                + "local network (iPhone Settings → Privacy & Security → Local Network)."
        }
        return error.localizedDescription
    }
}
