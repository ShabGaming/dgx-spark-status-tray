namespace SparkTray.Monitoring;

public enum HealthState
{
    Unknown,
    Checking,
    Online,
    Offline,
    Error,
}

public sealed record HealthStatus(HealthState State, DateTimeOffset? LastCheckedUtc, string? LastError, bool? LastIcmpReachable);
