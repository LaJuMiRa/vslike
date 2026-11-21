import AppKit
import SwiftTerm
import SwiftUI
import VsLikeKit

/// Terminal tabs (login shells and Run sessions) above the active terminal.
struct TerminalPanel: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        let terminals = workspace.terminals
        VStack(spacing: 0) {
            if terminals.sessions.isEmpty {
                ContentUnavailableView {
                    Label("No terminal", systemImage: "terminal")
                } description: {
                    Text("Run starts your program here, so it can read input.")
                } actions: {
                    Button("New Terminal") { terminals.newShell() }
                        .buttonStyle(.borderedProminent)
                }
            } else {
                HStack(spacing: 4) {
                    ForEach(terminals.sessions) { session in
                        TerminalTab(session: session, isActive: session.id == terminals.activeID)
                    }
                    Spacer()
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 4)
                if let active = terminals.active {
                    TerminalHost(session: active)
                        .id(active.id)
                        .padding(.leading, 6)
                }
            }
        }
        .background(Color(nsColor: Palette.editorBackground))
        .onAppear {
            if terminals.sessions.isEmpty { terminals.newShell() }
        }
    }
}

private struct TerminalTab: View {
    @Environment(Workspace.self) private var workspace
    let session: TerminalSession
    let isActive: Bool

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: session.isRun ? "play.fill" : "terminal")
                .font(.system(size: 9))
                .foregroundStyle(session.isRun ? Color.vsAccent : .secondary)
            Text(session.title).lineLimit(1)
            if !session.isRunning {
                Text(session.exitCode.map { "(\($0))" } ?? "").foregroundStyle(session.exitCode == 0 ? Color.vsSuccess : Color.vsError)
            }
            Button {
                workspace.terminals.close(session)
            } label: {
                Image(systemName: "xmark").font(.system(size: 8, weight: .bold))
            }
            .buttonStyle(.plain)
            .foregroundStyle(.secondary)
        }
        .font(.system(size: 11))
        .padding(.horizontal, 8)
        .frame(height: 22)
        .background(isActive ? Color.primary.opacity(0.09) : .clear, in: RoundedRectangle(cornerRadius: 5))
        .contentShape(Rectangle())
        .onTapGesture { workspace.terminals.activeID = session.id }
    }
}

private struct TerminalHost: NSViewRepresentable {
    let session: TerminalSession

    func makeNSView(context: Context) -> NSView {
        let container = NSView()
        embed(in: container)
        return container
    }

    func updateNSView(_ container: NSView, context: Context) {
        if session.view.superview !== container {
            embed(in: container)
        }
        session.applyColors()
    }

    private func embed(in container: NSView) {
        container.subviews.forEach { $0.removeFromSuperview() }
        let view = session.view
        view.removeFromSuperview()
        view.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(view)
        NSLayoutConstraint.activate([
            view.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            view.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            view.topAnchor.constraint(equalTo: container.topAnchor),
            view.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
        DispatchQueue.main.async { view.window?.makeFirstResponder(view) }
    }
}

/// Variables and watch, call stack and threads, and the debug console. The columns share the panel width and
/// never ask for more than it offers (a split view with minimum widths pushed the sidebars out of the window).
struct DebugPanel: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        let debugger = workspace.debugger
        HStack(spacing: 0) {
            VariablesPane(debugger: debugger)
                .frame(minWidth: 0, maxWidth: .infinity)
                .layoutPriority(1)
            Divider()
            CallStackPane(debugger: debugger)
                .frame(minWidth: 0, maxWidth: .infinity)
            Divider()
            DebugConsolePane(debugger: debugger)
                .frame(minWidth: 0, maxWidth: .infinity)
                .layoutPriority(1)
        }
        .frame(minWidth: 0, maxWidth: .infinity)
        .clipped()
    }
}

private struct PaneHeader: View {
    let title: LocalizedStringKey

    var body: some View {
        Text(title)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.secondary)
            .textCase(.uppercase)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
    }
}

private struct VariablesPane: View {
    @Bindable var debugger: DebugController
    @State private var newWatch = ""

    var body: some View {
        VStack(spacing: 0) {
            PaneHeader(title: "Variables")
            List {
                if !debugger.isStopped {
                    Text(debugger.isActive ? "Running…" : "Not debugging").foregroundStyle(.secondary)
                }
                ForEach(debugger.variables) { variable in
                    VariableRow(variable: variable, debugger: debugger)
                }
                Section("Watch") {
                    ForEach($debugger.watches) { $watch in
                        HStack(spacing: 6) {
                            Text(watch.expression).font(.system(size: 11.5, design: .monospaced))
                            Text("=").foregroundStyle(.tertiary)
                            Text(watch.value)
                                .font(.system(size: 11.5, design: .monospaced))
                                .foregroundStyle(watch.isError ? Color.vsError : Color(nsColor: SyntaxColors.string))
                                .lineLimit(1)
                                .textSelection(.enabled)
                            Spacer()
                            Button {
                                debugger.watches.removeAll { $0.id == watch.id }
                            } label: {
                                Image(systemName: "minus.circle")
                            }
                            .buttonStyle(.plain)
                            .foregroundStyle(.secondary)
                        }
                    }
                    TextField("Add expression", text: $newWatch)
                        .textFieldStyle(.plain)
                        .font(.system(size: 11.5, design: .monospaced))
                        .onSubmit {
                            debugger.addWatch(newWatch)
                            newWatch = ""
                        }
                }
            }
            .listStyle(.inset)
        }
    }
}

/// A variable with lazily loaded children.
private struct VariableRow: View {
    let variable: VariableNode
    let debugger: DebugController
    @State private var isExpanded = false
    @State private var children: [VariableNode] = []

    var body: some View {
        if variable.reference > 0 {
            DisclosureGroup(isExpanded: $isExpanded) {
                ForEach(children) { VariableRow(variable: $0, debugger: debugger) }
            } label: {
                label
            }
            .onChange(of: isExpanded) { _, expanded in
                if expanded && children.isEmpty {
                    Task { children = await debugger.children(of: variable) }
                }
            }
        } else {
            label
        }
    }

    private var label: some View {
        HStack(spacing: 6) {
            Text(variable.name).font(.system(size: 11.5, design: .monospaced)).foregroundStyle(Color(nsColor: SyntaxColors.parameter))
            Text(variable.value)
                .font(.system(size: 11.5, design: .monospaced))
                .lineLimit(1)
                .truncationMode(.tail)
                .textSelection(.enabled)
            Spacer(minLength: 4)
            if let type = variable.type {
                Text(type).font(.system(size: 10.5)).foregroundStyle(.tertiary).lineLimit(1)
            }
        }
        .help("\(variable.name) = \(variable.value)")
    }
}

private struct CallStackPane: View {
    @Bindable var debugger: DebugController

    var body: some View {
        VStack(spacing: 0) {
            PaneHeader(title: "Call Stack")
            List(selection: $debugger.selectedFrameID) {
                ForEach(debugger.frames) { frame in
                    VStack(alignment: .leading, spacing: 1) {
                        Text(frame.name).font(.system(size: 11.5, design: .monospaced)).lineLimit(1)
                        if let path = frame.filePath {
                            Text("\(URL(fileURLWithPath: path).lastPathComponent):\(frame.line)").font(.caption2).foregroundStyle(.secondary)
                        } else {
                            Text("External code").font(.caption2).foregroundStyle(.tertiary)
                        }
                    }
                    .tag(frame.id)
                }
                if debugger.threads.count > 1 {
                    Section("Threads") {
                        ForEach(debugger.threads) { thread in
                            Label(thread.name.isEmpty ? "Thread \(thread.id)" : thread.name, systemImage: thread.id == debugger.threadID ? "arrowtriangle.right.fill" : "circle")
                                .font(.caption)
                        }
                    }
                }
            }
            .listStyle(.inset)
        }
    }
}

private struct DebugConsolePane: View {
    let debugger: DebugController
    @State private var expression = ""

    var body: some View {
        VStack(spacing: 0) {
            PaneHeader(title: "Debug Console")
            LogView(lines: debugger.console)
            HStack(spacing: 6) {
                Image(systemName: "chevron.right").foregroundStyle(Color.vsAccent)
                TextField("Evaluate expression", text: $expression)
                    .textFieldStyle(.plain)
                    .font(.system(size: 12, design: .monospaced))
                    .onSubmit {
                        let text = expression
                        expression = ""
                        Task { await debugger.evaluate(text) }
                    }
                    .disabled(!debugger.isStopped)
            }
            .padding(8)
            .background(.bar)
        }
    }
}
