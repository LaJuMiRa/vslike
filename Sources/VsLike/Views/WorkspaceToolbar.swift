import SwiftUI
import VsLikeKit

/// The toolbar: run controls on the left, the target picker with the activity pill in the middle, analysis and
/// view toggles on the right. Debug controls replace the build button while a debug session runs.
struct WorkspaceToolbar: ToolbarContent {
    @Environment(Workspace.self) private var workspace

    var body: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            RunControls()
        }
        ToolbarItem(placement: .principal) {
            ActivityBar()
        }
        ToolbarItemGroup(placement: .primaryAction) {
            AnalysisControls()
        }
    }
}

private struct RunControls: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        HStack(spacing: 6) {
        let debugger = workspace.debugger
        let isRunning = debugger.isActive || workspace.terminals.hasRunningProgram || workspace.build.isBusy
        HStack(spacing: 2) {
            Button {
                Task { await debugger.start() }
            } label: {
                Label(debugger.isStopped ? "Continue" : "Run", systemImage: debugger.isStopped ? "play.fill" : "play.fill")
            }
            .help(debugger.isStopped ? "Continue (⌘R)" : "Run with debugger (⌘R)")
            .disabled(workspace.solution == nil || (debugger.isActive && !debugger.isStopped) || debugger.isStarting)

            Button {
                Task { await debugger.stop() }
                workspace.terminals.stopRuns()
                workspace.build.cancel()
            } label: {
                Label("Stop", systemImage: "stop.fill")
            }
            .help("Stop (⌘.)")
            .disabled(!isRunning)
        }

        if debugger.isActive {
            HStack(spacing: 2) {
                Button { Task { await debugger.pause() } } label: { Label("Pause", systemImage: "pause.fill") }
                    .disabled(debugger.isStopped)
                    .help("Pause")
                Button { Task { await debugger.stepOver() } } label: { Label("Step Over", systemImage: "arrow.turn.down.right") }
                    .disabled(!debugger.isStopped)
                    .help("Step Over (F10)")
                Button { Task { await debugger.stepIn() } } label: { Label("Step Into", systemImage: "arrow.down.to.line") }
                    .disabled(!debugger.isStopped)
                    .help("Step Into (F11)")
                Button { Task { await debugger.stepOut() } } label: { Label("Step Out", systemImage: "arrow.up.to.line") }
                    .disabled(!debugger.isStopped)
                    .help("Step Out (⇧F11)")
            }
        } else {
            Menu {
                Button("Build Solution", systemImage: "hammer") { workspace.build.start(.build) }
                    .keyboardShortcut("b")
                Button("Rebuild Solution", systemImage: "arrow.triangle.2.circlepath") { workspace.build.start(.rebuild) }
                Button("Clean Solution", systemImage: "trash") { workspace.build.start(.clean) }
                Divider()
                Button("Run Without Debugging", systemImage: "play") { workspace.build.startWithoutDebugging() }
                Button("Run Tests", systemImage: "checkmark.diamond") { workspace.tests.run() }
            } label: {
                Label("Build", systemImage: "hammer")
            } primaryAction: {
                workspace.build.start(.build)
            }
            .help("Build (⌘B)")
            .disabled(workspace.solution == nil || workspace.build.isBusy)
            .fixedSize()
        }
        }
        .labelStyle(.iconOnly)
    }
}

/// Target, configuration and launch profile in one capsule, followed by the current activity.
private struct ActivityBar: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        let build = workspace.build
        HStack(spacing: 0) {
            Menu {
                if build.runnableProjects.isEmpty {
                    Text("No startable project")
                }
                ForEach(build.runnableProjects) { project in
                    Button {
                        build.startupProject = project.filePath
                    } label: {
                        if build.startupProject == project.filePath {
                            Label(project.name, systemImage: "checkmark")
                        } else {
                            Text(project.name)
                        }
                    }
                }
                if !build.profiles.isEmpty {
                    Divider()
                    Section("Launch Profile") {
                        ForEach(build.profiles) { profile in
                            Button {
                                build.launchProfile = profile.name
                            } label: {
                                if build.launchProfile == profile.name {
                                    Label(profile.name, systemImage: "checkmark")
                                } else {
                                    Text(profile.name)
                                }
                            }
                        }
                    }
                }
            } label: {
                HStack(spacing: 5) {
                    Image(systemName: "shippingbox.fill").foregroundStyle(Color.vsAccent)
                    Text(build.startupProjectName ?? String(localized: "No target")).lineLimit(1)
                    if let profile = build.launchProfile, build.profiles.count > 1 {
                        Text(profile).foregroundStyle(.secondary).lineLimit(1)
                    }
                }
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .fixedSize()
            .padding(.leading, 12)

            Text("›").foregroundStyle(.tertiary).padding(.horizontal, 6)

            Menu {
                ForEach(BuildController.configurations, id: \.self) { configuration in
                    Button(configuration) { build.configuration = configuration }
                }
            } label: {
                Text(build.configuration).foregroundStyle(.secondary)
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .fixedSize()

            Divider().frame(height: 16).padding(.horizontal, 10)

            StatusPill()
                .padding(.trailing, 12)
        }
        .font(.system(size: 12))
        .frame(minWidth: 300, idealWidth: 480, maxWidth: 560, minHeight: 30, maxHeight: 30)
        .background(.quaternary.opacity(0.45), in: Capsule())
    }
}

private struct StatusPill: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        let status = workspace.status
        HStack(spacing: 7) {
            switch status.style {
            case .progress:
                ProgressView().controlSize(.mini)
            case .success:
                Image(systemName: "checkmark.circle.fill").foregroundStyle(Color.vsSuccess)
            case .warning:
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(Color.vsWarning)
            case .error:
                Image(systemName: "xmark.octagon.fill").foregroundStyle(Color.vsError)
            case .neutral:
                EmptyView()
            }
            Text(status.text)
                .lineLimit(1)
                .truncationMode(.middle)
                .contentTransition(.opacity)
            Spacer(minLength: 8)
            let errors = workspace.diagnostics.errorCount
            let warnings = workspace.diagnostics.warningCount
            Button {
                workspace.show(.problems)
            } label: {
                HStack(spacing: 8) {
                    Label("\(errors)", systemImage: "xmark.octagon.fill")
                        .foregroundStyle(errors > 0 ? Color.vsError : .secondary)
                    Label("\(warnings)", systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(warnings > 0 ? Color.vsWarning : .secondary)
                }
                .labelStyle(.titleAndIcon)
                .font(.system(size: 11).monospacedDigit())
            }
            .buttonStyle(.plain)
            .help("Show Problems (⇧⌘M)")
        }
        .animation(.easeInOut(duration: 0.2), value: status)
    }
}

private struct AnalysisControls: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        Button {
            Task { await workspace.toggleStyleCop() }
        } label: {
            if workspace.isStyleCopLoading {
                ProgressView().controlSize(.small)
            } else {
                Label("StyleCop", systemImage: workspace.styleCopEnabled ? "checkmark.seal.fill" : "checkmark.seal")
            }
        }
        .foregroundStyle(workspace.styleCopEnabled ? Color.vsStyleCop : .secondary)
        .help(workspace.styleCopEnabled ? "StyleCop is on for this solution" : "StyleCop is off for this solution")
        .disabled(workspace.solution == nil || workspace.isStyleCopLoading)

        Button {
            Task { await workspace.openSolutionDiagram() }
        } label: {
            Label("Class Diagram", systemImage: "rectangle.3.group")
        }
        .help("Class Diagram (⇧⌘D)")
        .disabled(workspace.solution == nil)

        Button {
            workspace.showTerminal()
        } label: {
            Label("Terminal", systemImage: "terminal")
        }
        .help("Terminal (⌃`)")

        Button {
            withAnimation(.snappy) { workspace.isBottomPanelVisible.toggle() }
        } label: {
            Label("Panel", systemImage: "rectangle.bottomthird.inset.filled")
        }
        .help("Show or hide the panel (⌘J)")

        Button {
            withAnimation(.snappy) { workspace.isInspectorVisible.toggle() }
        } label: {
            Label("Inspector", systemImage: "sidebar.right")
        }
        .help("Inspector")
    }
}
