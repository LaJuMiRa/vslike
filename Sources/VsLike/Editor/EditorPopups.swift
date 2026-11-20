import AppKit
import SwiftUI
import VsLikeKit

/// A borderless child panel that floats next to the caret without taking focus from the editor.
@MainActor
class PopupPanel: NSPanel {
    init(content: NSView) {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 300, height: 200), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        isFloatingPanel = true
        level = .popUpMenu
        hasShadow = true
        backgroundColor = .clear
        isOpaque = false
        hidesOnDeactivate = true
        let effect = NSVisualEffectView()
        effect.material = .popover
        effect.state = .active
        effect.wantsLayer = true
        effect.layer?.cornerRadius = 10
        effect.layer?.masksToBounds = true
        effect.layer?.borderWidth = 0.5
        effect.layer?.borderColor = NSColor.separatorColor.cgColor
        content.translatesAutoresizingMaskIntoConstraints = false
        effect.addSubview(content)
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: effect.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: effect.trailingAnchor),
            content.topAnchor.constraint(equalTo: effect.topAnchor),
            content.bottomAnchor.constraint(equalTo: effect.bottomAnchor),
        ])
        contentView = effect
    }

    override var canBecomeKey: Bool { false }

    /// Shows the panel below (or above, near the screen bottom) a rect in screen coordinates.
    func show(near anchor: NSRect, size: NSSize, parent: NSWindow, above: Bool = false) {
        var origin = NSPoint(x: anchor.minX, y: above ? anchor.maxY + 4 : anchor.minY - size.height - 4)
        if let screen = parent.screen?.visibleFrame {
            if origin.y < screen.minY { origin.y = anchor.maxY + 4 }
            if origin.y + size.height > screen.maxY { origin.y = anchor.minY - size.height - 4 }
            origin.x = min(max(screen.minX + 4, origin.x), screen.maxX - size.width - 4)
        }
        setFrame(NSRect(origin: origin, size: size), display: true)
        if self.parent == nil {
            parent.addChildWindow(self, ordered: .above)
        }
        orderFront(nil)
    }

    func dismiss() {
        parent?.removeChildWindow(self)
        orderOut(nil)
    }
}

/// The completion list: SF Symbol per kind, name, inline description; the selected item's documentation below.
@MainActor
final class CompletionPopup: NSObject, NSTableViewDataSource, NSTableViewDelegate {
    private let table = NSTableView()
    private let scroll = NSScrollView()
    private let detail = NSTextField(wrappingLabelWithString: "")
    private let stack = NSStackView()
    private lazy var panel = PopupPanel(content: stack)
    private(set) var items: [CompletionItem] = []
    var onCommit: ((CompletionItem) -> Void)?
    var onSelectionChange: ((CompletionItem) -> Void)?

    override init() {
        super.init()
        let column = NSTableColumn(identifier: .init("item"))
        table.addTableColumn(column)
        table.headerView = nil
        table.rowHeight = 22
        table.intercellSpacing = NSSize(width: 0, height: 0)
        table.backgroundColor = .clear
        table.style = .plain
        table.selectionHighlightStyle = .regular
        table.dataSource = self
        table.delegate = self
        table.target = self
        table.doubleAction = #selector(doubleClicked)
        scroll.documentView = table
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.scrollerStyle = .overlay
        detail.font = .systemFont(ofSize: 11)
        detail.textColor = .secondaryLabelColor
        detail.maximumNumberOfLines = 4
        detail.isHidden = true
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 0
        stack.edgeInsets = NSEdgeInsets(top: 4, left: 0, bottom: 4, right: 0)
        stack.addArrangedSubview(scroll)
        stack.addArrangedSubview(detail)
        scroll.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        detail.leadingAnchor.constraint(equalTo: stack.leadingAnchor, constant: 10).isActive = true
        detail.trailingAnchor.constraint(equalTo: stack.trailingAnchor, constant: -10).isActive = true
    }

    var isVisible: Bool { panel.isVisible }

    var selected: CompletionItem? {
        let row = table.selectedRow
        return row >= 0 && row < items.count ? items[row] : nil
    }

    func show(_ items: [CompletionItem], near anchor: NSRect, in window: NSWindow) {
        guard !items.isEmpty else {
            dismiss()
            return
        }
        self.items = items
        table.reloadData()
        let preselected = items.firstIndex(where: \.isPreselected) ?? 0
        table.selectRowIndexes([preselected], byExtendingSelection: false)
        table.scrollRowToVisible(preselected)
        let longest = items.prefix(200).map { ($0.displayText as NSString).size(withAttributes: [.font: NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)]).width + CGFloat(($0.inlineDescription ?? "").count) * 6.5 }.max() ?? 200
        let width = min(560, max(280, longest + 70))
        let rows = CGFloat(min(items.count, 10))
        let height = rows * table.rowHeight + 8 + (detail.isHidden ? 0 : 52)
        panel.show(near: anchor, size: NSSize(width: width, height: height), parent: window)
        if let item = selected { onSelectionChange?(item) }
    }

    func setDetail(_ text: String?) {
        let hasText = !(text ?? "").isEmpty
        detail.stringValue = text ?? ""
        if detail.isHidden == hasText {
            detail.isHidden = !hasText
            var frame = panel.frame
            let delta: CGFloat = hasText ? 52 : -52
            frame.size.height += delta
            frame.origin.y -= delta
            panel.setFrame(frame, display: true)
        }
    }

    func move(_ delta: Int) {
        guard !items.isEmpty else { return }
        let row = max(0, min(items.count - 1, table.selectedRow + delta))
        table.selectRowIndexes([row], byExtendingSelection: false)
        table.scrollRowToVisible(row)
    }

    func dismiss() {
        panel.dismiss()
        detail.isHidden = true
    }

    @objc private func doubleClicked() {
        if let item = selected { onCommit?(item) }
    }

    func numberOfRows(in tableView: NSTableView) -> Int { items.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let item = items[row]
        let view = tableView.makeView(withIdentifier: .init("cell"), owner: nil) as? CompletionCell ?? CompletionCell()
        view.identifier = .init("cell")
        view.configure(item)
        return view
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        if let item = selected { onSelectionChange?(item) }
    }
}

private final class CompletionCell: NSTableCellView {
    private let icon = NSImageView()
    private let label = NSTextField(labelWithString: "")
    private let detail = NSTextField(labelWithString: "")

    init() {
        super.init(frame: .zero)
        for view in [icon, label, detail] {
            view.translatesAutoresizingMaskIntoConstraints = false
            addSubview(view)
        }
        label.font = .monospacedSystemFont(ofSize: 12, weight: .regular)
        label.lineBreakMode = .byTruncatingTail
        detail.font = .systemFont(ofSize: 11)
        detail.textColor = .tertiaryLabelColor
        detail.lineBreakMode = .byTruncatingTail
        detail.alignment = .right
        detail.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 8),
            icon.centerYAnchor.constraint(equalTo: centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 16),
            label.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 7),
            label.centerYAnchor.constraint(equalTo: centerYAnchor),
            detail.leadingAnchor.constraint(greaterThanOrEqualTo: label.trailingAnchor, constant: 12),
            detail.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -10),
            detail.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not supported") }

    func configure(_ item: CompletionItem) {
        let (symbol, color) = CompletionCell.symbol(for: item.kind)
        icon.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?.withSymbolConfiguration(.init(pointSize: 12, weight: .semibold))
        icon.contentTintColor = color
        label.stringValue = item.displayText
        detail.stringValue = item.inlineDescription ?? ""
    }

    static func symbol(for kind: CompletionKind) -> (String, NSColor) {
        switch kind {
        case .class: return ("c.square.fill", SyntaxColors.type)
        case .struct: return ("s.square.fill", SyntaxColors.type)
        case .interface: return ("i.square.fill", SyntaxColors.interface)
        case .enum: return ("e.square.fill", SyntaxColors.type)
        case .enumMember, .constant: return ("number.square.fill", SyntaxColors.member)
        case .delegate: return ("d.square.fill", SyntaxColors.type)
        case .method, .extensionMethod: return ("m.square.fill", SyntaxColors.method)
        case .property: return ("p.square.fill", SyntaxColors.member)
        case .field: return ("f.square.fill", SyntaxColors.member)
        case .event: return ("bolt.square.fill", SyntaxColors.member)
        case .local, .parameter: return ("v.square.fill", SyntaxColors.parameter)
        case .namespace: return ("curlybraces.square.fill", SyntaxColors.namespace)
        case .typeParameter: return ("t.square.fill", SyntaxColors.type)
        case .keyword: return ("k.square.fill", SyntaxColors.keyword)
        case .snippet: return ("text.badge.plus", Palette.accent)
        case .other: return ("square.fill", .secondaryLabelColor)
        }
    }
}

/// Signature help and hover information as a small floating card.
@MainActor
final class InfoPopup {
    private let host = NSHostingView(rootView: AnyView(EmptyView()))
    private lazy var panel = PopupPanel(content: host)

    var isVisible: Bool { panel.isVisible }

    func show(_ content: some View, near anchor: NSRect, in window: NSWindow, above: Bool) {
        host.rootView = AnyView(content.padding(.horizontal, 10).padding(.vertical, 7).frame(maxWidth: 620, alignment: .leading).fixedSize(horizontal: false, vertical: true))
        let size = host.fittingSize
        panel.show(near: anchor, size: NSSize(width: min(640, max(160, size.width)), height: size.height), parent: window, above: above)
    }

    func dismiss() {
        panel.dismiss()
    }
}

struct SignatureHelpCard: View {
    let help: SignatureHelp

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            let signature = help.signatures[min(help.activeSignature, help.signatures.count - 1)]
            HStack(spacing: 6) {
                if help.signatures.count > 1 {
                    Text("\(help.activeSignature + 1)/\(help.signatures.count)")
                        .font(.caption2.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 4)
                        .background(.quaternary, in: Capsule())
                }
                signatureText(signature).font(.system(size: 12, design: .monospaced))
            }
            if let documentation = signature.documentation, !documentation.isEmpty {
                Text(documentation).font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func signatureText(_ signature: SignatureInfo) -> Text {
        var result = AttributedString(signature.prefix)
        for (index, parameter) in signature.parameters.enumerated() {
            if index > 0 { result += AttributedString(", ") }
            var part = AttributedString(parameter)
            if index == help.activeParameter {
                part.font = .system(size: 12, weight: .bold, design: .monospaced)
                part.foregroundColor = .vsAccent
            }
            result += part
        }
        result += AttributedString(signature.suffix)
        return Text(result)
    }
}

struct QuickInfoCard: View {
    let signature: String
    let documentation: String?
    let diagnostics: [Diagnostic]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(diagnostics.prefix(4)) { diagnostic in
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Image(systemName: diagnostic.severity == .error ? "xmark.octagon.fill" : diagnostic.severity == .warning ? "exclamationmark.triangle.fill" : "info.circle.fill")
                        .foregroundStyle(Color.severity(diagnostic.severity))
                    Text(diagnostic.message).font(.callout)
                    Text(diagnostic.code).font(.caption.monospaced()).foregroundStyle(.secondary)
                }
            }
            if !diagnostics.isEmpty && !signature.isEmpty {
                Divider()
            }
            if !signature.isEmpty {
                Text(signature).font(.system(size: 12, design: .monospaced)).textSelection(.enabled)
            }
            if let documentation, !documentation.isEmpty {
                Text(documentation).font(.callout).foregroundStyle(.secondary)
            }
        }
    }
}
