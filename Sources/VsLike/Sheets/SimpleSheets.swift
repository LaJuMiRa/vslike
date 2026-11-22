import SwiftUI
import VsLikeKit

/// Settings window (⌘,).
struct SettingsView: View {
    @Environment(Preferences.self) private var preferences

    var body: some View {
        @Bindable var preferences = preferences
        TabView {
            Form {
                Picker("Appearance", selection: $preferences.appearance) {
                    Text("System").tag(Preferences.Appearance.system)
                    Text("Light").tag(Preferences.Appearance.light)
                    Text("Dark").tag(Preferences.Appearance.dark)
                }
                Picker("Language", selection: $preferences.language) {
                    Text("System").tag(Preferences.Language.system)
                    Text("English").tag(Preferences.Language.en)
                    Text("Deutsch").tag(Preferences.Language.de)
                }
                Text("The language changes after restarting VsLike.").font(.caption).foregroundStyle(.secondary)
                LabeledContent("Folder for new solutions") {
                    HStack {
                        Text((preferences.projectsFolder.path as NSString).abbreviatingWithTildeInPath).lineLimit(1).truncationMode(.middle)
                        Button("Choose…") {
                            if let url = OpenPanel.chooseFolder(message: String(localized: "Choose where new solutions are created")) {
                                preferences.projectsFolder = url
                            }
                        }
                    }
                }
                Toggle("StyleCop for new solutions", isOn: $preferences.styleCopByDefault)
                Text("Each solution remembers its own setting in .vslike/settings.json; the toolbar button switches it.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .tabItem { Label("General", systemImage: "gearshape") }

            Form {
                Slider(value: $preferences.fontSize, in: 9...28, step: 1) {
                    Text("Font size")
                } minimumValueLabel: {
                    Text("9")
                } maximumValueLabel: {
                    Text("28")
                }
                Text("SF Mono \(Int(preferences.fontSize)) pt").font(.system(size: preferences.fontSize, design: .monospaced))
                Toggle("Close brackets and quotes automatically", isOn: $preferences.autoCloseBrackets)
                Toggle("Format document on save", isOn: $preferences.formatOnSave)
                Toggle("Show problems and breakpoints in the scroll bar", isOn: $preferences.showMinimapOfDiagnostics)
            }
            .tabItem { Label("Editor", systemImage: "character.cursor.ibeam") }
        }
        .formStyle(.grouped)
        .frame(width: 520, height: 360)
    }
}

/// A text input sheet (new file, rename, breakpoint condition, go to line).
struct PromptSheet: View {
    @Environment(\.dismiss) private var dismiss
    let request: PromptRequest
    @State private var value = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(request.title).font(.headline)
            if !request.message.isEmpty {
                Text(request.message).font(.callout).foregroundStyle(.secondary)
            }
            TextField(request.placeholder, text: $value)
                .textFieldStyle(.roundedBorder)
                .font(.system(.body, design: .monospaced))
                .onSubmit(confirm)
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button(request.confirmTitle, action: confirm)
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
            }
        }
        .padding(20)
        .frame(width: 420)
        .onAppear { value = request.initialValue }
    }

    private func confirm() {
        let text = value
        dismiss()
        Task { await request.action(text) }
    }
}

/// Definitions or references; click to open.
struct ReferencesSheet: View {
    @Environment(Workspace.self) private var workspace
    @Environment(\.dismiss) private var dismiss
    let list: ReferenceList

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(list.title).font(.headline)
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.cancelAction)
            }
            .padding(14)
            Divider()
            List {
                ForEach(Dictionary(grouping: list.locations, by: \.filePath).sorted { $0.key < $1.key }, id: \.key) { file, locations in
                    Section(URL(fileURLWithPath: file).lastPathComponent) {
                        ForEach(locations) { location in
                            Button {
                                dismiss()
                                workspace.navigate(to: location)
                            } label: {
                                HStack(alignment: .firstTextBaseline) {
                                    Text("\(location.line)").font(.caption.monospacedDigit()).foregroundStyle(.secondary).frame(width: 36, alignment: .trailing)
                                    Text(location.lineText.trimmingCharacters(in: .whitespaces)).font(.system(size: 12, design: .monospaced)).lineLimit(1)
                                }
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
        }
        .frame(width: 640, height: 420)
    }
}

/// Creates a solution with a first project: name, location, template.
struct NewSolutionSheet: View {
    @Environment(Workspace.self) private var workspace
    @Environment(Preferences.self) private var preferences
    @Environment(\.dismiss) private var dismiss
    @State private var name = "MyApp"
    @State private var templates: [ProjectTemplate] = []
    @State private var template = "console"
    @State private var withTests = false
    @State private var isWorking = false
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("New Solution").font(.title2.bold())
            Form {
                TextField("Name", text: $name)
                LabeledContent("Location") {
                    HStack {
                        Text((preferences.projectsFolder.path as NSString).abbreviatingWithTildeInPath).lineLimit(1).truncationMode(.middle)
                        Button("Choose…") {
                            if let url = OpenPanel.chooseFolder(message: String(localized: "Choose where the solution folder is created")) {
                                preferences.projectsFolder = url
                            }
                        }
                        .help("The folder is remembered for new solutions")
                    }
                }
                Toggle("Add a test project (xUnit)", isOn: $withTests)
            }
            .formStyle(.grouped)
            .frame(height: 170)
            TemplateGrid(templates: templates, selection: $template)
            if let error {
                Text(error).font(.caption).foregroundStyle(Color.vsError).lineLimit(4)
            }
            HStack {
                if isWorking { ProgressView().controlSize(.small); Text("Creating…").foregroundStyle(.secondary) }
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Create") { Task { await create() } }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
                    .disabled(name.trimmingCharacters(in: .whitespaces).isEmpty || isWorking)
            }
        }
        .padding(22)
        .frame(width: 640)
        .task { await loadTemplates() }
    }

    private func loadTemplates() async {
        guard let runner = try? CommandRunner.dotnet() else { return }
        let found = await ProjectService(runner: runner).templates()
        templates = found.isEmpty ? TemplateGrid.fallback : found
    }

    private func create() async {
        isWorking = true
        defer { isWorking = false }
        do {
            let service = ProjectService(runner: try CommandRunner.dotnet())
            let folder = preferences.projectsFolder.appendingPathComponent(name)
            let solution = try await service.createSolution(in: folder, name: name)
            let project = try await service.createProject(template: template, name: name, in: folder)
            try await service.addProject(project.path, to: solution.path)
            if withTests {
                let tests = try await service.createProject(template: "xunit", name: name + ".Tests", in: folder)
                try await service.addProject(tests.path, to: solution.path)
                try await service.addReference(project.path, to: tests.path)
            }
            _ = try? await service.runner.run(["restore", solution.path])
            dismiss()
            await workspace.open(solution)
        } catch {
            self.error = error.localizedDescription
        }
    }
}

/// Adds a project to the open solution.
struct NewProjectSheet: View {
    @Environment(Workspace.self) private var workspace
    @Environment(\.dismiss) private var dismiss
    let solution: SolutionModel
    @State private var name = "MyLibrary"
    @State private var templates: [ProjectTemplate] = []
    @State private var template = "classlib"
    @State private var isWorking = false
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Add Project to \(solution.name)").font(.title2.bold())
            TextField("Project name", text: $name).textFieldStyle(.roundedBorder)
            TemplateGrid(templates: templates, selection: $template)
            if let error {
                Text(error).font(.caption).foregroundStyle(Color.vsError).lineLimit(4)
            }
            HStack {
                if isWorking { ProgressView().controlSize(.small) }
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Add") { Task { await add() } }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
                    .disabled(name.isEmpty || isWorking)
            }
        }
        .padding(22)
        .frame(width: 640)
        .task {
            if let runner = try? CommandRunner.dotnet() {
                let found = await ProjectService(runner: runner).templates()
                templates = found.isEmpty ? TemplateGrid.fallback : found
            }
        }
    }

    private func add() async {
        isWorking = true
        defer { isWorking = false }
        do {
            let service = ProjectService(runner: try CommandRunner.dotnet())
            let project = try await service.createProject(template: template, name: name, in: solution.directory)
            try await service.addProject(project.path, to: solution.filePath)
            dismiss()
            workspace.refreshTree()
            await workspace.restartEngine()
            workspace.tests.reloadProjects()
        } catch {
            self.error = error.localizedDescription
        }
    }
}

/// Project templates as selectable cards.
struct TemplateGrid: View {
    static let fallback = [
        ProjectTemplate(name: "Console App", shortName: "console", tags: "Common/Console"),
        ProjectTemplate(name: "Class Library", shortName: "classlib", tags: "Common/Library"),
        ProjectTemplate(name: "xUnit Test Project", shortName: "xunit", tags: "Test"),
        ProjectTemplate(name: "ASP.NET Core Web API", shortName: "webapi", tags: "Web"),
        ProjectTemplate(name: "Worker Service", shortName: "worker", tags: "Common/Worker"),
    ]

    let templates: [ProjectTemplate]
    @Binding var selection: String

    var body: some View {
        ScrollView {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 180), spacing: 10)], spacing: 10) {
                ForEach(templates) { template in
                    let isSelected = template.shortName == selection
                    Button {
                        selection = template.shortName
                    } label: {
                        HStack(spacing: 10) {
                            Image(systemName: symbol(for: template))
                                .font(.system(size: 17))
                                .foregroundStyle(isSelected ? .white : Color.vsAccent)
                                .frame(width: 30, height: 30)
                                .background(isSelected ? Color.vsAccent : Color.vsAccent.opacity(0.13), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                            VStack(alignment: .leading, spacing: 2) {
                                Text(template.name).font(.callout.weight(.medium)).lineLimit(1)
                                Text(template.shortName).font(.caption.monospaced()).foregroundStyle(.secondary)
                            }
                            Spacer(minLength: 0)
                        }
                        .padding(10)
                        .background(isSelected ? Color.vsAccent.opacity(0.12) : Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(isSelected ? Color.vsAccent : .clear, lineWidth: 1.5))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .frame(height: 230)
        .overlay {
            if templates.isEmpty { ProgressView() }
        }
    }

    private func symbol(for template: ProjectTemplate) -> String {
        let tags = (template.tags + " " + template.shortName).lowercased()
        if tags.contains("test") || ["xunit", "nunit", "mstest"].contains(template.shortName) { return "checkmark.diamond" }
        if tags.contains("web") || tags.contains("blazor") || tags.contains("razor") || tags.contains("mvc") { return "globe" }
        if tags.contains("console") { return "terminal" }
        if tags.contains("library") { return "books.vertical" }
        if tags.contains("worker") || tags.contains("service") { return "gearshape.2" }
        if tags.contains("grpc") { return "network" }
        return "shippingbox"
    }
}
