using System.IO;
using System.Text.Json;
using MeetingNotes.Models;

namespace MeetingNotes.Services;

public static class SettingsService
{
    private static readonly string _path =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true   // human-readable file
    };

    public static AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            var defaults = new AppSettings();
            Save(defaults);   // write defaults on first run so the file is visible
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                           ?? new AppSettings();

            // Resolve paths: absolute paths (drive letter) used as-is;
            // relative paths (e.g. \Data\Audio\) combined with the app directory;
            // empty paths fall back to built-in defaults.
            settings.RecordingsFolder = ResolvePath(
                settings.RecordingsFolder,
                Path.Combine(AppContext.BaseDirectory, "Data", "Recordings"));

            settings.DatabaseFolder = ResolvePath(
                settings.DatabaseFolder,
                Path.Combine(AppContext.BaseDirectory, "Data"));

            settings.WhisperCacheFolder = ResolvePath(
                settings.WhisperCacheFolder,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache", "whisper.net"));

            settings.LogFolder = ResolvePath(
                settings.LogFolder,
                Path.Combine(AppContext.BaseDirectory, "Data", "Logs"));

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_path, json);
    }

    /// <summary>
    /// Resolves a path from settings.json:
    /// - Null / whitespace  → returns <paramref name="defaultPath"/>
    /// - Rooted with a drive letter (e.g. C:\…) → returned unchanged
    /// - Relative (e.g. \Data\Audio\) → combined with AppContext.BaseDirectory
    /// </summary>
    private static string ResolvePath(string? path, string defaultPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return defaultPath;

        // Path.IsPathRooted returns true for both "C:\..." AND "\relative" on Windows.
        // A drive letter is detected by the second character being ':'.
        // A UNC network path starts with \\.
        if (path.Length >= 2 && path[1] == ':')
            return path;   // absolute drive path — use exactly as typed

        if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
            return path;   // UNC network path (\\server\share) — use exactly as typed

        // Relative (starts with \ or just a folder name) — anchor to the app folder
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path.TrimStart('\\', '/')));
    }
}
