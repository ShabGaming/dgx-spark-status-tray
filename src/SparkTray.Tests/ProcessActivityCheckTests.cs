using SparkTray.Ssh;
using Xunit;

namespace SparkTray.Tests;

public class ProcessActivityCheckTests
{
    [Fact]
    public async Task RunAsync_FiltersOutIgnoredProcessNames()
    {
        var runner = new FakeSshCommandRunner("systemd\nsshd\npython3\nbash\n");
        var ignoreList = new[] { "systemd", "sshd", "bash" };

        var result = await ProcessActivityCheck.RunAsync(
            runner, DummyConnection(), ignoreList, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal(new[] { "python3" }, result.ActiveProcessNames);
    }

    [Fact]
    public async Task RunAsync_NothingLeftAfterFiltering_ReportsEmpty()
    {
        var runner = new FakeSshCommandRunner("systemd\nsshd\n");
        var ignoreList = new[] { "systemd", "sshd" };

        var result = await ProcessActivityCheck.RunAsync(
            runner, DummyConnection(), ignoreList, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.Available);
        Assert.Empty(result.ActiveProcessNames);
    }

    [Fact]
    public async Task RunAsync_CommandFails_ReportsUnavailable()
    {
        var runner = new FakeSshCommandRunner(succeeded: false);

        var result = await ProcessActivityCheck.RunAsync(
            runner, DummyConnection(), Array.Empty<string>(), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task RunAsync_IgnoreListIsCaseInsensitive()
    {
        var runner = new FakeSshCommandRunner("SystemD\npython3\n");
        var ignoreList = new[] { "systemd" };

        var result = await ProcessActivityCheck.RunAsync(
            runner, DummyConnection(), ignoreList, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(new[] { "python3" }, result.ActiveProcessNames);
    }

    [Fact]
    public async Task RunAsync_WildcardIgnoreEntry_MatchesByPrefix()
    {
        var runner = new FakeSshCommandRunner("gsd-power\ngsd-sound\npython3\n");
        var ignoreList = new[] { "gsd-*" };

        var result = await ProcessActivityCheck.RunAsync(
            runner, DummyConnection(), ignoreList, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(new[] { "python3" }, result.ActiveProcessNames);
    }

    [Fact]
    public async Task RunAsync_KernelThreadsAreAlwaysFiltered_EvenWithoutBeingInIgnoreList()
    {
        // Real output captured from a DGX Spark: kernel worker/helper threads have names
        // that embed a per-core or per-subsystem index, so no fixed ignore list could name
        // them all - they must be filtered structurally instead.
        var runner = new FakeSshCommandRunner(
            "kworker/9:1H-kblockd\nrcu_exp_par_gp_kthread_worker/1\nmigration/0\n(sd-pam)\n(udev-worker)\npython3\n");

        var result = await ProcessActivityCheck.RunAsync(
            runner, DummyConnection(), Array.Empty<string>(), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(new[] { "python3" }, result.ActiveProcessNames);
    }

    [Fact]
    public async Task RunAsync_RealSparkDesktopNoise_IsFilteredByDefaultIgnoreList()
    {
        var realOutput = string.Join('\n', new[]
        {
            "systemd", "dbus-run-sessio", "dbus-daemon", "gnome-session-b", "at-spi-bus-laun",
            "gnome-shell", "mutter-x11-fram", "at-spi2-registr", "colord", "xdg-permission-",
            "gjs", "upowerd", "gsd-sharing", "gsd-wacom", "ibus-daemon", "ibus-x11", "ibus-portal",
            "pipewire", "pipewire-pulse", "wireplumber", "xdg-document-po", "fusermount3", "sshd",
            "python3",
        });
        var runner = new FakeSshCommandRunner(realOutput);
        var ignoreList = new SparkTray.Config.ShutdownSettings().ProcessIgnoreList;

        var result = await ProcessActivityCheck.RunAsync(
            runner, DummyConnection(), ignoreList, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(new[] { "python3" }, result.ActiveProcessNames);
    }

    private static SshConnectionInfo DummyConnection() => new("host", 22, "user", "key");

    private sealed class FakeSshCommandRunner : ISshCommandRunner
    {
        private readonly bool _succeeded;
        private readonly string _stdOut;

        public FakeSshCommandRunner(string stdOut = "", bool succeeded = true)
        {
            _stdOut = stdOut;
            _succeeded = succeeded;
        }

        public Task<SshCommandResult> RunAsync(SshConnectionInfo connection, string command, TimeSpan timeout, CancellationToken ct, string? stdin = null) =>
            Task.FromResult(_succeeded
                ? new SshCommandResult(true, 0, _stdOut, "", null)
                : new SshCommandResult(false, -1, "", "", "simulated failure"));
    }
}
