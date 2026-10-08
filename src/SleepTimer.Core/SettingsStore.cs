using System.Text.Json;
using System.Text.Json.Serialization;

namespace SleepTimer.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; }

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sleep Timer",
            "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            if (settings.SettingsSchemaVersion < 1)
            {
                if (Math.Abs(settings.PromptScale - 1.0) < 0.001) settings.PromptScale = 0.8;
                settings.SettingsSchemaVersion = 1;
            }
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.SettingsSchemaVersion = Math.Max(settings.SettingsSchemaVersion, 1);
        settings.Normalize();
        var directory = Path.GetDirectoryName(FilePath) ?? throw new InvalidOperationException("The settings path needs a parent folder.");
        Directory.CreateDirectory(directory);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}

