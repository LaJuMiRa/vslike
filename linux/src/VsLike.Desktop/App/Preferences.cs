using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// Global settings, stored in <c>$XDG_CONFIG_HOME/vslike/settings.json</c> (UserDefaults on macOS). Per-solution settings
/// live in <c>.vslike/settings.json</c>. Every change is written immediately, with the same keys as on macOS.
/// </summary>
public sealed partial class Preferences : ObservableObject
{
    public static readonly string DefaultProjectsFolder = Path.Combine(AppPaths.Home, "Developer");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, IndentSize = 2 };
    private static Preferences? _shared;
    private readonly string _file;
    private readonly JsonObject _values;
    private readonly bool _loading;

    public Preferences(string? file = null)
    {
        _file = file ?? Path.Combine(AppPaths.ConfigFolder, "settings.json");
        _values = Read(_file);
        _loading = true;
        StyleCopByDefault = Get("styleCopByDefault", false);
        FontSize = Get("fontSize", 13.0);
        ShowMinimapOfDiagnostics = Get("showDiagnosticStripe", true);
        AutoCloseBrackets = Get("autoCloseBrackets", true);
        FormatOnSave = Get("formatOnSave", false);
        AppearanceMode = Enum.TryParse<Appearance>(Get("appearance", string.Empty), ignoreCase: true, out var a) ? a : Appearance.System;
        UiLanguage = Enum.TryParse<Language>(Get("language", string.Empty), ignoreCase: true, out var l) ? l : Language.System;
        RecentSolutions = _values["recentSolutions"] is JsonArray list ? [.. list.Select(n => n?.GetValue<string>()).OfType<string>()] : [];
        StyleCopCompanyName = Get("styleCopCompanyName", FullUserName());
        StyleCopCopyrightText = Get("styleCopCopyrightText", StyleCopSettingsFile.DefaultCopyrightText);
        ProjectsFolder = Get("projectsFolder", DefaultProjectsFolder);
        _loading = false;
    }

    public enum Appearance
    {
        System,
        Light,
        Dark,
    }

    public enum Language
    {
        System,
        En,
        De,
    }

    public static Preferences Shared => _shared ??= new Preferences();

    /// <summary>Gets the monospaced font stack of the editor (SF Mono on macOS).</summary>
    public static string EditorFontFamily => "JetBrains Mono, Cascadia Mono, Fira Mono, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace";

    [ObservableProperty]
    public partial bool StyleCopByDefault { get; set; }

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial bool ShowMinimapOfDiagnostics { get; set; }

    [ObservableProperty]
    public partial bool AutoCloseBrackets { get; set; }

    [ObservableProperty]
    public partial bool FormatOnSave { get; set; }

    [ObservableProperty]
    public partial Appearance AppearanceMode { get; set; }

    /// <summary>Gets or sets the UI language; takes effect after a restart.</summary>
    [ObservableProperty]
    public partial Language UiLanguage { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> RecentSolutions { get; set; }

    /// <summary>Gets or sets the folder suggested for new solutions (default ~/Developer).</summary>
    [ObservableProperty]
    public partial string ProjectsFolder { get; set; }

    /// <summary>Gets or sets the company written into a new solution's stylecop.json (file header rules).</summary>
    [ObservableProperty]
    public partial string StyleCopCompanyName { get; set; }

    [ObservableProperty]
    public partial string StyleCopCopyrightText { get; set; }

    /// <summary>Gets the culture for Roslyn messages, the dotnet CLI and the UI ("de" or "en").</summary>
    public string EffectiveLanguage
    {
        get
        {
            if (UiLanguage != Language.System)
            {
                return UiLanguage == Language.De ? "de" : "en";
            }

            var system = Environment.GetEnvironmentVariable("LANGUAGE")?.Split(':')[0]
                ?? Environment.GetEnvironmentVariable("LC_ALL")
                ?? Environment.GetEnvironmentVariable("LC_MESSAGES")
                ?? Environment.GetEnvironmentVariable("LANG")
                ?? System.Globalization.CultureInfo.CurrentUICulture.Name;
            return system.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
        }
    }

    public void NoteRecent(string path)
    {
        RecentSolutions = [path, .. RecentSolutions.Where(p => p != path).Take(9)];
    }

    public void ApplyAppearance()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = AppearanceMode switch
        {
            Appearance.Light => ThemeVariant.Light,
            Appearance.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private static JsonObject Read(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>The user's full name from the GECOS field of /etc/passwd (NSFullUserName on macOS), else the login name.</summary>
    private static string FullUserName()
    {
        try
        {
            var user = Environment.UserName;
            var line = File.ReadLines("/etc/passwd").FirstOrDefault(l => l.StartsWith(user + ":", StringComparison.Ordinal));
            var gecos = line?.Split(':') is { Length: > 4 } fields ? fields[4].Split(',')[0].Trim() : string.Empty;
            return gecos.Length > 0 ? gecos : user;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Environment.UserName;
        }
    }

    private T Get<T>(string key, T fallback)
    {
        try
        {
            return _values[key] is JsonValue value && value.TryGetValue<T>(out var result) ? result : fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private void Set(string key, JsonNode? value)
    {
        if (_loading)
        {
            return;
        }

        if (value is null)
        {
            _values.Remove(key);
        }
        else
        {
            _values[key] = value;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, _values.ToJsonString(WriteOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; a read-only home must not break the app.
        }
    }

    partial void OnStyleCopByDefaultChanged(bool value) => Set("styleCopByDefault", value);

    partial void OnFontSizeChanged(double value) => Set("fontSize", value);

    partial void OnShowMinimapOfDiagnosticsChanged(bool value) => Set("showDiagnosticStripe", value);

    partial void OnAutoCloseBracketsChanged(bool value) => Set("autoCloseBrackets", value);

    partial void OnFormatOnSaveChanged(bool value) => Set("formatOnSave", value);

    partial void OnAppearanceModeChanged(Appearance value)
    {
        Set("appearance", value.ToString().ToLowerInvariant());
        if (!_loading)
        {
            ApplyAppearance();
        }
    }

    partial void OnUiLanguageChanged(Language value) => Set("language", value.ToString().ToLowerInvariant());

    partial void OnRecentSolutionsChanged(IReadOnlyList<string> value) => Set("recentSolutions", new JsonArray([.. value.Select(p => (JsonNode?)JsonValue.Create(p))]));

    partial void OnProjectsFolderChanged(string value) => Set("projectsFolder", value);

    partial void OnStyleCopCompanyNameChanged(string value) => Set("styleCopCompanyName", value);

    partial void OnStyleCopCopyrightTextChanged(string value) => Set("styleCopCopyrightText", value);
}
