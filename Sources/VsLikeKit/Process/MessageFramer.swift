import Foundation

/// Splits a byte stream into `Content-Length` framed messages (the framing of JSON-RPC in the engine protocol and of
/// the Debug Adapter Protocol). Feed it chunks as they arrive; it returns every complete message body.
public struct MessageFramer: Sendable {
    private var buffer = Data()

    public init() {}

    public static func frame(_ body: Data) -> Data {
        var data = Data("Content-Length: \(body.count)\r\n\r\n".utf8)
        data.append(body)
        return data
    }

    public mutating func append(_ chunk: Data) -> [Data] {
        buffer.append(chunk)
        var messages: [Data] = []
        while let message = nextMessage() {
            messages.append(message)
        }
        return messages
    }

    private mutating func nextMessage() -> Data? {
        let separator = Data("\r\n\r\n".utf8)
        guard let headerEnd = buffer.range(of: separator) else {
            return nil
        }
        let header = String(decoding: buffer[buffer.startIndex..<headerEnd.lowerBound], as: UTF8.self)
        var length: Int?
        for line in header.split(separator: "\r\n") {
            let parts = line.split(separator: ":", maxSplits: 1)
            if parts.count == 2, parts[0].trimmingCharacters(in: .whitespaces).lowercased() == "content-length" {
                length = Int(parts[1].trimmingCharacters(in: .whitespaces))
            }
        }
        guard let length else {
            // Malformed header: drop it so that the stream can recover at the next header.
            buffer.removeSubrange(buffer.startIndex..<headerEnd.upperBound)
            return nextMessage()
        }
        let bodyStart = headerEnd.upperBound
        guard buffer.distance(from: bodyStart, to: buffer.endIndex) >= length else {
            return nil
        }
        let bodyEnd = buffer.index(bodyStart, offsetBy: length)
        let body = Data(buffer[bodyStart..<bodyEnd])
        buffer.removeSubrange(buffer.startIndex..<bodyEnd)
        return body
    }
}
