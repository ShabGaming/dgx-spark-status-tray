using System.Diagnostics;
using SparkTray.Logging;

namespace SparkTray.Ssh;

/// <summary>
/// Fallback SSH transport that shells out to the OS-provided ssh.exe (ships by default on
/// Windows 10/11 as the OpenSSH client optional feature). Swap this in for
/// <see cref="SshNetCommandRunner"/> if SSH.NET ever proves too much trouble under Native
/// AOT - both implement <see cref="ISshCommandRunner"/> so the rest of the app doesn't care
/// which one is wired up in Program.cs.
/// </summary>
public sealed class SshExeCommandRunner : ISshCommandRunner
{
    private readonly IAppLogger _logger;

    public SshExeCommandRunner(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<SshCommandResult> RunAsync(SshConnectionInfo connection, string command, TimeSpan timeout, CancellationToken ct, string? stdin = null)
    {
        var psi = new ProcessStartInfo("ssh.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(connection.PrivateKeyPath);
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(connection.Port.ToString());
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("BatchMode=yes"); // never hang on an interactive prompt from a headless tray app
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("StrictHostKeyChecking=accept-new");
        psi.ArgumentList.Add($"{connection.Username}@{connection.Host}");
        psi.ArgumentList.Add(command);

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ssh.exe");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var stdOutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stdErrTask = process.StandardError.ReadToEndAsync(cts.Token);

            if (stdin is not null)
            {
                // ssh.exe forwards its own stdin to the remote command's stdin, so this
                // reaches the remote side the same way it would if you'd typed it over an
                // interactive session - just non-interactively, for something like `sudo -S`.
                await process.StandardInput.WriteLineAsync(stdin).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            var stdOut = await stdOutTask.ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

            return new SshCommandResult(true, process.ExitCode, stdOut, stdErr, null);
        }
        catch (Exception ex)
        {
            _logger.Warn($"ssh.exe command against {connection.Host} failed: {ex.Message}");
            return new SshCommandResult(false, -1, "", "", ex.Message);
        }
    }
}
