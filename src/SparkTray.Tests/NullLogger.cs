using SparkTray.Logging;

namespace SparkTray.Tests;

internal sealed class NullLogger : IAppLogger
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
