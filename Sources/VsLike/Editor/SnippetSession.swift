import AppKit
import VsLikeKit

/// Tab stops of an inserted snippet; linked placeholders (same index) are edited together.
@MainActor
final class SnippetSession {
    private(set) var stops: [Int]
    private var placeholders: [ExpandedSnippet.Placeholder]
    private var finalCaret: Int
    private var current = 0

    init(snippet: ExpandedSnippet, base: Int) {
        stops = snippet.stops
        placeholders = snippet.placeholders.map { .init(index: $0.index, start: $0.start + base, length: $0.length) }
        finalCaret = snippet.finalCaret + base
    }

    var firstSelection: NSRange? {
        guard let first = stops.first, let placeholder = placeholders.first(where: { $0.index == first }) else { return nil }
        return NSRange(location: placeholder.start, length: placeholder.length)
    }

    func next(in view: NSTextView) -> Bool {
        current += 1
        if current >= stops.count {
            view.setSelectedRange(NSRange(location: finalCaret, length: 0))
            return true
        }
        select(in: view)
        return true
    }

    func previous(in view: NSTextView) -> Bool {
        guard current > 0 else { return false }
        current -= 1
        select(in: view)
        return true
    }

    private func select(in view: NSTextView) {
        guard let placeholder = placeholders.first(where: { $0.index == stops[current] }) else { return }
        view.setSelectedRange(NSRange(location: placeholder.start, length: placeholder.length))
    }

    /// Keeps placeholder positions up to date while the user types into one.
    func textWillChange(in range: NSRange, replacement: String) {
        let delta = (replacement as NSString).length - range.length
        guard current < stops.count else { return }
        let index = stops[current]
        let active = placeholders.first { $0.index == index && range.location >= $0.start && NSMaxRange(range) <= $0.start + $0.length }
        placeholders = placeholders.map { placeholder in
            if let active, placeholder == active {
                return .init(index: placeholder.index, start: placeholder.start, length: placeholder.length + delta)
            }
            if placeholder.start >= NSMaxRange(range) {
                return .init(index: placeholder.index, start: placeholder.start + delta, length: placeholder.length)
            }
            return placeholder
        }
        if finalCaret >= NSMaxRange(range) {
            finalCaret += delta
        }
    }

    func selectionChanged(_ range: NSRange) {}
}
