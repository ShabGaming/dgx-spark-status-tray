using SparkTray.App;
using SparkTray.Config;
using SparkTray.Logging;
using SparkTray.Ssh;

namespace SparkTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new SingleInstanceGuard();
        if (!singleInstance.IsFirstInstance)
        {
            MessageBox.Show("DGX Spark Status Tray is already running.", "DGX Spark Status Tray");
            return;
        }

        ApplicationConfiguration.Initialize();

        var bootstrapLogger = new RollingFileLogger(enabled: true);
        bootstrapLogger.Info($"Starting (pid {Environment.ProcessId}, path {Environment.ProcessPath}).");
        var settingsStore = new SettingsStore(bootstrapLogger);
        var initialSettings = settingsStore.Load();
        bootstrapLogger.SetEnabled(initialSettings.Logging.Enabled);

        using var context = new TrayApplicationContext(settingsStore, bootstrapLogger, () => new SshNetCommandRunner(bootstrapLogger));
        Application.Run(context);

        bootstrapLogger.Dispose();
    }
}
