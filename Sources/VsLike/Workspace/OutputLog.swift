import Foundation
import Observation
import VsLikeKit

/// The Output panel: build, test, engine and debugger output by channel. Appends are batched per run-loop turn so
/// that fast builds do not redraw the panel for every line.
@MainActor
@Observable
final class OutputLog {
    enum Channel: String, CaseIterable, Identifiable {
        case build, tests, engine, debugger
        var id: String { rawValue }

        var title: String {
            switch self {
            case .build: return String(localized: "Build")
            case .tests: return String(localized: "Tests")
            case .engine: return String(localized: "Analysis")
            case .debugger: return String(localized: "Debugger")
            }
        }
    }

    struct Line: Identifiable, Hashable {
        let id: Int
        let channel: Channel
        let text: String
        let isError: Bool
    }

    static let maxLines = 20_000

    private(set) var lines: [Line] = []
    var selectedChannel: Channel = .build
    @ObservationIgnored private var nextID = 0
    @ObservationIgnored private var pending: [Line] = []
    @ObservationIgnored private var flushScheduled = false

    func lines(in channel: Channel) -> [Line] { lines.filter { $0.channel == channel } }

    func append(_ text: String, channel: Channel, isError: Bool = false) {
        for part in text.split(separator: "\n", omittingEmptySubsequences: false) {
            nextID += 1
            pending.append(Line(id: nextID, channel: channel, text: String(part), isError: isError))
        }
        guard !flushScheduled else {
            return
        }
        flushScheduled = true
        DispatchQueue.main.async { [weak self] in
            MainActor.assumeIsolated { self?.flush() }
        }
    }

    func clear(_ channel: Channel) {
        lines.removeAll { $0.channel == channel }
    }

    func flush() {
        flushScheduled = false
        guard !pending.isEmpty else {
            return
        }
        lines.append(contentsOf: pending)
        pending.removeAll()
        if lines.count > Self.maxLines {
            lines.removeFirst(lines.count - Self.maxLines)
        }
    }

    /// A thread-safe sink for process output.
    nonisolated func sink(_ channel: Channel) -> @Sendable (OutputLine) -> Void {
        { [weak self] line in
            Task { @MainActor in self?.append(line.text, channel: channel, isError: line.isError) }
        }
    }
}
