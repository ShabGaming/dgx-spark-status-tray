namespace SparkTray.App;

/// <summary>Prevents a second tray icon from ever spawning. Held for the process lifetime.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, @"Local\SparkTray.SingleInstance", out var createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
