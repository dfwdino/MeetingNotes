using System.IO;
using System.Text.Json;
using MeetingNotes.Models;

namespace MeetingNotes.Services;

public static class SettingsService
{
    /// <summary>
    /// Writable user-data folder: %LOCALAPPDATA%\MeetingNotes.
    /// All user files (settings, DB, recordings, logs) belong here, not next to the exe,
    /// because the MSIX install directory is read-only.
    /// </summary>
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingNotes");

    // User's live settings file — writable
    private static readonly string _path = Path.Combine(UserDataFolder, "settings.json");

    // Shipped template (read-only install dir) — seeded to _path on first run
    private static readonly string _templatePath = Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        // On first run (or after a clean install), seed the user file from the shipped template.
        // The template has relative paths that ResolvePath will anchor to UserDataFolder.
        if (!File.Exists(_path) && File.Exists(_templatePath))
        {
            Directory.CreateDirectory(UserDataFolder);
            try { File.Copy(_templatePath, _path); } catch { }
        }

        if (!File.Exists(_path))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                           ?? new AppSettings();

            settings.RecordingsFolder = ResolvePath(
                settings.RecordingsFolder,
                Path.Combine(UserDataFolder, "Data", "Audio"));

            settings.DatabaseFolder = ResolvePath(
                settings.DatabaseFolder,
                Path.Combine(UserDataFolder, "Data", "DB"));

            settings.WhisperCacheFolder = ResolvePath(
                settings.WhisperCacheFolder,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache", "whisper.net"));

            settings.LogFolder = ResolvePath(
                settings.LogFolder,
                Path.Combine(UserDataFolder, "Data", "Logs"));

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(UserDataFolder);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_path, json);
    }

    /// <summary>
    /// Resolves a path from settings.json:
    /// - Null / whitespace  → returns <paramref name="defaultPath"/>
    /// - Rooted with a drive letter (e.g. C:\…) → returned unchanged
    /// - UNC (\\server\share) → returned unchanged
    /// - Relative (e.g. \Data\Audio\) → combined with <see cref="UserDataFolder"/>
    ///   so relative paths always resolve inside the writable user-data folder, not
    ///   the read-only install directory.
    /// </summary>
    private static string ResolvePath(string? path, string defaultPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return defaultPath;

        if (path.Length >= 2 && path[1] == ':')
            return path;   // absolute drive path — use exactly as typed

        if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
            return path;   // UNC network path — use exactly as typed

        // Relative — anchor to the writable user-data folder, not the (possibly read-only) install dir
        return Path.GetFullPath(Path.Combine(UserDataFolder, path.TrimStart('\\', '/')));
    }
}
