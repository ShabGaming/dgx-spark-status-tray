using System.Text;
using Renci.SshNet;
using SparkTray.Logging;

namespace SparkTray.Ssh;

/// <summary>
/// Primary SSH transport, backed by SSH.NET. Opens a fresh connection per command -
/// this app only makes a handful of SSH calls (a pre-shutdown check, a shutdown), so a
/// pooled/long-lived session would add reconnect-on-drop complexity for no real benefit.
/// </summary>
public sealed class SshNetCommandRunner : ISshCommandRunner
{
    private readonly IAppLogger _logger;

    public SshNetCommandRunner(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<SshCommandResult> RunAsync(SshConnectionInfo connection, string command, TimeSpan timeout, CancellationToken ct, string? stdin = null)
    {
        // SSH.NET's classic API is synchronous; running it on a pool thread keeps the
        // caller (UI thread / health-check loop) fully async without blocking on it.
        return Task.Run(() => RunBlocking(connection, command, timeout, stdin), ct);
    }

    private SshCommandResult RunBlocking(SshConnectionInfo connection, string command, TimeSpan timeout, string? stdin)
    {
        try
        {
            var keyFile = new PrivateKeyFile(connection.PrivateKeyPath);
            var authMethod = new PrivateKeyAuthenticationMethod(connection.Username, keyFile);
            var connectionInfo = new ConnectionInfo(connection.Host, connection.Port, connection.Username, authMethod)
            {
                Timeout = timeout,
            };

            using var client = new SshClient(connectionInfo);
            client.Connect();

            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = timeout;

            string output;
            if (stdin is null)
            {
                output = cmd.Execute();
            }
            else
            {
                // SSH.NET only allows writing to a command's input stream once execution
                // has actually started (confirmed directly: calling CreateInputStream()
                // beforehand throws "The input stream can be used only during execution").
                var executeTask = cmd.ExecuteAsync(CancellationToken.None);
                var inputStream = cmd.CreateInputStream();
                var bytes = Encoding.UTF8.GetBytes(stdin + "\n");
                inputStream.Write(bytes, 0, bytes.Length);
                inputStream.Flush();
                inputStream.Close();
                executeTask.GetAwaiter().GetResult();
                output = cmd.Result;
            }

            client.Disconnect();

            return new SshCommandResult(true, cmd.ExitStatus ?? -1, output, cmd.Error, null);
        }
        catch (Exception ex)
        {
            _logger.Warn($"SSH command against {connection.Host} failed: {ex.Message}");
            return new SshCommandResult(false, -1, "", "", ex.Message);
        }
    }
}
