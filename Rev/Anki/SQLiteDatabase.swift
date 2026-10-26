import Foundation
import SQLite3

/// A very small helper around Apple's built-in SQLite library.
/// Inside every .apkg file, Anki keeps its cards in a SQLite database,
/// so we need this to read and write them.
final class SQLiteDatabase {
    enum Value {
        case int(Int64)
        case text(String)
    }

    struct DatabaseError: LocalizedError {
        let message: String
        var errorDescription: String? { "Database error: \(message)" }
    }

    /// One result row. Columns are numbered from 0, in the order of the SELECT.
    struct Row {
        let statement: OpaquePointer

        func int(_ column: Int32) -> Int64 {
            sqlite3_column_int64(statement, column)
        }

        func text(_ column: Int32) -> String {
            guard let chars = sqlite3_column_text(statement, column) else { return "" }
            return String(cString: chars)
        }

        func blob(_ column: Int32) -> Data {
            let count = Int(sqlite3_column_bytes(statement, column))
            guard count > 0, let bytes = sqlite3_column_blob(statement, column) else { return Data() }
            return Data(bytes: bytes, count: count)
        }
    }

    private var handle: OpaquePointer?

    /// Tells SQLite to make its own copy of text we give it.
    private static let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

    init(path: String, readOnly: Bool) throws {
        let flags = readOnly ? SQLITE_OPEN_READONLY : (SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE)
        guard sqlite3_open_v2(path, &handle, flags, nil) == SQLITE_OK else {
            let message = handle.map { String(cString: sqlite3_errmsg($0)) } ?? "could not open file"
            sqlite3_close(handle)
            handle = nil
            throw DatabaseError(message: message)
        }
    }

    deinit {
        sqlite3_close(handle)
    }

    /// Runs one or more SQL statements that return nothing.
    func execute(_ sql: String) throws {
        var errorPointer: UnsafeMutablePointer<CChar>?
        if sqlite3_exec(handle, sql, nil, nil, &errorPointer) != SQLITE_OK {
            let message = errorPointer.map { String(cString: $0) } ?? "unknown error"
            sqlite3_free(errorPointer)
            throw DatabaseError(message: message)
        }
    }

    /// Runs one statement with `?` placeholders filled in from `values`.
    func run(_ sql: String, _ values: [Value]) throws {
        let statement = try prepare(sql)
        defer { sqlite3_finalize(statement) }

        for (index, value) in values.enumerated() {
            let position = Int32(index + 1)
            switch value {
            case .int(let number):
                sqlite3_bind_int64(statement, position, number)
            case .text(let string):
                sqlite3_bind_text(statement, position, string, -1, Self.transient)
            }
        }
        guard sqlite3_step(statement) == SQLITE_DONE else { throw lastError() }
    }

    /// Runs a SELECT and calls `eachRow` for every row.
    func query(_ sql: String, _ eachRow: (Row) throws -> Void) throws {
        let statement = try prepare(sql)
        defer { sqlite3_finalize(statement) }

        while true {
            let result = sqlite3_step(statement)
            if result == SQLITE_ROW {
                try eachRow(Row(statement: statement))
            } else if result == SQLITE_DONE {
                break
            } else {
                throw lastError()
            }
        }
    }

    private func prepare(_ sql: String) throws -> OpaquePointer {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else {
            throw lastError()
        }
        return statement
    }

    private func lastError() -> DatabaseError {
        DatabaseError(message: handle.map { String(cString: sqlite3_errmsg($0)) } ?? "unknown error")
    }
}
