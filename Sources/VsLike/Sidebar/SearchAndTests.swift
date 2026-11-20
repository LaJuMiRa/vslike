import SwiftUI
import VsLikeKit

/// Find and replace in all files.
struct SearchSidebar: View {
    @Environment(Workspace.self) private var workspace
    @Bindable var search: SearchModel
    @State private var showsReplace = false
    @FocusState private var focused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 6) {
                Button {
                    withAnimation(.snappy) { showsReplace.toggle() }
                } label: {
                    Image(systemName: "chevron.right").rotationEffect(.degrees(showsReplace ? 90 : 0))
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                VStack(spacing: 6) {
                    TextField("Search", text: $search.query)
                        .textFieldStyle(.roundedBorder)
                        .focused($focused)
                        .onSubmit { search.run() }
                    if showsReplace {
                        HStack(spacing: 4) {
                            TextField("Replace", text: $search.replacement).textFieldStyle(.roundedBorder)
                            Button {
                                search.replaceAll()
                            } label: {
                                Image(systemName: "arrow.2.squarepath")
                            }
                            .help("Replace All")
                            .disabled(search.results.isEmpty)
                        }
                    }
                }
            }
            HStack(spacing: 4) {
                OptionToggle(symbol: "textformat", help: "Match Case", isOn: $search.caseSensitive)
                OptionToggle(symbol: "text.word.spacing", help: "Whole Word", isOn: $search.wholeWord)
                OptionToggle(symbol: "asterisk", help: "Regular Expression", isOn: $search.useRegex)
                Spacer()
                if search.isSearching {
                    ProgressView().controlSize(.mini)
                } else if !search.results.isEmpty {
                    Text("\(search.matchCount) in \(search.results.count) files").font(.caption).foregroundStyle(.secondary)
                }
            }
            if let error = search.errorText {
                Text(error).font(.caption).foregroundStyle(Color.vsError)
            }
        }
        .padding(.horizontal, 12)
        .onChange(of: search.caseSensitive) { search.run() }
        .onChange(of: search.wholeWord) { search.run() }
        .onChange(of: search.useRegex) { search.run() }
        .onAppear { focused = true }

        List {
            ForEach(search.results) { file in
                Section {
                    ForEach(file.matches) { match in
                        Button {
                            workspace.openFile(URL(fileURLWithPath: file.path), line: match.line, column: match.column, selectLength: match.length)
                        } label: {
                            HStack(alignment: .firstTextBaseline, spacing: 6) {
                                Text("\(match.line)").font(.caption.monospacedDigit()).foregroundStyle(.tertiary).frame(width: 30, alignment: .trailing)
                                Text(match.preview).font(.system(size: 11, design: .monospaced)).lineLimit(1)
                            }
                        }
                        .buttonStyle(.plain)
                    }
                } header: {
                    HStack(spacing: 5) {
                        Image(systemName: FileNode.symbol(forFile: file.path)).foregroundStyle(.secondary)
                        Text(URL(fileURLWithPath: file.path).lastPathComponent)
                        Text("\(file.matches.count)").foregroundStyle(.secondary)
                    }
                }
            }
        }
        .listStyle(.sidebar)
    }
}

private struct OptionToggle: View {
    let symbol: String
    let help: LocalizedStringKey
    @Binding var isOn: Bool

    var body: some View {
        Button {
            isOn.toggle()
        } label: {
            Image(systemName: symbol)
                .frame(width: 24, height: 20)
                .foregroundStyle(isOn ? Color.vsAccent : .secondary)
                .background(isOn ? Color.vsAccent.opacity(0.15) : .clear, in: RoundedRectangle(cornerRadius: 5))
        }
        .buttonStyle(.plain)
        .help(help)
    }
}

/// Test explorer: tree of test projects, namespaces, classes and tests with their outcome.
struct TestExplorer: View {
    @Environment(Workspace.self) private var workspace
    @Bindable var tests: TestController

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Button { tests.run() } label: { Image(systemName: "play.fill") }
                    .help("Run All Tests (⌘U)")
                Button { tests.run(onlyFailed: true) } label: { Image(systemName: "arrow.clockwise") }
                    .help("Run Failed Tests")
                    .disabled(tests.failedCount == 0)
                Button { tests.discover() } label: { Image(systemName: "magnifyingglass") }
                    .help("Discover Tests")
                Spacer()
                if tests.isBusy {
                    ProgressView().controlSize(.mini)
                }
                if tests.totalCount > 0 {
                    HStack(spacing: 6) {
                        Label("\(tests.passedCount)", systemImage: "checkmark.circle.fill").foregroundStyle(Color.vsSuccess)
                        Label("\(tests.failedCount)", systemImage: "xmark.circle.fill").foregroundStyle(tests.failedCount > 0 ? Color.vsError : .secondary)
                    }
                    .font(.caption.monospacedDigit())
                }
            }
            .buttonStyle(.borderless)
            .padding(.horizontal, 14)
            .padding(.bottom, 6)

            if tests.testProjects.isEmpty {
                ContentUnavailableView("No test projects", systemImage: "checkmark.diamond", description: Text("Add an xUnit, NUnit or MSTest project to the solution."))
                    .frame(maxHeight: .infinity)
            } else {
                List(selection: $tests.selectedID) {
                    OutlineGroup(tests.tree, children: \.children) { node in
                        TestRow(node: node)
                            .tag(node.id)
                            .contextMenu {
                                Button("Run", systemImage: "play") { tests.run([node]) }
                                Button("Go to Test", systemImage: "arrow.right.circle") { Task { await tests.open(node) } }
                            }
                    }
                }
                .listStyle(.sidebar)
                if let id = tests.selectedID, let node = find(id, in: tests.tree), let result = tests.result(for: node), result.outcome == .failed {
                    TestFailureDetail(node: node, result: result)
                }
            }
        }
        .onAppear {
            if tests.totalCount == 0 && !tests.testProjects.isEmpty {
                tests.discover()
            }
        }
    }

    private func find(_ id: String, in nodes: [TestNode]) -> TestNode? {
        for node in nodes {
            if node.id == id { return node }
            if let found = find(id, in: node.children ?? []) { return found }
        }
        return nil
    }
}

private struct TestRow: View {
    @Environment(Workspace.self) private var workspace
    let node: TestNode

    var body: some View {
        HStack(spacing: 6) {
            OutcomeIcon(outcome: node.outcome)
            Text(node.name).lineLimit(1)
            Spacer()
            if let result = workspace.tests.result(for: node), result.duration > 0 {
                Text(Duration.seconds(result.duration).formatted(.units(allowed: [.seconds, .milliseconds], width: .narrow)))
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(.tertiary)
            }
            if node.kind == .test || node.kind == .testCase || node.kind == .testClass {
                Button {
                    workspace.tests.run([node])
                } label: {
                    Image(systemName: "play.circle")
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .help("Run")
            }
        }
        .onTapGesture(count: 2) { Task { await workspace.tests.open(node) } }
    }
}

struct OutcomeIcon: View {
    let outcome: TestOutcome

    var body: some View {
        switch outcome {
        case .passed: Image(systemName: "checkmark.circle.fill").foregroundStyle(Color.vsSuccess)
        case .failed: Image(systemName: "xmark.circle.fill").foregroundStyle(Color.vsError)
        case .skipped: Image(systemName: "minus.circle.fill").foregroundStyle(Color.vsWarning)
        case .running: ProgressView().controlSize(.mini).frame(width: 14)
        case .notRun: Image(systemName: "circle.dotted").foregroundStyle(.tertiary)
        }
    }
}

private struct TestFailureDetail: View {
    @Environment(Workspace.self) private var workspace
    let node: TestNode
    let result: TestResult

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Divider()
            Label(node.name, systemImage: "xmark.circle.fill").foregroundStyle(Color.vsError).font(.callout.weight(.medium))
            ScrollView {
                Text(result.errorMessage ?? "")
                    .font(.system(size: 11, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .frame(maxHeight: 120)
            if let frame = TestService.firstSourceFrame(result.stackTrace) {
                Button("\(URL(fileURLWithPath: frame.file).lastPathComponent):\(frame.line)") {
                    workspace.openFile(URL(fileURLWithPath: frame.file), line: frame.line)
                }
                .buttonStyle(.link)
                .font(.caption)
            }
        }
        .padding(.horizontal, 12)
        .padding(.bottom, 8)
    }
}
