import Foundation

/// The `stylecop.json` VsLike creates next to a solution: company name and copyright text for the file header rules
/// (SA1633 ff.). StyleCop replaces `{companyName}` and `{fileName}` in the copyright text.
public enum StyleCopSettingsFile {
    public static let fileName = "stylecop.json"
    public static let defaultCopyrightText = "Copyright (c) {companyName}. All rights reserved."
    static let schema = "https://raw.githubusercontent.com/DotNetAnalyzers/StyleCopAnalyzers/master/StyleCop.Analyzers/StyleCop.Analyzers/Settings/stylecop.schema.json"

    /// Where the file lives for a solution (or project) file: in its folder, where the engine finds it for every project.
    public static func url(forSolution solution: URL) -> URL {
        solution.deletingLastPathComponent().appendingPathComponent(fileName)
    }

    public static func contents(companyName: String, copyrightText: String) throws -> String {
        let settings: [String: Any] = [
            "$schema": schema,
            "settings": [
                "documentationRules": [
                    "companyName": companyName,
                    "copyrightText": copyrightText.isEmpty ? defaultCopyrightText : copyrightText,
                ],
            ],
        ]
        let data = try JSONSerialization.data(withJSONObject: settings, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
        return String(decoding: data, as: UTF8.self) + "\n"
    }
}
