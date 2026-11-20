import Foundation
import Observation
import VsLikeKit

/// A node of the test tree: project › namespace › class › test (› data-driven case).
struct TestNode: Identifiable, Hashable {
    enum Kind: Hashable { case project, namespace, testClass, test, testCase }

    let id: String
    let name: String
    let kind: Kind
    let project: String
    /// Full test name for tests and cases, the prefix (`Ns.Class.`) for groups.
    let fullName: String
    var outcome: TestOutcome
    var children: [TestNode]?
}

@MainActor
@Observable
final class TestController {
    @ObservationIgnored private unowned let workspace: Workspace
    @ObservationIgnored private var task: Task<Void, Never>?

    private(set) var testProjects: [SolutionProject] = []
    private(set) var tests: [String: [String]] = [:]
    private(set) var results: [String: TestResult] = [:]
    private(set) var running: Set<String> = []
    private(set) var isBusy = false
    var selectedID: String?

    init(workspace: Workspace) {
        self.workspace = workspace
    }

    var passedCount: Int { results.values.filter { $0.outcome == .passed }.count }
    var failedCount: Int { results.values.filter { $0.outcome == .failed }.count }
    var totalCount: Int { tests.values.map(\.count).reduce(0, +) }

    func reloadProjects() {
        testProjects = (workspace.solution?.projects ?? []).filter { (try? ProjectFile.load($0.filePath))?.isTestProject == true }
        tests = tests.filter { key, _ in testProjects.contains { $0.filePath == key } }
    }

    var tree: [TestNode] {
        testProjects.map { project in
            let names = tests[project.filePath] ?? []
            var namespaces: [String: [String: [String]]] = [:]
            for name in names {
                let parsed = TestName(name)
                namespaces[parsed.namespace, default: [:]][parsed.className, default: []].append(name)
            }
            let children = namespaces.keys.sorted().map { namespace -> TestNode in
                let classes = namespaces[namespace]!.keys.sorted().map { className -> TestNode in
                    let classFull = namespace.isEmpty ? className : namespace + "." + className
                    var methods: [String: [String]] = [:]
                    for name in namespaces[namespace]![className]! {
                        methods[TestName(name).methodFullName, default: []].append(name)
                    }
                    let tests = methods.keys.sorted().map { method -> TestNode in
                        let cases = methods[method]!
                        if cases.count == 1 && cases[0] == method {
                            return TestNode(id: project.filePath + "|" + method, name: TestName(method).method, kind: .test, project: project.filePath, fullName: method, outcome: outcome(of: [method]))
                        }
                        let caseNodes = cases.sorted().map { name in
                            TestNode(id: project.filePath + "|" + name, name: TestName(name).method + TestName(name).arguments, kind: .testCase, project: project.filePath, fullName: name, outcome: outcome(of: [name]))
                        }
                        return TestNode(id: project.filePath + "|" + method, name: TestName(method).method, kind: .test, project: project.filePath, fullName: method + "(", outcome: outcome(of: cases), children: caseNodes)
                    }
                    let all = namespaces[namespace]![className]!
                    return TestNode(id: project.filePath + "|" + classFull, name: className, kind: .testClass, project: project.filePath, fullName: classFull + ".", outcome: outcome(of: all), children: tests)
                }
                let all = namespaces[namespace]!.values.flatMap { $0 }
                return TestNode(id: project.filePath + "|ns:" + namespace, name: namespace.isEmpty ? "–" : namespace, kind: .namespace, project: project.filePath, fullName: namespace + ".", outcome: outcome(of: all), children: classes)
            }
            return TestNode(id: project.filePath, name: project.name, kind: .project, project: project.filePath, fullName: "", outcome: outcome(of: names), children: children)
        }
    }

    private func outcome(of names: [String]) -> TestOutcome {
        if names.contains(where: running.contains) { return .running }
        let outcomes = names.compactMap { results[$0]?.outcome }
        if outcomes.contains(.failed) { return .failed }
        if outcomes.isEmpty { return .notRun }
        if outcomes.count < names.count { return .notRun }
        return outcomes.allSatisfy { $0 == .skipped } ? .skipped : .passed
    }

    func result(for node: TestNode) -> TestResult? {
        results[node.fullName]
    }

    func discover() {
        guard !isBusy else { return }
        task = Task { [weak self] in
            guard let self else { return }
            self.isBusy = true
            defer { self.isBusy = false }
            await self.discoverAll()
        }
    }

    private func discoverAll() async {
        guard let runner = try? CommandRunner.dotnet(language: workspace.preferences.effectiveLanguage) else { return }
        let service = TestService(runner: runner)
        workspace.status = StatusMessage(text: String(localized: "Discovering tests…"), style: .progress)
        for project in testProjects {
            if let found = try? await service.discover(project: project.filePath, configuration: workspace.build.configuration, onOutput: workspace.output.sink(.tests)) {
                tests[project.filePath] = found.tests
                if found.tests.isEmpty && !found.process.succeeded {
                    workspace.diagnostics.setBuild(BuildOutputParser.parse(found.process.output))
                }
            }
        }
        workspace.status = StatusMessage(text: String(localized: "\(totalCount) tests found"), style: .neutral)
    }

    /// Runs all tests (`nodes` empty), a selection, or the failed ones.
    func run(_ nodes: [TestNode] = [], onlyFailed: Bool = false) {
        guard !isBusy else { return }
        task = Task { [weak self] in
            guard let self else { return }
            self.isBusy = true
            defer {
                self.isBusy = false
                self.running = []
            }
            if self.tests.isEmpty {
                await self.discoverAll()
            }
            guard let runner = try? CommandRunner.dotnet(language: self.workspace.preferences.effectiveLanguage) else { return }
            self.workspace.saveAll()
            self.workspace.output.clear(.tests)
            let service = TestService(runner: runner)
            for project in self.testProjects {
                let all = self.tests[project.filePath] ?? []
                var exact: [String] = []
                var prefixes: [String] = []
                if onlyFailed {
                    exact = all.filter { self.results[$0]?.outcome == .failed }
                    if exact.isEmpty { continue }
                } else if !nodes.isEmpty {
                    let mine = nodes.filter { $0.project == project.filePath }
                    if mine.isEmpty { continue }
                    if !mine.contains(where: { $0.kind == .project }) {
                        exact = mine.filter { $0.kind == .testCase || ($0.kind == .test && $0.children == nil) }.map(\.fullName)
                        prefixes = mine.filter { $0.kind == .namespace || $0.kind == .testClass || ($0.kind == .test && $0.children != nil) }.map(\.fullName)
                    }
                }
                let filter = exact.isEmpty && prefixes.isEmpty ? nil : TestService.filter(exact: exact, prefixes: prefixes)
                self.running = Set(all.filter { name in
                    filter == nil || exact.contains(name) || prefixes.contains { name.hasPrefix($0) }
                })
                self.workspace.status = StatusMessage(text: String(localized: "Running tests in \(project.name)…"), style: .progress)
                guard let outcome = try? await service.run(project: project.filePath, configuration: self.workspace.build.configuration, filter: filter, onOutput: self.workspace.output.sink(.tests)) else {
                    continue
                }
                for result in outcome.results {
                    self.results[result.fullName] = result
                }
                if outcome.results.isEmpty && !outcome.process.succeeded {
                    self.workspace.diagnostics.setBuild(BuildOutputParser.parse(outcome.process.output))
                }
            }
            let failed = self.failedCount
            self.workspace.status = failed == 0
                ? StatusMessage(text: String(localized: "\(self.passedCount) tests passed"), style: .success)
                : StatusMessage(text: String(localized: "\(failed) tests failed"), style: .error)
        }
    }

    func cancel() {
        task?.cancel()
    }

    /// Opens the test's source: the failing frame if there is one, otherwise its declaration.
    func open(_ node: TestNode) async {
        if let frame = TestService.firstSourceFrame(results[node.fullName]?.stackTrace) {
            workspace.openFile(URL(fileURLWithPath: frame.file), line: frame.line)
            return
        }
        var base = node.fullName
        while base.hasSuffix(".") || base.hasSuffix("(") {
            base.removeLast()
        }
        let name = TestName(base)
        let isTest = node.kind == .test || node.kind == .testCase
        let typeName = isTest ? name.classFullName : base
        let member = isTest ? name.method : nil
        if let engine = workspace.engine, let location = try? await engine.declaration(typeName: typeName, memberName: member) {
            workspace.navigate(to: location)
        }
    }
}
