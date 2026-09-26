namespace SparkTray.Ssh;

public sealed record SshConnectionInfo(string Host, int Port, string Username, string PrivateKeyPath);

public sealed record SshCommandResult(bool Succeeded, int ExitCode, string StandardOutput, string StandardError, string? Error);

public interface ISshCommandRunner
{
    /// <summary>
    /// Runs a command over SSH. <paramref name="stdin"/>, when provided, is written to the
    /// command's input stream right after it starts and then closed - this is what lets
    /// <c>sudo -S</c> read a password non-interactively instead of needing a TTY.
    /// </summary>
    Task<SshCommandResult> RunAsync(SshConnectionInfo connection, string command, TimeSpan timeout, CancellationToken ct, string? stdin = null);
}
