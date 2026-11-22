import SwiftUI
import VsLikeKit

/// NuGet package manager for one project: browse, installed, updates.
struct NuGetSheet: View {
    enum Tab: String, CaseIterable, Identifiable {
        case browse, installed, updates
        var id: String { rawValue }
    }

    @Environment(Workspace.self) private var workspace
    @Environment(\.dismiss) private var dismiss
    let project: String
    @State private var tab = Tab.browse
    @State private var query = ""
    @State private var prerelease = false
    @State private var results: [NuGetPackage] = []
    @State private var installed: [InstalledPackage] = []
    @State private var selected: String?
    @State private var versions: [String] = []
    @State private var version = ""
    @State private var isWorking = false
    @State private var message: String?
    @State private var client = NuGetClient()

    private var projectName: String { URL(fileURLWithPath: project).deletingPathExtension().lastPathComponent }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Image(systemName: "cube.box.fill").foregroundStyle(Color.vsAccent).font(.title2)
                Text("NuGet Packages · \(projectName)").font(.headline)
                Spacer()
                Picker("", selection: $tab) {
                    Text("Browse").tag(Tab.browse)
                    Text("Installed").tag(Tab.installed)
                    Text("Updates").tag(Tab.updates)
                }
                .pickerStyle(.segmented)
                .fixedSize()
                Button("Done") { dismiss() }.keyboardShortcut(.cancelAction)
            }
            .padding(14)
            Divider()
            HSplitView {
                VStack(spacing: 8) {
                    if tab == .browse {
                        HStack {
                            TextField("Search nuget.org", text: $query)
                                .textFieldStyle(.roundedBorder)
                                .onSubmit { Task { await search() } }
                            Toggle("Pre-release", isOn: $prerelease).toggleStyle(.checkbox)
                        }
                        .padding(.horizontal, 10)
                        .padding(.top, 10)
                    }
                    List(selection: $selected) {
                        ForEach(rows, id: \.id) { row in
                            VStack(alignment: .leading, spacing: 2) {
                                HStack {
                                    Text(row.id).fontWeight(.medium)
                                    if row.verified { Image(systemName: "checkmark.seal.fill").foregroundStyle(Color.vsInfo).font(.caption) }
                                    Spacer()
                                    Text(row.version).font(.caption.monospaced()).foregroundStyle(.secondary)
                                }
                                if !row.detail.isEmpty {
                                    Text(row.detail).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                                }
                            }
                            .tag(row.id)
                            .padding(.vertical, 2)
                        }
                    }
                }
                .frame(minWidth: 320)
                detail
                    .frame(minWidth: 260, maxWidth: .infinity)
            }
            if let message {
                Divider()
                Text(message).font(.caption).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading).padding(10)
            }
        }
        .frame(width: 820, height: 520)
        .task { await loadInstalled() }
        .onChange(of: tab) { Task { await loadInstalled() } }
        .onChange(of: selected) { Task { await loadVersions() } }
    }

    private struct Row {
        let id: String
        let version: String
        let detail: String
        let verified: Bool
    }

    private var rows: [Row] {
        switch tab {
        case .browse:
            return results.map { Row(id: $0.id, version: $0.latestVersion, detail: $0.description ?? "", verified: $0.isVerified) }
        case .installed:
            return installed.map { Row(id: $0.id, version: $0.resolvedVersion ?? $0.requestedVersion ?? "", detail: $0.hasUpdate ? String(localized: "Update available: \($0.latestVersion ?? "")") : "", verified: false) }
        case .updates:
            return installed.filter(\.hasUpdate).map { Row(id: $0.id, version: "\($0.resolvedVersion ?? "") → \($0.latestVersion ?? "")", detail: "", verified: false) }
        }
    }

    @ViewBuilder private var detail: some View {
        if let selected {
            let current = installed.first { $0.id == selected }
            VStack(alignment: .leading, spacing: 12) {
                Text(selected).font(.title3.bold())
                if let package = results.first(where: { $0.id == selected }) {
                    if let authors = package.authors { Text(authors).foregroundStyle(.secondary) }
                    Text("\(package.totalDownloads.formatted()) downloads").font(.caption).foregroundStyle(.secondary)
                    if let description = package.description { Text(description).font(.callout) }
                }
                if let current {
                    Label("Installed: \(current.resolvedVersion ?? current.requestedVersion ?? "")", systemImage: "checkmark.circle.fill").foregroundStyle(Color.vsSuccess)
                }
                Picker("Version", selection: $version) {
                    ForEach(versions, id: \.self) { Text($0).tag($0) }
                }
                HStack {
                    Button(current == nil ? "Install" : "Install Version") { Task { await install(selected) } }
                        .buttonStyle(.borderedProminent)
                        .disabled(isWorking)
                    if current != nil {
                        Button("Uninstall", role: .destructive) { Task { await uninstall(selected) } }
                            .disabled(isWorking)
                    }
                    if isWorking { ProgressView().controlSize(.small) }
                }
                Spacer()
            }
            .padding(16)
        } else {
            ContentUnavailableView("Select a package", systemImage: "cube.box")
        }
    }

    private func search() async {
        guard !query.isEmpty else { return }
        isWorking = true
        defer { isWorking = false }
        do {
            results = try await client.search(query, includePrerelease: prerelease)
            message = nil
        } catch {
            message = error.localizedDescription
        }
    }

    private func loadInstalled() async {
        guard let service = try? ProjectService(runner: CommandRunner.dotnet()) else { return }
        installed = (try? await service.listPackages(of: project, outdated: tab == .updates)) ?? installed
        if tab != .updates, let outdated = try? await service.listPackages(of: project, outdated: true) {
            installed = installed.map { package in outdated.first { $0.id == package.id } ?? package }
        }
    }

    private func loadVersions() async {
        guard let selected else { return }
        versions = (try? await client.versions(of: selected, includePrerelease: prerelease)) ?? []
        version = versions.first ?? ""
    }

    private func install(_ id: String) async {
        isWorking = true
        defer { isWorking = false }
        do {
            try await ProjectService(runner: try CommandRunner.dotnet()).addPackage(id, version: version, to: project)
            message = String(localized: "\(id) \(version) installed.")
            await loadInstalled()
            workspace.refreshTree()
            await workspace.restartEngine()
        } catch {
            message = error.localizedDescription
        }
    }

    private func uninstall(_ id: String) async {
        isWorking = true
        defer { isWorking = false }
        do {
            try await ProjectService(runner: try CommandRunner.dotnet()).removePackage(id, from: project)
            message = String(localized: "\(id) removed.")
            await loadInstalled()
            workspace.refreshTree()
            await workspace.restartEngine()
        } catch {
            message = error.localizedDescription
        }
    }
}

/// Project properties (TargetFramework, Nullable …) and project references.
struct ProjectPropertiesSheet: View {
    @Environment(Workspace.self) private var workspace
    @Environment(\.dismiss) private var dismiss
    let project: String
    @State private var values: [String: String] = [:]
    @State private var original: [String: String] = [:]
    @State private var references: Set<String> = []
    @State private var originalReferences: Set<String> = []
    @State private var error: String?
    @State private var isSaving = false
    @State private var frameworks: [TargetFrameworkOption] = []

    private var candidates: [SolutionProject] {
        (workspace.solution?.projects ?? []).filter { $0.filePath != project }
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Image(systemName: "slider.horizontal.3").foregroundStyle(Color.vsAccent).font(.title2)
                Text(URL(fileURLWithPath: project).deletingPathExtension().lastPathComponent).font(.headline)
                Spacer()
            }
            .padding(14)
            Form {
                Section("Properties") {
                    ForEach(ProjectFile.editableProperties, id: \.self) { key in
                        propertyField(key)
                    }
                }
                Section("Project References") {
                    if candidates.isEmpty {
                        Text("The solution has no other projects.").foregroundStyle(.secondary)
                    }
                    ForEach(candidates) { other in
                        Toggle(other.name, isOn: Binding(
                            get: { references.contains(other.filePath) },
                            set: { if $0 { references.insert(other.filePath) } else { references.remove(other.filePath) } }
                        ))
                    }
                }
            }
            .formStyle(.grouped)
            if let error {
                Text(error).font(.caption).foregroundStyle(Color.vsError).padding(.horizontal)
            }
            HStack {
                if isSaving { ProgressView().controlSize(.small) }
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { Task { await save() } }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
                    .disabled(isSaving)
            }
            .padding(14)
        }
        .frame(width: 560, height: 620)
        .onAppear(perform: load)
        .task {
            if let runner = try? CommandRunner.dotnet() {
                frameworks = DotnetLocator.targetFrameworks(for: await DotnetLocator.listSdks(runner: runner))
            }
        }
    }

    @ViewBuilder private func propertyField(_ key: String) -> some View {
        let binding = Binding(get: { values[key] ?? "" }, set: { values[key] = $0 })
        switch key {
        case "TargetFramework":
            Picker(key, selection: binding) {
                ForEach(frameworks) { option in
                    Text("\(option.moniker)  (.NET SDK \(option.sdkVersions.joined(separator: ", ")))").tag(option.moniker)
                }
                if let current = values[key], !current.isEmpty, !frameworks.contains(where: { $0.moniker == current }) {
                    Text(current).tag(current)
                }
            }
            Picker(key, selection: binding) {
                Text("–").tag("")
                ForEach(["enable", "disable", "warnings", "annotations"], id: \.self) { Text($0).tag($0) }
            }
        case "OutputType":
            Picker(key, selection: binding) {
                Text("–").tag("")
                ForEach(["Exe", "Library", "WinExe"], id: \.self) { Text($0).tag($0) }
            }
        case "ImplicitUsings", "TreatWarningsAsErrors", "GenerateDocumentationFile", "InvariantGlobalization":
            Picker(key, selection: binding) {
                Text("–").tag("")
                Text("true").tag("true")
                Text("false").tag("false")
                Text("enable").tag("enable")
            }
        default:
            TextField(key, text: binding)
        }
    }

    private func load() {
        guard let file = try? ProjectFile.load(project) else { return }
        for key in ProjectFile.editableProperties {
            values[key] = file.property(key) ?? ""
        }
        original = values
        references = Set(file.projectReferences())
        originalReferences = references
    }

    private func save() async {
        isSaving = true
        defer { isSaving = false }
        do {
            if values != original {
                var file = try ProjectFile.load(project)
                for key in ProjectFile.editableProperties where values[key] != original[key] {
                    file.setProperty(key, values[key])
                }
                try file.save()
            }
            if references != originalReferences {
                let service = ProjectService(runner: try CommandRunner.dotnet())
                for added in references.subtracting(originalReferences) {
                    try await service.addReference(added, to: project)
                }
                for removed in originalReferences.subtracting(references) {
                    try await service.removeReference(removed, from: project)
                }
            }
            dismiss()
            workspace.refreshTree()
            await workspace.restartEngine()
        } catch {
            self.error = error.localizedDescription
        }
    }
}
