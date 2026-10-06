using System.Text.Json;

namespace PW64Editor.Core.Workspace;

/// <summary>
/// Editor-wide settings that apply to every project on this computer.
/// </summary>
public sealed class EditorSettings
{
    /// <summary>Recently opened project folders, most recent first.</summary>
    public List<string> RecentProjects { get; set; } = [];
}

/// <summary>
/// The editor's own data folder: settings and the private copy of the clean ROM.
/// </summary>
/// <remarks>
/// <para>Default location: <c>%AppData%\HawksHangar</c>.</para>
/// <code>
/// HawksHangar/
///   settings.json
///   rom/
///     Pilotwings 64 (USA).z64   private copy of the clean ROM (always big-endian)
/// </code>
/// <para>The root folder can be chosen freely, which keeps tests away from the real settings.</para>
/// </remarks>
public sealed class EditorStorage
{
    private const string SettingsFileName = "settings.json";
    private const string RomFolderName = "rom";
    private const string RomFileName = "Pilotwings 64 (USA).z64";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public EditorStorage(string rootFolder)
    {
        RootFolder = Path.GetFullPath(rootFolder);
    }

    /// <summary>The storage in the user's roaming application data folder.</summary>
    public static EditorStorage CreateDefault() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HawksHangar"));

    /// <summary>Root of the editor's data folder.</summary>
    public string RootFolder { get; }

    /// <summary>Where the private copy of the clean ROM is kept.</summary>
    public string CleanRomPath => Path.Combine(RootFolder, RomFolderName, RomFileName);

    private string SettingsPath => Path.Combine(RootFolder, SettingsFileName);

    /// <summary>
    /// Loads the settings. Missing or unreadable settings give the defaults, because losing
    /// a list of recent projects must never stop the editor from starting.
    /// </summary>
    public EditorSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new EditorSettings()
                : new EditorSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new EditorSettings();
        }
    }

    /// <summary>Saves the settings.</summary>
    public void SaveSettings(EditorSettings settings)
    {
        Directory.CreateDirectory(RootFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
