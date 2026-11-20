// swift-tools-version:6.2
import PackageDescription

let package = Package(
    name: "VsLike",
    defaultLocalization: "en",
    platforms: [.macOS(.v26)],
    products: [
        .executable(name: "VsLike", targets: ["VsLike"]),
        .library(name: "VsLikeKit", targets: ["VsLikeKit"]),
    ],
    dependencies: [
        // Terminal emulator for the built-in terminal (MIT). Pinned to 1.11.2: later versions add Metal shaders, which
        // need the separately downloaded Metal toolchain to build.
        .package(url: "https://github.com/migueldeicaza/SwiftTerm.git", exact: "1.11.2"),
    ],
    targets: [
        // Foundation only: engine client (JSON-RPC), dotnet CLI (build, run, tests, templates, NuGet), solution and
        // project files, debugger client (DAP), C# lexer, class diagram layout and export. No AppKit.
        .target(name: "VsLikeKit"),
        // The macOS app: SwiftUI shell, AppKit editor, terminal, diagram canvas. Localizations live in Resources/
        // and are copied into the .app by build.sh, so a plain `swift build` binary shows the English texts.
        .executableTarget(
            name: "VsLike",
            dependencies: ["VsLikeKit", .product(name: "SwiftTerm", package: "SwiftTerm")],
            exclude: ["Resources"]
        ),
        .testTarget(name: "VsLikeKitTests", dependencies: ["VsLikeKit"], resources: [.copy("Fixtures")]),
        .testTarget(name: "VsLikeTests", dependencies: ["VsLike", "VsLikeKit"]),
    ]
)
