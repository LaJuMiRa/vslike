import AppKit
import Observation
import VsLikeKit

/// Build, rebuild, clean and run (in the integrated terminal, so programs can read input). Holds the startup
/// project, configuration and launch profile shown in the toolbar.
@MainActor
@Observable
final class BuildController {
    enum Phase: Equatable {
        case idle, building, running
    }

    @ObservationIgnored private unowned let workspace: Workspace
    @ObservationIgnored private var task: Task<Void, Never>?

    private(set) var phase: Phase = .idle
    private(set) var lastResult: BuildResult?
    var configuration = "Debug" { didSet { persist() } }
    var startupProject: String? { didSet { refreshProfiles(); persist() } }
    var launchProfile: String? { didSet { persist() } }
    private(set) var profiles: [LaunchProfile] = []

    init(workspace: Workspace) {
        self.workspace = workspace
    }

    static let configurations = ["Debug", "Release"]

    /// Projects that can be started (executables and test-less apps).
    var runnableProjects: [SolutionProject] {
        (workspace.solution?.projects ?? []).filter { project in
            guard let file = try? ProjectFile.load(project.filePath) else { return false }
            let output = file.property("OutputType")?.lowercased()
            let sdk = file.text.range(of: "Microsoft.NET.Sdk.Web", options: .caseInsensitive) != nil
            return (output == "exe" || output == "winexe" || sdk) && !file.isTestProject
        }
    }

    var startupProjectName: String? {
        startupProject.map { URL(fileURLWithPath: $0).deletingPathExtension().lastPathComponent }
    }

    var isBusy: Bool { phase != .idle }

    func restoreSelection() {
        let settings = workspace.settings
        configuration = settings.configuration ?? "Debug"
        let runnable = runnableProjects
        startupProject = runnable.first { $0.filePath == settings.startupProject }?.filePath ?? runnable.first?.filePath
        launchProfile = settings.launchProfile.flatMap { name in profiles.first { $0.name == name }?.name } ?? profiles.first(where: \.isRunnable)?.name
    }

    private func refreshProfiles() {
        profiles = startupProject.map { LaunchProfile.read(project: $0).filter(\.isRunnable) } ?? []
        if let launchProfile, !profiles.contains(where: { $0.name == launchProfile }) {
            self.launchProfile = profiles.first?.name
        }
    }

    private func persist() {
        guard let solutionURL = workspace.solutionURL else { return }
        var settings = workspace.settings
        guard settings.configuration != configuration || settings.startupProject != startupProject || settings.launchProfile != launchProfile else {
            return
        }
        // Only remember choices that differ from the defaults, so that a fresh solution gets no settings file.
        let isDefault = configuration == "Debug" && startupProject == runnableProjects.first?.filePath && (launchProfile == nil || launchProfile == profiles.first?.name)
        if isDefault && settings == SolutionSettings() {
            return
        }
        settings.configuration = configuration
        settings.startupProject = startupProject
        settings.launchProfile = launchProfile
        workspace.settings = settings
        try? settings.save(for: solutionURL)
    }

    var buildTarget: String? { workspace.solutionURL?.path }

    func cancel() {
        task?.cancel()
        task = nil
    }

    @discardableResult
    func run(_ kind: BuildKind) async -> BuildResult? {
        guard let target = buildTarget, phase == .idle else {
            return nil
        }
        workspace.saveAll()
        phase = .building
        workspace.output.clear(.build)
        workspace.output.selectedChannel = .build
        let verb = switch kind {
        case .build: String(localized: "Building")
        case .rebuild: String(localized: "Rebuilding")
        case .clean: String(localized: "Cleaning")
        }
        workspace.status = StatusMessage(text: "\(verb) \(URL(fileURLWithPath: target).lastPathComponent)…", style: .progress)
        defer { phase = .idle }
        do {
            let service = BuildService(runner: try CommandRunner.dotnet(language: workspace.preferences.effectiveLanguage))
            let result = try await service.build(target: target, configuration: configuration, kind: kind, onOutput: workspace.output.sink(.build))
            lastResult = result
            workspace.diagnostics.setBuild(result.diagnostics)
            if result.process.wasCancelled {
                workspace.status = StatusMessage(text: String(localized: "Build cancelled"), style: .warning)
            } else if result.succeeded {
                let seconds = Double(result.duration.components.seconds) + Double(result.duration.components.attoseconds) / 1e18
                let time = seconds.formatted(.number.precision(.fractionLength(1)))
                let text = kind == .clean
                    ? String(localized: "Clean succeeded")
                    : String(localized: "Build succeeded · \(result.warningCount) warnings · \(time) s")
                workspace.status = StatusMessage(text: text, style: .success)
            } else {
                workspace.status = StatusMessage(text: String(localized: "Build failed · \(result.errorCount) errors"), style: .error)
                workspace.bottomTab = .problems
                workspace.isBottomPanelVisible = true
            }
            return result
        } catch {
            workspace.status = StatusMessage(text: error.localizedDescription, style: .error)
            workspace.output.append(error.localizedDescription, channel: .build, isError: true)
            return nil
        }
    }

    func start(_ kind: BuildKind) {
        guard task == nil else { return }
        task = Task { [weak self] in
            await self?.run(kind)
            self?.task = nil
        }
    }

    /// Builds, then starts the startup project in a terminal tab (input works, output keeps colours).
    func startWithoutDebugging() {
        guard task == nil else { return }
        task = Task { [weak self] in
            defer { self?.task = nil }
            guard let self, let project = self.startupProject else {
                self?.workspace.alert = AlertMessage(title: String(localized: "Run"), message: String(localized: "This solution has no project that can be started."))
                return
            }
            guard let result = await self.run(.build), result.succeeded else {
                return
            }
            let profile = self.profiles.first { $0.name == self.launchProfile }
            let arguments = BuildService.runArguments(project: project, configuration: self.configuration, profile: profile)
            let directory = BuildService.workingDirectory(project: project, profile: profile)
            self.workspace.terminals.run(
                title: String(localized: "Run: \(self.startupProjectName ?? "")"),
                arguments: arguments,
                in: directory
            )
            self.workspace.status = StatusMessage(text: String(localized: "Running \(self.startupProjectName ?? "")"), style: .progress)
        }
    }
}
