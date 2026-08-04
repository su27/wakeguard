using System.Text.Json;
using Microsoft.Win32;

namespace WakeGuard.Tray;

internal sealed record TraySettings(bool StartWithWindows, UiLanguage Language)
{
    internal static TraySettings CreateDefault() => new(true, UiText.DetectDefaultLanguage());
}

internal static class TraySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WakeGuard",
        "settings.json");

    internal static TraySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return TraySettings.CreateDefault();
            }

            return Deserialize(File.ReadAllText(SettingsPath));
        }
        catch (Exception exception)
        {
            TrayLog.Error("Failed to load tray settings.", exception);
            return TraySettings.CreateDefault();
        }
    }

    internal static void Save(TraySettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            Serialize(settings));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    internal static string Serialize(TraySettings settings) =>
        JsonSerializer.Serialize(settings, SerializerOptions);

    internal static TraySettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<TraySettings>(json);
        return settings is not null && Enum.IsDefined(settings.Language)
            ? settings
            : TraySettings.CreateDefault();
    }
}

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WakeGuard";

    internal static bool IsInstalledExecutable
    {
        get
        {
            var programFiles = Path.GetFullPath(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            var executable = Path.GetFullPath(Application.ExecutablePath);
            return executable.StartsWith(
                programFiles.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("The Windows startup registry key is unavailable.");
            var command = $"\"{Application.ExecutablePath}\"";
            if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal))
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }

            return;
        }

        using var existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
