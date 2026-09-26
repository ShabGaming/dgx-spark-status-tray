using System.Threading.Channels;

namespace SparkTray.Logging;

/// <summary>
/// Minimal append-only logger for %AppData%\SparkTray\logs\spark-tray.log.
/// Writes are queued and flushed on a single background thread so a slow disk
/// never blocks a health check or a UI event handler. Rotates to a single
/// ".log.bak" once the active file crosses <see cref="MaxBytes"/>.
/// </summary>
public sealed class RollingFileLogger : IAppLogger, IDisposable
{
    private const long MaxBytes = 1 * 1024 * 1024;

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SparkTray", "logs");

    private static readonly string LogPath = Path.Combine(LogDir, "spark-tray.log");
    private static readonly string BackupPath = Path.Combine(LogDir, "spark-tray.log.bak");

    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly Task _writerTask;
    private volatile bool _enabled;

    public RollingFileLogger(bool enabled)
    {
        _enabled = enabled;
        _writerTask = Task.Run(ProcessQueueAsync);
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public void Info(string message) => Enqueue("INFO", message);

    public void Warn(string message) => Enqueue("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Enqueue("ERROR", exception is null ? message : $"{message} :: {exception}");

    private void Enqueue(string level, string message)
    {
        if (!_enabled)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        _queue.Writer.TryWrite(line);
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var line in _queue.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                RotateIfNeeded();
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the app; drop the line and move on.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath))
        {
            return;
        }

        var info = new FileInfo(LogPath);
        if (info.Length < MaxBytes)
        {
            return;
        }

        File.Copy(LogPath, BackupPath, overwrite: true);
        File.Delete(LogPath);
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // best effort on shutdown
        }
    }
}
