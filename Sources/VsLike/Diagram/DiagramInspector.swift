import SwiftUI
import VsLikeKit

/// Edits the selected diagram type: name, access, modifiers, base types and members. Every change is written into
/// the code by the engine and can be undone in the diagram.
struct DiagramInspector: View {
    @Environment(Workspace.self) private var workspace
    @Bindable var model: DiagramEditorModel

    var body: some View {
        if let type = model.selectedType {
            TypeInspector(model: model, type: type)
                .id(type.fullName + "\(type.members.count)")
        } else {
            VStack(spacing: 14) {
                ContentUnavailableView("No Type Selected", systemImage: "rectangle.3.group", description: Text("Click a type on the diagram to edit it."))
                VStack(alignment: .leading, spacing: 8) {
                    Text("Diagram").font(.headline)
                    LabeledContent("Types", value: "\(model.boxes.count)")
                    LabeledContent("Relations", value: "\(model.edges.count)")
                    Toggle("Show external types", isOn: $model.showsExternal)
                }
                .padding()
            }
        }
    }
}

private struct TypeInspector: View {
    @Environment(Workspace.self) private var workspace
    let model: DiagramEditorModel
    let type: DiagramType
    @State private var name = ""
    @State private var newMemberKind = MemberKind.property
    @State private var newMemberText = ""

    private var bases: [DiagramType] { model.baseCandidates(for: type) }

    private var interfaces: [DiagramType] { model.interfaceCandidates(for: type) }

    var body: some View {
        Form {
            Section {
                HStack(spacing: 10) {
                    Image(systemName: icon)
                        .font(.title2)
                        .foregroundStyle(TypeBoxView.color(for: type.kind))
                    VStack(alignment: .leading) {
                        Text(type.displayName).font(.headline)
                        Text(type.namespace.isEmpty ? type.kind.keyword : "\(type.kind.keyword) · \(type.namespace)")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                if type.isExternal {
                    Label("External type (read-only)", systemImage: "lock").foregroundStyle(.secondary)
                } else {
                    TextField("Name", text: $name)
                        .onSubmit { Task { await model.renameType(type, to: name) } }
                    Picker("Access", selection: Binding(get: { type.access }, set: { access in Task { await model.setModifiers(type, access: access, modifiers: type.modifiers) } })) {
                        ForEach([Access.public, .internal], id: \.self) { Text($0.keyword).tag($0) }
                    }
                    if type.kind == .class || type.kind == .record {
                        modifierToggle("abstract", .abstract)
                        modifierToggle("sealed", .sealed)
                        modifierToggle("static", .static)
                    }
                    modifierToggle("partial", .partial)
                }
            }

            if !type.isExternal && type.kind != .enum {
                Section("Base Types") {
                    if type.kind == .class || type.kind == .record {
                        Picker("Base class", selection: Binding(get: { type.baseType ?? "" }, set: { base in
                            Task { await model.setBaseList(type, baseType: base.isEmpty ? nil : base, interfaces: type.interfaces) }
                        })) {
                            Text("None").tag("")
                            ForEach(bases) { Text($0.displayName).tag($0.displayName) }
                            if let base = type.baseType, !bases.contains(where: { $0.displayName == base }) {
                                Text(base).tag(base)
                            }
                        }
                    }
                    ForEach(interfaces) { candidate in
                        Toggle(candidate.displayName, isOn: Binding(get: { type.interfaces.contains(candidate.displayName) }, set: { on in
                            let list = on ? type.interfaces + [candidate.displayName] : type.interfaces.filter { $0 != candidate.displayName }
                            Task { await model.setBaseList(type, baseType: type.baseType, interfaces: list) }
                        }))
                    }
                    ForEach(type.interfaces.filter { name in !interfaces.contains { $0.displayName == name } }, id: \.self) { external in
                        Label(external, systemImage: "i.square").foregroundStyle(.secondary)
                    }
                }
            }

            Section("Members") {
                ForEach(Array(type.members.enumerated()), id: \.offset) { _, member in
                    MemberRow(model: model, type: type, member: member)
                }
                if !type.isExternal {
                    HStack {
                        Picker("", selection: $newMemberKind) {
                            ForEach(type.kind == .enum ? [MemberKind.enumValue] : [.property, .method, .field, .constructor, .event], id: \.self) {
                                Text(MemberKindText.title($0)).tag($0)
                            }
                        }
                        .labelsHidden()
                        .fixedSize()
                        TextField(MemberKindText.placeholder(newMemberKind), text: $newMemberText)
                            .font(.system(size: 11.5, design: .monospaced))
                            .onSubmit(addMember)
                        Button(action: addMember) { Image(systemName: "plus.circle.fill") }
                            .buttonStyle(.borderless)
                            .disabled(newMemberText.isEmpty)
                    }
                    Text("Only the name is required (default: public, int). Optional: + public  - private  # protected  ~ internal").font(.caption2).foregroundStyle(.tertiary)
                }
            }

            if !type.isExternal {
                Section {
                    Button("Open in Editor", systemImage: "doc.text") { Task { await model.openInEditor(type) } }
                    Button("Delete from Code…", systemImage: "trash", role: .destructive) { Task { await model.deleteType(type) } }
                }
            }
        }
        .formStyle(.grouped)
        .onAppear {
            name = type.name
            newMemberKind = type.kind == .enum ? .enumValue : .property
        }
    }

    private var icon: String {
        switch type.kind {
        case .class: return "c.square.fill"
        case .interface: return "i.square.fill"
        case .struct, .recordStruct: return "s.square.fill"
        case .record: return "r.square.fill"
        case .enum: return "e.square.fill"
        }
    }

    private func modifierToggle(_ title: String, _ flag: TypeModifiers) -> some View {
        Toggle(title, isOn: Binding(get: { type.modifiers.contains(flag) }, set: { on in
            var modifiers = type.modifiers
            if on { modifiers.insert(flag) } else { modifiers.remove(flag) }
            if on && flag == .static { modifiers.remove(.abstract); modifiers.remove(.sealed) }
            if on && (flag == .abstract || flag == .sealed) { modifiers.remove(.static) }
            if on && flag == .abstract { modifiers.remove(.sealed) }
            if on && flag == .sealed { modifiers.remove(.abstract) }
            Task { await model.setModifiers(type, access: type.access, modifiers: modifiers) }
        }))
    }

    private func addMember() {
        let text = newMemberText
        guard !text.isEmpty else { return }
        newMemberText = ""
        Task { await model.addMember(to: type, kind: newMemberKind, text: text) }
    }
}

private struct MemberRow: View {
    let model: DiagramEditorModel
    let type: DiagramType
    let member: DiagramMember
    @State private var text = ""
    @State private var isEditing = false

    var body: some View {
        HStack(spacing: 6) {
            if isEditing {
                TextField("", text: $text)
                    .font(.system(size: 11.5, design: .monospaced))
                    .onSubmit {
                        isEditing = false
                        if text != member.editText {
                            Task { await model.updateMember(member, in: type, text: text) }
                        }
                    }
                    .onExitCommand { isEditing = false }
            } else {
                Text(member.umlLine)
                    .font(.system(size: 11.5, design: .monospaced))
                    .lineLimit(1)
                    .onTapGesture(count: 2) {
                        if !type.isExternal {
                            text = member.editText
                            isEditing = true
                        }
                    }
            }
            Spacer()
            if !type.isExternal {
                Button { Task { await model.openInEditor(type, member: member) } } label: { Image(systemName: "arrow.right.circle") }
                    .buttonStyle(.borderless)
                    .help("Show in code")
                Button { Task { await model.removeMember(member, from: type) } } label: { Image(systemName: "minus.circle") }
                    .buttonStyle(.borderless)
                    .help("Delete member")
            }
        }
    }
}
