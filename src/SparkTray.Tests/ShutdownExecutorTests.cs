using SparkTray.Ssh;
using Xunit;

namespace SparkTray.Tests;

public class ShutdownExecutorTests
{
    [Fact]
    public async Task ShutdownAsync_NoSudoPassword_RunsCommandUnchangedWithNoStdin()
    {
        var runner = new RecordingSshCommandRunner();
        var executor = new ShutdownExecutor(runner, new NullLogger());

        await executor.ShutdownAsync(DummyConnection(), "sudo shutdown -h now", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("sudo shutdown -h now", runner.LastCommand);
        Assert.Null(runner.LastStdin);
    }

    [Fact]
    public async Task ShutdownAsync_WithSudoPassword_RewritesCommandAndPipesStdin()
    {
        var runner = new RecordingSshCommandRunner();
        var executor = new ShutdownExecutor(runner, new NullLogger());

        await executor.ShutdownAsync(DummyConnection(), "sudo shutdown -h now", TimeSpan.FromSeconds(5), CancellationToken.None, "my-password");

        Assert.Equal("sudo -S -p '' shutdown -h now", runner.LastCommand);
        Assert.Equal("my-password", runner.LastStdin);
    }

    [Fact]
    public async Task ShutdownAsync_SudoPasswordGiven_ButCommandDoesNotStartWithSudo_LeavesCommandAlone()
    {
        var runner = new RecordingSshCommandRunner();
        var executor = new ShutdownExecutor(runner, new NullLogger());

        await executor.ShutdownAsync(DummyConnection(), "/home/user/shutdown-wrapper.sh", TimeSpan.FromSeconds(5), CancellationToken.None, "my-password");

        Assert.Equal("/home/user/shutdown-wrapper.sh", runner.LastCommand);
        Assert.Null(runner.LastStdin);
    }

    private static SshConnectionInfo DummyConnection() => new("host", 22, "user", "key");

    private sealed class RecordingSshCommandRunner : ISshCommandRunner
    {
        public string? LastCommand { get; private set; }
        public string? LastStdin { get; private set; }

        public Task<SshCommandResult> RunAsync(SshConnectionInfo connection, string command, TimeSpan timeout, CancellationToken ct, string? stdin = null)
        {
            LastCommand = command;
            LastStdin = stdin;
            return Task.FromResult(new SshCommandResult(true, 0, "", "", null));
        }
    }
}
