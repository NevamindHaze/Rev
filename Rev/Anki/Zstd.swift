import Foundation
import libzstd

/// Unpacks data compressed with Zstandard ("zstd").
/// Anki's newest format compresses its database and media files this way.
enum Zstd {
    struct DecompressionError: LocalizedError {
        let message: String
        var errorDescription: String? { "Couldn't unpack the Anki file: \(message)" }
    }

    static func decompress(_ data: Data) throws -> Data {
        guard let context = ZSTD_createDCtx() else {
            throw DecompressionError(message: "out of memory")
        }
        defer { ZSTD_freeDCtx(context) }

        let chunkSize = ZSTD_DStreamOutSize()
        let chunk = UnsafeMutableRawPointer.allocate(byteCount: chunkSize, alignment: 1)
        defer { chunk.deallocate() }

        var output = Data()
        try data.withUnsafeBytes { (source: UnsafeRawBufferPointer) in
            var input = ZSTD_inBuffer(src: source.baseAddress, size: source.count, pos: 0)
            // Keep going until all input is used and the last chunk came out less than full.
            while true {
                var out = ZSTD_outBuffer(dst: chunk, size: chunkSize, pos: 0)
                let code = ZSTD_decompressStream(context, &out, &input)
                if ZSTD_isError(code) != 0 {
                    throw DecompressionError(message: String(cString: ZSTD_getErrorName(code)))
                }
                output.append(chunk.assumingMemoryBound(to: UInt8.self), count: out.pos)
                if input.pos >= input.size && out.pos < out.size { break }
            }
        }
        return output
    }
}
