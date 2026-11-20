import AppKit
import SwiftUI
import VsLikeKit

/// Keeps one editor (scroll view + text view) per open document so that switching tabs keeps scroll position,
/// selection and undo.
@MainActor
final class EditorRegistry {
    static let shared = EditorRegistry()
    private var editors: [UUID: (scroll: NSScrollView, text: CodeTextView)] = [:]

    func editor(for document: CodeDocument, workspace: Workspace) -> (scroll: NSScrollView, text: CodeTextView) {
        if let existing = editors[document.id] {
            return existing
        }
        let created = CodeTextView.make(document: document, workspace: workspace)
        editors[document.id] = created
        return created
    }

    func prune(keeping documents: [CodeDocument]) {
        let alive = Set(documents.map(\.id))
        for id in editors.keys where !alive.contains(id) {
            editors[id]?.text.document?.storage.layoutManagers.forEach { $0.textStorage?.removeLayoutManager($0) }
            editors[id] = nil
        }
    }
}

/// SwiftUI wrapper of the AppKit editor. Diagnostics, execution line and pending navigation come in as values so
/// that SwiftUI's observation updates the view when they change.
struct EditorHost: NSViewRepresentable {
    let document: CodeDocument
    let workspace: Workspace
    let diagnostics: [Diagnostic]
    let executionLine: Int?
    let pendingSelection: NSRange?
    let fontSize: Double

    func makeNSView(context: Context) -> NSView {
        let container = NSView()
        attach(to: container)
        return container
    }

    func updateNSView(_ container: NSView, context: Context) {
        let (scroll, text) = EditorRegistry.shared.editor(for: document, workspace: workspace)
        if scroll.superview !== container {
            attach(to: container)
        }
        text.applyFont()
        text.setDiagnostics(diagnostics)
        text.needsDisplay = true
        scroll.verticalRulerView?.needsDisplay = true
        if let pendingSelection {
            document.pendingSelection = nil
            DispatchQueue.main.async {
                text.window?.makeFirstResponder(text)
                text.reveal(pendingSelection)
            }
        }
    }

    private func attach(to container: NSView) {
        container.subviews.forEach { $0.removeFromSuperview() }
        let (scroll, text) = EditorRegistry.shared.editor(for: document, workspace: workspace)
        scroll.removeFromSuperview()
        scroll.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(scroll)
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: container.topAnchor),
            scroll.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
        workspace.activeEditor = text
        DispatchQueue.main.async {
            if text.window?.firstResponder == nil || !(text.window?.firstResponder is NSTextView) {
                text.window?.makeFirstResponder(text)
            }
        }
    }
}

/// The editor area for one code document, with a breadcrumb bar above the text.
struct CodeEditorPane: View {
    @Environment(Workspace.self) private var workspace
    @Environment(Preferences.self) private var preferences
    let document: CodeDocument

    var body: some View {
        VStack(spacing: 0) {
            Breadcrumb(document: document)
            Divider().opacity(0.5)
            EditorHost(
                document: document,
                workspace: workspace,
                diagnostics: workspace.diagnostics.diagnostics(for: document.path),
                executionLine: document.executionLine,
                pendingSelection: document.pendingSelection,
                fontSize: preferences.fontSize
            )
        }
        .background(Color(nsColor: Palette.editorBackground))
    }
}

/// Project › folders › file, plus diagnostics counts and the language of the file.
struct Breadcrumb: View {
    @Environment(Workspace.self) private var workspace
    let document: CodeDocument

    private var parts: [String] {
        guard let root = workspace.rootURL else { return [document.name] }
        let relative = document.path.hasPrefix(root.path + "/") ? String(document.path.dropFirst(root.path.count + 1)) : document.name
        return relative.split(separator: "/").map(String.init)
    }

    var body: some View {
        let problems = workspace.diagnostics.diagnostics(for: document.path)
        HStack(spacing: 4) {
            ForEach(Array(parts.enumerated()), id: \.offset) { index, part in
                if index > 0 {
                    Image(systemName: "chevron.compact.right").font(.caption2).foregroundStyle(.tertiary)
                }
                Text(part)
                    .foregroundStyle(index == parts.count - 1 ? .primary : .secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 12)
            let errors = problems.filter { $0.severity == .error }.count
            let warnings = problems.filter { $0.severity == .warning }.count
            if errors > 0 {
                Label("\(errors)", systemImage: "xmark.octagon.fill").foregroundStyle(Color.vsError)
            }
            if warnings > 0 {
                Label("\(warnings)", systemImage: "exclamationmark.triangle.fill").foregroundStyle(Color.vsWarning)
            }
            Text(document.languageName).foregroundStyle(.secondary)
        }
        .font(.system(size: 11))
        .labelStyle(.titleAndIcon)
        .padding(.horizontal, 12)
        .frame(height: 26)
    }
}
