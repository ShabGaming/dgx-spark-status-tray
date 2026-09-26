using System.Net;
using SparkTray.Config;
using SparkTray.Logging;

namespace SparkTray.Monitoring;

/// <summary>
/// Periodically checks whether the configured host is reachable, debounced so a single
/// dropped packet doesn't flip the tray icon: it takes <see cref="FailureThreshold"/>
/// consecutive failed checks to declare Offline, but only one success to declare Online
/// again, since a false "it's back" reads as harmless while a false "it's down" is annoying.
/// </summary>
public sealed class HealthCheckService : IDisposable
{
    private const int FailureThreshold = 2;

    private readonly Func<AppSettings> _getSettings;
    private readonly MdnsResolver _mdnsResolver;
    private readonly TcpProbe _tcpProbe;
    private readonly IAppLogger _logger;

    private readonly System.Threading.Timer _timer;
    private volatile bool _checkInFlight;
    private int _consecutiveFailures;

    private HealthStatus _status = new(HealthState.Unknown, null, null, null);

    public event EventHandler<HealthStatus>? StatusChanged;

    public HealthStatus Status => _status;

    public HealthCheckService(Func<AppSettings> getSettings, MdnsResolver mdnsResolver, TcpProbe tcpProbe, IAppLogger logger)
    {
        _getSettings = getSettings;
        _mdnsResolver = mdnsResolver;
        _tcpProbe = tcpProbe;
        _logger = logger;
        _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _getSettings().Target.CheckIntervalSeconds));
        _timer.Change(TimeSpan.Zero, interval);
    }

    public void Restart() => Start();

    /// <summary>Triggers an immediate check outside the regular timer cadence (the "Check now" menu item).</summary>
    public void CheckNow() => _ = RunCheckAsync();

    private void OnTick(object? state) => _ = RunCheckAsync();

    private async Task RunCheckAsync()
    {
        if (_checkInFlight)
        {
            return; // a slow previous check is still running; skip this tick rather than pile up
        }

        _checkInFlight = true;
        try
        {
            var settings = _getSettings();
            var hostname = settings.Target.Hostname;
            if (string.IsNullOrWhiteSpace(hostname))
            {
                Publish(new HealthStatus(HealthState.Error, DateTimeOffset.UtcNow, "No target host configured.", null));
                return;
            }

            var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.Target.CheckTimeoutSeconds));

            IPAddress? address;
            if (IPAddress.TryParse(hostname, out var literal))
            {
                address = literal;
            }
            else if (MdnsResolver.IsMdnsHostname(hostname))
            {
                address = await _mdnsResolver.ResolveAsync(hostname, timeout, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                address = await ResolveViaDnsAsync(hostname).ConfigureAwait(false);
            }

            if (address is null)
            {
                RecordFailure("Could not resolve host.");
                return;
            }

            var probe = await _tcpProbe.ProbeAsync(address, settings.Ssh.Port, settings.Target.EnableIcmpProbe, timeout, CancellationToken.None)
                .ConfigureAwait(false);

            if (probe.TcpReachable)
            {
                _consecutiveFailures = 0;
                Publish(new HealthStatus(HealthState.Online, DateTimeOffset.UtcNow, null, probe.IcmpReachable));
            }
            else
            {
                RecordFailure("TCP connect failed.", probe.IcmpReachable);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Health check failed unexpectedly", ex);
            RecordFailure(ex.Message);
        }
        finally
        {
            _checkInFlight = false;
        }
    }

    private void RecordFailure(string reason, bool? icmpReachable = null)
    {
        _consecutiveFailures++;
        var state = _consecutiveFailures >= FailureThreshold ? HealthState.Offline : HealthState.Checking;
        Publish(new HealthStatus(state, DateTimeOffset.UtcNow, reason, icmpReachable));
    }

    private static async Task<IPAddress?> ResolveViaDnsAsync(string hostname)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
            return addresses.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private void Publish(HealthStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose() => _timer.Dispose();
}
