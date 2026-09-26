using System.Text.Json;
using SparkTray.Logging;

namespace SparkTray.Config;

public sealed class SettingsStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SparkTray");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private readonly IAppLogger _logger;

    public SettingsStore(IAppLogger logger)
    {
        _logger = logger;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppSettings.CreateDefault();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                   ?? AppSettings.CreateDefault();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load settings, using defaults: {ex.Message}");
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
    }
}
