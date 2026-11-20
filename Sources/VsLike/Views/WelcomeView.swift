import SwiftUI
import VsLikeKit

/// Start screen: open or create a solution, recent solutions, and a check of the .NET SDK.
struct WelcomeView: View {
    @Environment(Workspace.self) private var workspace
    @Environment(Preferences.self) private var preferences
    @State private var sdk: SdkInfo?
    @State private var sdkChecked = false

    var body: some View {
        HStack(spacing: 0) {
            VStack(spacing: 26) {
                Spacer()
                AppMark()
                VStack(spacing: 6) {
                    Text("VsLike")
                        .font(.system(size: 34, weight: .bold, design: .rounded))
                    Text("C# for the Mac")
                        .font(.title3)
                        .foregroundStyle(.secondary)
                }
                VStack(spacing: 10) {
                    WelcomeAction(title: "Open…", subtitle: "Solution, project, folder or file", symbol: "folder", shortcut: "⌘O") {
                        OpenPanel.chooseAndOpen(workspace)
                    }
                    WelcomeAction(title: "New Solution…", subtitle: "Console app, library, tests, web …", symbol: "plus.square.on.square", shortcut: "⇧⌘N") {
                        workspace.isNewSolutionPresented = true
                    }
                }
                .frame(width: 360)
                Spacer()
                SdkStatus(sdk: sdk, checked: sdkChecked)
                    .padding(.bottom, 22)
            }
            .frame(maxWidth: .infinity)

            Divider()

            VStack(alignment: .leading, spacing: 0) {
                Text("Recent")
                    .font(.headline)
                    .padding(.horizontal, 20)
                    .padding(.top, 52)
                    .padding(.bottom, 8)
                if preferences.recentSolutions.isEmpty {
                    Text("Solutions you open appear here.")
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 20)
                    Spacer()
                } else {
                    List(preferences.recentSolutions, id: \.self) { path in
                        RecentRow(path: path)
                    }
                    .listStyle(.sidebar)
                    .scrollContentBackground(.hidden)
                }
            }
            .frame(width: 320)
            .background(.ultraThinMaterial)
        }
        .ignoresSafeArea(edges: .top)
        .task {
            if let runner = try? CommandRunner.dotnet() {
                sdk = await DotnetLocator.listSdks(runner: runner).first
            }
            sdkChecked = true
        }
    }
}

/// The app icon motif drawn in SwiftUI (amber brackets on a slate tile).
struct AppMark: View {
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 26, style: .continuous)
                .fill(LinearGradient(colors: [Color(red: 0.20, green: 0.23, blue: 0.29), Color(red: 0.09, green: 0.10, blue: 0.13)], startPoint: .top, endPoint: .bottom))
                .shadow(color: .black.opacity(0.25), radius: 14, y: 8)
            Image(systemName: "chevron.left.forwardslash.chevron.right")
                .font(.system(size: 44, weight: .bold))
                .foregroundStyle(LinearGradient(colors: [Color(red: 1, green: 0.80, blue: 0.40), Color(red: 0.91, green: 0.58, blue: 0.17)], startPoint: .top, endPoint: .bottom))
        }
        .frame(width: 112, height: 112)
    }
}

private struct WelcomeAction: View {
    let title: LocalizedStringKey
    let subtitle: LocalizedStringKey
    let symbol: String
    let shortcut: String
    let action: () -> Void
    @State private var isHovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 14) {
                Image(systemName: symbol)
                    .font(.system(size: 18, weight: .medium))
                    .foregroundStyle(Color.vsAccent)
                    .frame(width: 34, height: 34)
                    .background(Color.vsAccent.opacity(0.14), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.system(size: 14, weight: .semibold))
                    Text(subtitle).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Text(shortcut).font(.caption).foregroundStyle(.tertiary)
            }
            .padding(12)
            .background(isHovering ? Color.primary.opacity(0.07) : Color.primary.opacity(0.035), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { isHovering = $0 }
        .animation(.easeOut(duration: 0.12), value: isHovering)
    }
}

private struct RecentRow: View {
    @Environment(Workspace.self) private var workspace
    @Environment(Preferences.self) private var preferences
    let path: String

    var body: some View {
        let url = URL(fileURLWithPath: path)
        Button {
            Task { await workspace.open(url) }
        } label: {
            HStack(spacing: 10) {
                Image(systemName: url.pathExtension.hasPrefix("sln") ? "square.stack.3d.up.fill" : url.pathExtension == "csproj" ? "shippingbox.fill" : "folder.fill")
                    .foregroundStyle(Color.vsAccent)
                    .frame(width: 20)
                VStack(alignment: .leading, spacing: 1) {
                    Text(url.deletingPathExtension().lastPathComponent).fontWeight(.medium).lineLimit(1)
                    Text((url.deletingLastPathComponent().path as NSString).abbreviatingWithTildeInPath)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
            }
            .padding(.vertical, 3)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .contextMenu {
            Button("Remove from List") { preferences.recentSolutions.removeAll { $0 == path } }
            Button("Reveal in Finder") { NSWorkspace.shared.activateFileViewerSelecting([url]) }
        }
    }
}

private struct SdkStatus: View {
    let sdk: SdkInfo?
    let checked: Bool

    var body: some View {
        if !checked {
            ProgressView().controlSize(.small)
        } else if let sdk, sdk.isSupported {
            Label(".NET SDK \(sdk.fullVersion)", systemImage: "checkmark.circle.fill")
                .font(.caption)
                .foregroundStyle(.secondary)
        } else {
            VStack(spacing: 6) {
                Label(sdk == nil ? "The .NET SDK is not installed" : "VsLike needs the .NET SDK 10 or newer (found \(sdk!.fullVersion))", systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(Color.vsWarning)
                Link("Download the .NET SDK", destination: URL(string: "https://dotnet.microsoft.com/download")!)
                    .font(.caption)
            }
        }
    }
}
