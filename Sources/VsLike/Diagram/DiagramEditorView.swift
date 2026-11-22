import AppKit
import SwiftUI
import UniformTypeIdentifiers
import VsLikeKit

/// The graphical class diagram: grid, relations and type boxes on a zoomable, pannable canvas with a floating tool
/// bar. Selecting a box shows it in the inspector.
struct DiagramEditorView: View {
    @Environment(Workspace.self) private var workspace
    @Bindable var model: DiagramEditorModel
    @State private var zoom: Double = 1
    @State private var offset: CGSize = .zero
    @State private var dragOffsets: [String: CGSize] = [:]
    @State private var panStart: CGSize?
    @State private var connection: (from: String, to: CGPoint)?
    @State private var viewSize: CGSize = .zero
    @State private var didRestoreView = false
    @State private var hoverLocation = CGPoint.zero
    @State private var scrollMonitor = ScrollMonitor()

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .topLeading) {
                Color(nsColor: Palette.editorBackground)
                if model.layout.showGrid {
                    GridBackground(zoom: zoom, offset: offset)
                }
                Color.clear
                    .contentShape(Rectangle())
                    .gesture(backgroundDrag)
                    .onTapGesture(coordinateSpace: .local) { location in backgroundTapped(at: location) }
                    .onContinuousHover { phase in
                        if case let .active(location) = phase { hoverLocation = location }
                    }
                    .contextMenu {
                        CanvasContextMenu(model: model, newType: { kind in promptNewType(kind, at: diagramPoint(hoverLocation)) }, fit: { fit(in: geometry.size) })
                    }

                RelationsLayer(edges: edges, zoom: zoom, offset: offset)
                    .allowsHitTesting(false)

                ForEach(boxes, id: \.type.fullName) { box in
                    let shift = dragOffsets[box.type.fullName] ?? .zero
                    TypeBoxView(box: box, model: model, isSelected: model.selection == box.type.fullName)
                        .frame(width: box.frame.width, height: box.frame.height)
                        .scaleEffect(zoom, anchor: .topLeading)
                        .frame(width: box.frame.width * zoom, height: box.frame.height * zoom, alignment: .topLeading)
                        .position(x: (box.frame.midX) * zoom + offset.width + shift.width, y: (box.frame.midY) * zoom + offset.height + shift.height)
                        .gesture(boxDrag(box))
                        .onTapGesture(count: 2) { Task { await model.openInEditor(box.type) } }
                        .onTapGesture { select(box.type.fullName) }
                }

                if let connection, let source = boxes.first(where: { $0.type.fullName == connection.from }) {
                    Path { path in
                        path.move(to: CGPoint(x: source.center.x * zoom + offset.width, y: source.center.y * zoom + offset.height))
                        path.addLine(to: connection.to)
                    }
                    .stroke(Color.vsAccent, style: StrokeStyle(lineWidth: 2, dash: [6, 4]))
                    .allowsHitTesting(false)
                }

                if model.isLoading {
                    ProgressView("Reading types…").frame(maxWidth: .infinity, maxHeight: .infinity)
                } else if boxes.isEmpty {
                    EmptyDiagram(model: model).frame(maxWidth: .infinity, maxHeight: .infinity)
                }

                DiagramToolbar(model: model, zoom: $zoom, fit: { fit(in: geometry.size) }, exportPNG: exportPNG)
                    .padding(12)
            }
            .clipped()
            .onAppear {
                viewSize = geometry.size
                restoreView(size: geometry.size)
                scrollMonitor.install(frame: { geometry.frame(in: .global) }) { event in
                    switch event {
                    case let .pan(delta):
                        offset.width += delta.width
                        offset.height += delta.height
                        persistView()
                    case let .zoom(factor, location):
                        zoom(by: factor, around: location)
                    }
                }
            }
            .onDisappear { scrollMonitor.remove() }
            .onChange(of: geometry.size) { _, size in viewSize = size }
            .onChange(of: model.selection) { _, selection in
                if selection != nil { workspace.isInspectorVisible = true }
            }
        }
        .focusable()
        .onDeleteCommand {
            if let type = model.selectedType { Task { await model.deleteType(type) } }
        }
    }

    private var boxes: [DiagramBox] { model.boxes }

    /// Edges between the current (possibly dragged) box positions.
    private var edges: [DiagramEdge] {
        guard !dragOffsets.isEmpty else { return model.edges }
        let moved = boxes.map { box -> DiagramBox in
            guard let shift = dragOffsets[box.type.fullName] else { return box }
            return DiagramBox(type: box.type, frame: box.frame.offsetBy(dx: shift.width / zoom, dy: shift.height / zoom), collapsed: box.collapsed)
        }
        return DiagramGeometry.edges(model: model.model, boxes: moved)
    }

    private func diagramPoint(_ point: CGPoint) -> CGPoint {
        CGPoint(x: (point.x - offset.width) / zoom, y: (point.y - offset.height) / zoom)
    }

    private func select(_ name: String?) {
        model.selection = name
    }

    private var backgroundDrag: some Gesture {
        DragGesture(minimumDistance: 3)
            .onChanged { value in
                if panStart == nil { panStart = offset }
                offset = CGSize(width: panStart!.width + value.translation.width, height: panStart!.height + value.translation.height)
            }
            .onEnded { _ in
                panStart = nil
                persistView()
            }
    }

    private func boxDrag(_ box: DiagramBox) -> some Gesture {
        DragGesture(minimumDistance: 3, coordinateSpace: .named("canvas"))
            .onChanged { value in
                if model.tool == .inherit {
                    connection = (box.type.fullName, CGPoint(x: box.center.x * zoom + offset.width + value.translation.width, y: box.center.y * zoom + offset.height + value.translation.height))
                } else if !box.type.isExternal || true {
                    dragOffsets[box.type.fullName] = value.translation
                }
            }
            .onEnded { value in
                if model.tool == .inherit, let connection {
                    self.connection = nil
                    let target = diagramPoint(connection.to)
                    if let destination = boxes.first(where: { $0.frame.contains(target) && $0.type.fullName != box.type.fullName }) {
                        Task { await model.connect(from: box.type.fullName, to: destination.type.fullName) }
                    }
                    model.tool = .select
                    return
                }
                dragOffsets[box.type.fullName] = nil
                model.move(box.type.fullName, to: CGPoint(x: box.frame.minX + value.translation.width / zoom, y: box.frame.minY + value.translation.height / zoom))
            }
    }

    private func backgroundTapped(at location: CGPoint) {
        switch model.tool {
        case .select:
            select(nil)
        case let .create(kind):
            model.tool = .select
            promptNewType(kind, at: diagramPoint(location))
        case .inherit:
            model.tool = .select
        }
    }

    private func promptNewType(_ kind: DiagramTypeKind, at point: CGPoint) {
        workspace.prompt = PromptRequest(
            title: String(localized: "New \(kind.keyword)"),
            message: String(localized: "Creates a file in \(model.newTypeFolder.lastPathComponent) with the namespace of the folder."),
            placeholder: kind == .interface ? "IShape" : "Shape",
            confirmTitle: String(localized: "Create")
        ) { name in
            await model.createType(kind, name: name, at: point)
        }
    }

    private func zoom(by factor: Double, around location: CGPoint) {
        let newZoom = min(3, max(0.2, zoom * factor))
        let anchor = diagramPoint(location)
        zoom = newZoom
        offset = CGSize(width: location.x - anchor.x * newZoom, height: location.y - anchor.y * newZoom)
        persistView()
    }

    private func fit(in size: CGSize) {
        guard !boxes.isEmpty else { return }
        let bounds = DiagramGeometry.bounds(boxes, margin: 40)
        let scale = min(1.25, max(0.2, min(size.width / bounds.width, (size.height - 60) / bounds.height)))
        withAnimation(.snappy) {
            zoom = scale
            offset = CGSize(width: (size.width - bounds.width * scale) / 2 - bounds.minX * scale, height: 60 + (size.height - 60 - bounds.height * scale) / 2 - bounds.minY * scale)
        }
        persistView()
    }

    private func restoreView(size: CGSize) {
        guard !didRestoreView else { return }
        didRestoreView = true
        let layout = model.layout
        if layout.offsetX != 0 || layout.offsetY != 0 || layout.zoom != 1 {
            zoom = layout.zoom
            offset = CGSize(width: layout.offsetX, height: layout.offsetY)
        } else {
            offset = CGSize(width: 40, height: 70)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { fit(in: size) }
        }
    }

    private func persistView() {
        model.setView(zoom: zoom, offset: CGPoint(x: offset.width, y: offset.height))
    }

    private func exportPNG() {
        let snapshot = DiagramSnapshot(model: model.model, layout: model.layout)
        let renderer = ImageRenderer(content: snapshot)
        renderer.scale = 2
        guard let image = renderer.nsImage, let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
              let png = bitmap.representation(using: .png, properties: [:]) else { return }
        save(png, name: model.name + ".png", type: .png)
    }

    private func save(_ data: Data, name: String, type: UTType) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = name
        panel.allowedContentTypes = [type]
        panel.directoryURL = model.folder
        if panel.runModal() == .OK, let url = panel.url {
            try? data.write(to: url)
        }
    }
}

/// Dots every grid step that move with pan and zoom.
private struct GridBackground: View {
    let zoom: Double
    let offset: CGSize

    var body: some View {
        Canvas { context, size in
            let step = BoxMetrics.gridSize * zoom * (zoom < 0.5 ? 4 : zoom < 0.9 ? 2 : 1)
            guard step > 4 else { return }
            let startX = offset.width.truncatingRemainder(dividingBy: step)
            let startY = offset.height.truncatingRemainder(dividingBy: step)
            var x = startX
            let dot = Color.primary.opacity(0.13)
            while x < size.width {
                var y = startY
                while y < size.height {
                    context.fill(Path(ellipseIn: CGRect(x: x - 0.9, y: y - 0.9, width: 1.8, height: 1.8)), with: .color(dot))
                    y += step
                }
                x += step
            }
        }
        .allowsHitTesting(false)
    }
}

/// UML relation lines: hollow triangle for inheritance (dashed for interfaces), arrow for associations, diamond for
/// compositions, with labels and multiplicities.
struct RelationsLayer: View {
    let edges: [DiagramEdge]
    let zoom: Double
    let offset: CGSize

    var body: some View {
        Canvas { context, _ in
            for edge in edges {
                let start = CGPoint(x: edge.start.x * zoom + offset.width, y: edge.start.y * zoom + offset.height)
                let end = CGPoint(x: edge.end.x * zoom + offset.width, y: edge.end.y * zoom + offset.height)
                RelationsLayer.draw(edge.relation, from: start, to: end, scale: zoom, in: &context)
            }
        }
    }

    static func draw(_ relation: DiagramRelation, from start: CGPoint, to end: CGPoint, scale: Double, in context: inout GraphicsContext) {
        let ink = Color.primary.opacity(0.62)
        let angle = atan2(end.y - start.y, end.x - start.x)
        let size = 12 * max(0.6, scale)
        func point(_ distance: Double, _ spread: Double) -> CGPoint {
            CGPoint(x: end.x - distance * cos(angle) + spread * sin(angle), y: end.y - distance * sin(angle) - spread * cos(angle))
        }
        var line = Path()
        line.move(to: start)
        let isHierarchy = relation.kind == .inheritance || relation.kind == .implementation
        line.addLine(to: isHierarchy ? point(size, 0) : end)
        let dash: [CGFloat] = relation.kind == .implementation ? [6, 4] : []
        context.stroke(line, with: .color(ink), style: StrokeStyle(lineWidth: 1.3, dash: dash))
        switch relation.kind {
        case .inheritance, .implementation:
            var triangle = Path()
            triangle.move(to: end)
            triangle.addLine(to: point(size, size * 0.55))
            triangle.addLine(to: point(size, -size * 0.55))
            triangle.closeSubpath()
            context.fill(triangle, with: .color(Color(nsColor: Palette.editorBackground)))
            context.stroke(triangle, with: .color(ink), lineWidth: 1.3)
        case .association, .composition:
            var arrow = Path()
            arrow.move(to: point(size * 0.9, size * 0.45))
            arrow.addLine(to: end)
            arrow.addLine(to: point(size * 0.9, -size * 0.45))
            context.stroke(arrow, with: .color(ink), lineWidth: 1.3)
            if relation.kind == .composition {
                let back = atan2(start.y - end.y, start.x - end.x)
                func diamondPoint(_ distance: Double, _ spread: Double) -> CGPoint {
                    CGPoint(x: start.x - distance * cos(back) + spread * sin(back), y: start.y - distance * sin(back) - spread * cos(back))
                }
                var diamond = Path()
                diamond.move(to: start)
                diamond.addLine(to: diamondPoint(size * 0.8, size * 0.45))
                diamond.addLine(to: diamondPoint(size * 1.6, 0))
                diamond.addLine(to: diamondPoint(size * 0.8, -size * 0.45))
                diamond.closeSubpath()
                context.fill(diamond, with: .color(ink))
            }
            if let label = relation.label {
                let text = Text(relation.isMany ? "\(label) [*]" : label).font(.system(size: 10 * max(0.7, scale))).foregroundColor(.secondary)
                context.draw(text, at: CGPoint(x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 - 8), anchor: .center)
            }
        }
    }
}

/// One type: coloured header with stereotype and name, then the members in UML notation.
struct TypeBoxView: View {
    let box: DiagramBox
    let model: DiagramEditorModel?
    let isSelected: Bool

    private var type: DiagramType { box.type }

    static func color(for kind: DiagramTypeKind) -> Color {
        switch kind {
        case .class: return Color(nsColor: SyntaxColors.type)
        case .interface: return Color(nsColor: SyntaxColors.interface)
        case .struct, .recordStruct: return Color(nsColor: SyntaxColors.method)
        case .record: return Color(nsColor: SyntaxColors.member)
        case .enum: return Color(nsColor: SyntaxColors.string)
        }
    }

    var body: some View {
        let tint = type.isExternal ? Color.secondary : Self.color(for: type.kind)
        VStack(spacing: 0) {
            VStack(spacing: 1) {
                if !type.stereotype.isEmpty {
                    Text(type.stereotype).font(.system(size: 9.5)).foregroundStyle(.secondary)
                }
                HStack(spacing: 4) {
                    Text(type.displayName)
                        .font(.system(size: 13, weight: .bold))
                        .italic(type.kind == .interface || type.modifiers.contains(.abstract))
                        .lineLimit(1)
                    if let model, !type.isExternal, !type.members.isEmpty {
                        Button {
                            model.toggleCollapsed(type.fullName)
                        } label: {
                            Image(systemName: box.collapsed ? "chevron.down" : "chevron.up").font(.system(size: 8, weight: .bold))
                        }
                        .buttonStyle(.plain)
                        .foregroundStyle(.tertiary)
                    }
                }
            }
            .frame(maxWidth: .infinity)
            .frame(height: BoxMetrics.headerHeight)
            .background(tint.opacity(type.isExternal ? 0.08 : 0.16))
            .overlay(alignment: .top) { Rectangle().fill(tint).frame(height: 3) }

            if !box.collapsed && !type.isExternal && !type.members.isEmpty {
                Divider()
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(type.members.enumerated()), id: \.offset) { _, member in
                        MemberLine(member: member)
                            .frame(height: BoxMetrics.lineHeight)
                            .contentShape(Rectangle())
                            .onTapGesture(count: 2) {
                                if let model { Task { await model.openInEditor(type, member: member) } }
                            }
                    }
                }
                .padding(.horizontal, BoxMetrics.padding - 2)
                .padding(.top, BoxMetrics.padding / 2)
                .frame(maxWidth: .infinity, alignment: .leading)
                Spacer(minLength: 0)
            }
        }
        .background(Color(nsColor: .controlBackgroundColor).opacity(type.isExternal ? 0.6 : 1))
        .clipShape(RoundedRectangle(cornerRadius: 9, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .strokeBorder(isSelected ? Color.vsAccent : Color.primary.opacity(type.isExternal ? 0.15 : 0.22), style: StrokeStyle(lineWidth: isSelected ? 2 : 1, dash: type.isExternal ? [4, 3] : []))
        }
        // Only the selected box casts a shadow: a shadow per box is an extra render pass on every zoom step.
        .shadow(color: .black.opacity(isSelected ? 0.22 : 0), radius: isSelected ? 10 : 0, y: isSelected ? 4 : 0)
        .contextMenu { if let model { TypeContextMenu(model: model, type: type, collapsed: box.collapsed) } }
    }
}

private struct MemberLine: View {
    let member: DiagramMember

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: symbol)
                .font(.system(size: 8, weight: .bold))
                .foregroundStyle(color)
                .frame(width: 9)
            Text(member.umlLine)
                .font(.system(size: BoxMetrics.fontSize - 0.5, design: .monospaced))
                .underline(member.modifiers.contains(.static))
                .italic(member.modifiers.contains(.abstract))
                .lineLimit(1)
        }
    }

    private var symbol: String {
        switch member.kind {
        case .field: return "circle.fill"
        case .property: return "square.fill"
        case .method: return "m.circle.fill"
        case .constructor: return "plus.circle.fill"
        case .event: return "bolt.fill"
        case .enumValue: return "number"
        }
    }

    private var color: Color {
        switch member.kind {
        case .method, .constructor: return Color(nsColor: SyntaxColors.method)
        case .enumValue: return Color(nsColor: SyntaxColors.number)
        default: return Color(nsColor: SyntaxColors.member)
        }
    }
}

private struct TypeContextMenu: View {
    @Environment(Workspace.self) private var workspace
    let model: DiagramEditorModel
    let type: DiagramType
    let collapsed: Bool

    var body: some View {
        if !type.isExternal {
            Button("Open in Editor", systemImage: "doc.text") { Task { await model.openInEditor(type) } }
            Menu("Add Member", systemImage: "plus") {
                ForEach(memberKinds, id: \.self) { kind in
                    Button(MemberKindText.title(kind)) { promptMember(kind) }
                }
            }
            if type.kind == .class || type.kind == .record {
                Menu("Base Class", systemImage: "arrow.up.square") {
                    Picker("Base Class", selection: Binding(get: { type.baseType ?? "" }, set: { base in
                        Task { await model.setBaseList(type, baseType: base.isEmpty ? nil : base, interfaces: type.interfaces) }
                    })) {
                        Text("None").tag("")
                        ForEach(model.baseCandidates(for: type)) { Text($0.displayName).tag($0.displayName) }
                        if let base = type.baseType, !model.baseCandidates(for: type).contains(where: { $0.displayName == base }) {
                            Text(base).tag(base)
                        }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }
            }
            if type.kind != .enum {
                Menu(type.kind == .interface ? "Extends Interfaces" : "Implements Interfaces", systemImage: "i.square") {
                    let candidates = model.interfaceCandidates(for: type)
                    if candidates.isEmpty {
                        Text("The project has no interfaces")
                    }
                    ForEach(candidates) { candidate in
                        Toggle(candidate.displayName, isOn: Binding(get: { type.interfaces.contains(candidate.displayName) }, set: { on in
                            let list = on ? type.interfaces + [candidate.displayName] : type.interfaces.filter { $0 != candidate.displayName }
                            Task { await model.setBaseList(type, baseType: type.baseType, interfaces: list) }
                        }))
                    }
                }
            }
            Button("Rename…", systemImage: "pencil") {
                workspace.prompt = PromptRequest(title: String(localized: "Rename \(type.name)"), message: String(localized: "All references are updated."), placeholder: type.name, initialValue: type.name, confirmTitle: String(localized: "Rename")) { name in
                    await model.renameType(type, to: name)
                }
            }
            Button(collapsed ? "Expand" : "Collapse", systemImage: "rectangle.compress.vertical") { model.toggleCollapsed(type.fullName) }
            Divider()
        }
        Button("Remove from Diagram", systemImage: "eye.slash") { model.removeFromDiagram(type.fullName) }
        if !type.isExternal {
            Button("Delete from Code…", systemImage: "trash", role: .destructive) { Task { await model.deleteType(type) } }
        }
    }

    private var memberKinds: [MemberKind] {
        type.kind == .enum ? [.enumValue] : [.property, .method, .field, .constructor, .event]
    }

    private func promptMember(_ kind: MemberKind) {
        workspace.prompt = PromptRequest(
            title: String(localized: "New \(MemberKindText.title(kind))"),
            message: MemberKindText.example(kind),
            placeholder: MemberKindText.placeholder(kind),
            confirmTitle: String(localized: "Add")
        ) { text in
            await model.addMember(to: type, kind: kind, text: text)
        }
    }
}

enum MemberKindText {
    static func title(_ kind: MemberKind) -> String {
        switch kind {
        case .field: return String(localized: "Field")
        case .property: return String(localized: "Property")
        case .method: return String(localized: "Method")
        case .constructor: return String(localized: "Constructor")
        case .event: return String(localized: "Event")
        case .enumValue: return String(localized: "Enum Value")
        }
    }

    static func placeholder(_ kind: MemberKind) -> String {
        switch kind {
        case .field: return "- _items: List<Item>"
        case .property: return "+ Name: string"
        case .method: return "+ Total(withTax: bool): decimal"
        case .constructor: return "+ (name: string)"
        case .event: return "+ Changed: EventHandler"
        case .enumValue: return "Active = 1"
        }
    }

    static func example(_ kind: MemberKind) -> String {
        switch kind {
        case .constructor, .enumValue:
            return String(localized: "UML notation: + public, - private, # protected, ~ internal. Example: \(placeholder(kind))")
        default:
            return String(localized: "Only the name is required: without a type it becomes int (methods: void, events: EventHandler), without a symbol public. Optional UML notation: + public, - private, # protected, ~ internal. Example: \(placeholder(kind))")
        }
    }
}

/// Right-click on the empty canvas: create types at the click position, add existing types, arrange.
private struct CanvasContextMenu: View {
    let model: DiagramEditorModel
    let newType: (DiagramTypeKind) -> Void
    let fit: () -> Void

    var body: some View {
        Button("New Class…", systemImage: "c.square") { newType(.class) }
        Button("New Interface…", systemImage: "i.square") { newType(.interface) }
        Button("New Record…", systemImage: "r.square") { newType(.record) }
        Button("New Struct…", systemImage: "s.square") { newType(.struct) }
        Button("New Enum…", systemImage: "e.square") { newType(.enum) }
        Divider()
        Menu("Add Existing Type", systemImage: "plus.rectangle.on.rectangle") {
            if model.missingTypes.isEmpty {
                Text("All types are on the diagram")
            } else {
                Button("Add All") { model.addExisting(model.missingTypes.map(\.fullName)) }
                Divider()
                ForEach(model.missingTypes) { type in
                    Button(type.displayName) { model.addExisting([type.fullName]) }
                }
            }
        }
        Button("Arrange automatically", systemImage: "wand.and.rays") { withAnimation(.snappy) { model.arrangeAll() } }
        Button("Fit to window", systemImage: "arrow.up.left.and.down.right.magnifyingglass", action: fit)
    }
}

/// The floating glass tool bar of the diagram.
private struct DiagramToolbar: View {
    @Environment(Workspace.self) private var workspace
    @Bindable var model: DiagramEditorModel
    @Binding var zoom: Double
    let fit: () -> Void
    let exportPNG: () -> Void

    var body: some View {
        HStack(spacing: 2) {
            tool(.select, symbol: "cursorarrow", help: "Select and move")
            Divider().frame(height: 18).padding(.horizontal, 4)
            tool(.create(.class), symbol: "c.square", help: "New class (click on the canvas)")
            tool(.create(.interface), symbol: "i.square", help: "New interface")
            tool(.create(.record), symbol: "r.square", help: "New record")
            tool(.create(.struct), symbol: "s.square", help: "New struct")
            tool(.create(.enum), symbol: "e.square", help: "New enum")
            tool(.inherit, symbol: "arrow.up.right.and.arrow.down.left.rectangle", help: "Inherit or implement: drag from the derived type to the base type")
            Divider().frame(height: 18).padding(.horizontal, 4)
            Menu {
                if model.missingTypes.isEmpty {
                    Text("All types are on the diagram")
                } else {
                    Button("Add All") { model.addExisting(model.missingTypes.map(\.fullName)) }
                    Divider()
                    ForEach(model.missingTypes) { type in
                        Button(type.displayName) { model.addExisting([type.fullName]) }
                    }
                }
            } label: {
                Image(systemName: "plus.rectangle.on.rectangle")
            }
            .menuIndicator(.hidden)
            .help("Add existing types")
            iconButton("wand.and.rays", help: "Arrange automatically") { withAnimation(.snappy) { model.arrangeAll() } }
            iconButton(model.layout.showGrid ? "grid" : "square.dashed", help: "Grid and snapping") {
                model.setGrid(show: !model.layout.showGrid, snap: !model.layout.showGrid)
            }
            Divider().frame(height: 18).padding(.horizontal, 4)
            iconButton("minus.magnifyingglass", help: "Zoom out") { zoom = max(0.2, zoom / 1.2) }
            Text("\(Int(zoom * 100)) %").font(.system(size: 11).monospacedDigit()).frame(width: 44)
            iconButton("plus.magnifyingglass", help: "Zoom in") { zoom = min(3, zoom * 1.2) }
            iconButton("arrow.up.left.and.down.right.magnifyingglass", help: "Fit to window", action: fit)
            Divider().frame(height: 18).padding(.horizontal, 4)
            iconButton("arrow.uturn.backward", help: "Undo (⌘Z)") { Task { await model.undo() } }
                .disabled(!model.canUndo)
            iconButton("arrow.uturn.forward", help: "Redo (⇧⌘Z)") { Task { await model.redo() } }
                .disabled(!model.canRedo)
            Menu {
                Button("Export as SVG…") {
                    let panel = NSSavePanel()
                    panel.nameFieldStringValue = model.name + ".svg"
                    panel.allowedContentTypes = [.svg]
                    panel.directoryURL = model.folder
                    if panel.runModal() == .OK, let url = panel.url {
                        try? model.exportSVG().write(to: url, atomically: true, encoding: .utf8)
                    }
                }
                Button("Export as PNG…", action: exportPNG)
            } label: {
                Image(systemName: "square.and.arrow.up")
            }
            .menuIndicator(.hidden)
            .help("Export")
        }
        .buttonStyle(.borderless)
        .padding(.horizontal, 8)
        .padding(.vertical, 5)
        .glassEffect(.regular, in: Capsule())
        .fixedSize()
    }

    private func tool(_ tool: DiagramEditorModel.Tool, symbol: String, help: LocalizedStringKey) -> some View {
        let isActive = model.tool == tool
        return Button {
            model.tool = tool
        } label: {
            Image(systemName: symbol)
                .frame(width: 26, height: 24)
                .foregroundStyle(isActive ? .white : .primary)
                .background(isActive ? Color.vsAccent : .clear, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
        }
        .help(help)
    }

    private func iconButton(_ symbol: String, help: LocalizedStringKey, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).frame(width: 26, height: 24)
        }
        .help(help)
    }
}

private struct EmptyDiagram: View {
    let model: DiagramEditorModel

    var body: some View {
        ContentUnavailableView {
            Label("Empty diagram", systemImage: "rectangle.3.group")
        } description: {
            Text("Pick a type tool above and click on the canvas, or add the existing types.")
        } actions: {
            if !model.missingTypes.isEmpty {
                Button("Add All \(model.missingTypes.count) Types") { model.addExisting(model.missingTypes.map(\.fullName)) }
                    .buttonStyle(.borderedProminent)
            }
        }
    }
}

/// Static rendering of the whole diagram for PNG export.
private struct DiagramSnapshot: View {
    let model: DiagramModel
    let layout: DiagramDocument

    var body: some View {
        let boxes = DiagramGeometry.boxes(model: model, document: layout)
        let bounds = DiagramGeometry.bounds(boxes, margin: 30)
        ZStack(alignment: .topLeading) {
            Color(nsColor: Palette.editorBackground)
            RelationsLayer(edges: DiagramGeometry.edges(model: model, boxes: boxes), zoom: 1, offset: CGSize(width: -bounds.minX, height: -bounds.minY))
            ForEach(boxes, id: \.type.fullName) { box in
                TypeBoxView(box: box, model: nil, isSelected: false)
                    .frame(width: box.frame.width, height: box.frame.height)
                    .position(x: box.frame.midX - bounds.minX, y: box.frame.midY - bounds.minY)
            }
        }
        .frame(width: bounds.width, height: bounds.height)
    }
}

/// Two-finger scrolling pans the canvas, pinching (or ⌘ + scroll) zooms around the pointer. A local event monitor is
/// used because SwiftUI has no scroll-wheel gesture on macOS, and because a pinch must also work over the type boxes
/// (a SwiftUI magnify gesture on the background never sees it there). Events outside the canvas pass through.
@MainActor
final class ScrollMonitor {
    enum CanvasEvent {
        case pan(CGSize)
        case zoom(Double, around: CGPoint)
    }

    private var monitor: Any?

    func install(frame: @escaping @MainActor () -> CGRect, handler: @escaping @MainActor (CanvasEvent) -> Void) {
        remove()
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.scrollWheel, .magnify]) { event in
            guard let window = event.window, let content = window.contentView else { return event }
            let point = event.locationInWindow
            let flipped = CGPoint(x: point.x, y: content.bounds.height - point.y)
            let canvasEvent: CanvasEvent
            if event.type == .magnify {
                canvasEvent = .zoom(1 + event.magnification, around: .zero)
            } else {
                let factor: CGFloat = event.hasPreciseScrollingDeltas ? 1 : 10
                let delta = CGSize(width: event.scrollingDeltaX * factor, height: event.scrollingDeltaY * factor)
                canvasEvent = event.modifierFlags.contains(.command) ? .zoom(1 + delta.height * 0.01, around: .zero) : .pan(delta)
            }
            let handled = MainActor.assumeIsolated { () -> Bool in
                let canvas = frame()
                guard canvas.contains(flipped) else { return false }
                let location = CGPoint(x: flipped.x - canvas.minX, y: flipped.y - canvas.minY)
                switch canvasEvent {
                case .pan: handler(canvasEvent)
                case let .zoom(factor, _): handler(.zoom(factor, around: location))
                }
                return true
            }
            return handled ? nil : event
        }
    }

    func remove() {
        if let monitor {
            NSEvent.removeMonitor(monitor)
        }
        monitor = nil
    }
}
