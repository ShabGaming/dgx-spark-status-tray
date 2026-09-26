using SparkTray.Config;
using SparkTray.Monitoring;
using Xunit;

namespace SparkTray.Tests;

public class HealthCheckServiceTests
{
    // 127.0.0.1 with nothing listening on this port fails fast and deterministically
    // (connection refused), which is exactly what we need to exercise the debounce logic
    // without depending on any real network state.
    private const string UnreachableHost = "127.0.0.1";
    private const int UnreachablePort = 1;

    [Fact]
    public async Task RepeatedFailures_TakesTwoTicksToReportOffline()
    {
        var settings = BuildSettings();
        var service = new HealthCheckService(() => settings, new MdnsResolver(new NullLogger()), new TcpProbe(), new NullLogger());

        var states = new List<HealthState>();
        service.StatusChanged += (_, status) => states.Add(status.State);

        await InvokeCheckDirectlyAsync(service);
        await InvokeCheckDirectlyAsync(service);

        Assert.True(states.Count >= 2);
        Assert.NotEqual(HealthState.Offline, states[0]); // first failure: not yet confirmed offline
        Assert.Equal(HealthState.Offline, states[^1]);    // second consecutive failure: confirmed offline
    }

    [Fact]
    public async Task SuccessAfterFailures_ImmediatelyReportsOnline()
    {
        var settings = BuildSettings();
        var service = new HealthCheckService(() => settings, new MdnsResolver(new NullLogger()), new TcpProbe(), new NullLogger());

        await InvokeCheckDirectlyAsync(service);
        await InvokeCheckDirectlyAsync(service);
        Assert.Equal(HealthState.Offline, service.Status.State);

        // Point at something that will actually succeed: a local listener.
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        settings.Target.Hostname = "127.0.0.1";
        settings.Ssh.Port = port;

        await InvokeCheckDirectlyAsync(service);

        Assert.Equal(HealthState.Online, service.Status.State);
    }

    private static AppSettings BuildSettings() => new()
    {
        Target = new TargetSettings
        {
            Hostname = UnreachableHost,
            CheckIntervalSeconds = 5,
            CheckTimeoutSeconds = 1,
            EnableIcmpProbe = false,
        },
        Ssh = new SshSettings { Port = UnreachablePort },
    };

    private static async Task InvokeCheckDirectlyAsync(HealthCheckService service)
    {
        // The service's own tick is private/timer-driven; CheckNow() triggers the same
        // code path on demand, which is exactly what the "Check now" menu item does too.
        service.CheckNow();

        // CheckNow() is fire-and-forget by design (mirrors production usage from a menu
        // click); give the fast local failure/success a brief moment to complete.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var previousTimestamp = service.Status.LastCheckedUtc;
        while (DateTime.UtcNow < deadline && service.Status.LastCheckedUtc == previousTimestamp)
        {
            await Task.Delay(25);
        }
    }
}
