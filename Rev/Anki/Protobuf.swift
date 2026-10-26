import Foundation

/// Reads the few bits of Google's "protobuf" format that Anki's newest files use.
/// A protobuf message is a list of numbered fields; we only need numbers, text and nested bytes.
struct ProtobufMessage {
    private(set) var varints: [Int: [UInt64]] = [:]
    private(set) var blobs: [Int: [Data]] = [:]

    init(_ data: Data) {
        let bytes = [UInt8](data)
        var index = 0

        func readVarint() -> UInt64? {
            var result: UInt64 = 0
            var shift: UInt64 = 0
            while index < bytes.count, shift < 64 {
                let byte = bytes[index]
                index += 1
                result |= UInt64(byte & 0x7F) << shift
                if byte & 0x80 == 0 { return result }
                shift += 7
            }
            return nil
        }

        while index < bytes.count {
            guard let key = readVarint() else { break }
            let field = Int(key >> 3)
            switch key & 0x7 {
            case 0:   // a number
                guard let value = readVarint() else { return }
                varints[field, default: []].append(value)
            case 1:   // 8 fixed bytes (not needed)
                index += 8
            case 2:   // text, bytes or a nested message
                guard let length = readVarint(), length <= UInt64(bytes.count - index) else { return }
                blobs[field, default: []].append(Data(bytes[index..<(index + Int(length))]))
                index += Int(length)
            case 5:   // 4 fixed bytes (not needed)
                index += 4
            default:
                return   // a format we don't understand; stop safely
            }
        }
    }

    func string(_ field: Int) -> String? {
        blobs[field]?.first.flatMap { String(data: $0, encoding: .utf8) }
    }

    func number(_ field: Int) -> UInt64? {
        varints[field]?.first
    }

    func messages(_ field: Int) -> [ProtobufMessage] {
        (blobs[field] ?? []).map { ProtobufMessage($0) }
    }
}
