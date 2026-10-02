using System.Text.Json;

namespace WinOpt.Engine.Gaming;

/// <summary>
/// Where the Gaming Center keeps the things it must find again next run: the
/// live session, the folders the user added, and saved presets.
///
/// The session file is the restore path — if it is missing, a session that was
/// started cannot be stopped, so it is written before the first control is
/// touched rather than at the end.
/// </summary>
public static class GamingState
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string DirectoryPath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinOpt", "gamemode");

    public static string SessionFile => Path.Combine(DirectoryPath, "current.json");
    public static string FoldersFile => Path.Combine(DirectoryPath, "folders.json");
    public static string PresetsFile => Path.Combine(DirectoryPath, "presets.json");

    /// <summary>
    /// Redirects every path to a folder of the caller's choosing. Tests use it
    /// so a session round-trip can be exercised without touching the real one.
    /// </summary>
    public static void UseDirectory(string directory)
    {
        DirectoryPath = directory;
        System.IO.Directory.CreateDirectory(DirectoryPath);
    }

    public static void ResetDirectory()
    {
        DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "gamemode");
    }

    public static void EnsureDirectory() => System.IO.Directory.CreateDirectory(DirectoryPath);

    public static T? Read<T>(string file) where T : class
    {
        try
        {
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
        }
        catch
        {
            // A half-written or hand-edited file must not make the feature
            // unusable; null means "treat as nothing stored".
            return null;
        }
    }

    public static void Write<T>(string file, T value)
    {
        EnsureDirectory();
        // Write to a sibling and move, so a crash mid-write leaves the previous
        // session file intact rather than a truncated one.
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, file, overwrite: true);
    }

    public static JsonSerializerOptions Serializer => Json;
}
