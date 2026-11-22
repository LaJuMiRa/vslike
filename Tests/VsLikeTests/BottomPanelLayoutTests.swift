import AppKit
import SwiftUI
import Testing
@testable import VsLike
@testable import VsLikeKit

@MainActor
@Suite("Bottom panel layout")
struct BottomPanelLayoutTests {
    private func workspace(tab: BottomTab) -> Workspace {
        let preferences = Preferences(defaults: UserDefaults(suiteName: "vslike-tests-\(UUID().uuidString)")!)
        let workspace = Workspace(preferences: preferences)
        workspace.diagnostics.setLive([
            Diagnostic(code: "SA1641", message: "The file header company name should match the company name from the settings.", severity: .warning, filePath: "/tmp/Program.cs", startLine: 1, startColumn: 1, category: "StyleCop", projectName: "MyApp"),
        ], for: "/tmp/Program.cs")
        workspace.bottomTab = tab
        return workspace
    }

    /// The panel must fit narrow detail areas; a wider minimum centres it and cuts off both edges.
    @Test(arguments: [BottomTab.problems, .output, .terminal])
    func panel_fitsNarrowWidths(tab: BottomTab) {
        let controller = NSHostingController(rootView: BottomPanel().environment(workspace(tab: tab)))
        let needed = controller.sizeThatFits(in: CGSize(width: 420, height: 300))

        #expect(needed.width <= 420, "minimum width \(needed.width)")
    }
}
