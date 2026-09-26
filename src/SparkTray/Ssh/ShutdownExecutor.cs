using System.Text.RegularExpressions;
using SparkTray.Logging;

namespace SparkTray.Ssh;

public sealed class ShutdownExecutor
{
    // Matches a leading "sudo " (optionally already carrying its own flags) so a password
    // can be inserted right after it. Deliberately narrow: this only ever rewrites a command
    // that already starts with plain "sudo ", never anything else a user might configure.
    private static readonly Regex LeadingSudo = new(@"^sudo\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISshCommandRunner _runner;
    private readonly IAppLogger _logger;

    public ShutdownExecutor(ISshCommandRunner runner, IAppLogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <summary>
    /// Fires the configured shutdown command and returns without waiting for a "success"
    /// response - a shutdown command that actually works will drop the SSH session before
    /// it can reply cleanly, so treating that as a failure would be wrong.
    ///
    /// <paramref name="sudoPassword"/> is only used when the command starts with plain
    /// "sudo " - it's rewritten to "sudo -S -p ''" and the password is piped over stdin,
    /// which lets sudo read it non-interactively instead of needing a real TTY prompt.
    /// This is the alternative to setting up passwordless sudo on the Spark: nothing is
    /// ever written to disk, and it's asked for fresh every time from the shutdown dialog.
    /// </summary>
    public async Task<bool> ShutdownAsync(SshConnectionInfo connection, string command, TimeSpan timeout, CancellationToken ct, string? sudoPassword = null)
    {
        var effectiveCommand = command;
        string? stdin = null;

        if (!string.IsNullOrEmpty(sudoPassword) && LeadingSudo.IsMatch(command))
        {
            effectiveCommand = LeadingSudo.Replace(command, "sudo -S -p '' ", count: 1);
            stdin = sudoPassword;
        }

        _logger.Info($"Sending shutdown command to {connection.Host}: {command}");
        var result = await _runner.RunAsync(connection, effectiveCommand, timeout, ct, stdin).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            _logger.Warn($"Shutdown command may not have been delivered: {result.Error}");
        }

        return result.Succeeded;
    }
}
