using System.Reflection;
using SparkTray.Monitoring;

namespace SparkTray.UI;

/// <summary>
/// Loads the four state icons from embedded resources (baked into the exe rather than
/// loose files next to it, so a single-file publish has no missing-asset failure mode).
/// </summary>
public sealed class TrayIcons : IDisposable
{
    private readonly Dictionary<HealthState, Icon> _icons = new();

    public TrayIcons()
    {
        _icons[HealthState.Unknown] = Load("spark-checking.ico");
        _icons[HealthState.Checking] = Load("spark-checking.ico");
        _icons[HealthState.Online] = Load("spark-online.ico");
        _icons[HealthState.Offline] = Load("spark-offline.ico");
        _icons[HealthState.Error] = Load("spark-error.ico");
    }

    public Icon For(HealthState state) => _icons[state];

    private static Icon Load(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded icon: {fileName}");
        return new Icon(stream);
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values.Distinct())
        {
            icon.Dispose();
        }
    }
}
