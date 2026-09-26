using System.Text.RegularExpressions;

namespace SparkTray.Ssh;

public sealed record ProcessActivityResult(bool Available, IReadOnlyList<string> ActiveProcessNames, string? Error);

/// <summary>
/// Catches non-GPU work (a plain CPU-bound script, a Jupyter server, a long-running build)
/// that <see cref="GpuActivityCheck"/> can't see, by listing process names and filtering out
/// expected noise.
///
/// Tested directly against a real DGX Spark: it runs a full GNOME desktop session (not a
/// headless server), and lists dozens of kernel worker/helper threads whose names embed a
/// per-core or per-subsystem index (e.g. "kworker/9:1H-kblockd", "rcu_exp_par_gp_kthread_worker/1")
/// that no fixed exact-match list could ever fully enumerate. So filtering happens in two
/// layers: a built-in structural filter for kernel threads (never real "work", regardless of
/// settings), plus the user-editable ignore list for everything else - which also supports a
/// trailing "*" wildcard (e.g. "gsd-*") for exactly this kind of daemon-with-many-names case.
/// </summary>
public static class ProcessActivityCheck
{
    private const string Command = "ps -eo comm= --no-headers";

    // Kernel worker/helper threads: always noise, never something a user would recognize as
    // "their work" running, and their names vary too much (per CPU core, per kernel subsystem)
    // to list exhaustively. Matched regardless of the configured ignore list.
    private static readonly Regex KernelThreadPattern = new(
        @"^(kworker/|kthreadd$|ksoftirqd/|migration/|rcu_|watchdog/|cpuhp/|idle_inject/|pool_workqueue|oom_reaper$|khugepaged$|kcompactd|kswapd|writeback$|kblockd|scsi_eh_|ipv6_addrconf$)",
        RegexOptions.Compiled);

    public static async Task<ProcessActivityResult> RunAsync(
        ISshCommandRunner runner,
        SshConnectionInfo connection,
        IReadOnlyCollection<string> ignoreList,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var result = await runner.RunAsync(connection, Command, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new ProcessActivityResult(false, Array.Empty<string>(), result.Error ?? "ps unavailable");
        }

        var exactIgnore = new HashSet<string>(
            ignoreList.Where(entry => !entry.EndsWith('*')),
            StringComparer.OrdinalIgnoreCase);
        var prefixIgnore = ignoreList
            .Where(entry => entry.EndsWith('*'))
            .Select(entry => entry[..^1])
            .Where(prefix => prefix.Length > 0)
            .ToList();

        var active = result.StandardOutput
            .Split('\n')
            .Select(line => line.Trim())
            .Where(name => name.Length > 0)
            .Where(name => !IsKernelThread(name))
            .Where(name => !IsParenthesizedPlaceholder(name))
            .Where(name => !exactIgnore.Contains(name))
            .Where(name => !prefixIgnore.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProcessActivityResult(true, active, null);
    }

    private static bool IsKernelThread(string name) => KernelThreadPattern.IsMatch(name);

    // ps shows some kernel-managed helpers/placeholders wrapped in parentheses, e.g.
    // "(sd-pam)", "(udev-worker)" - structurally never "user work" either.
    private static bool IsParenthesizedPlaceholder(string name) =>
        name.Length > 1 && name[0] == '(' && name[^1] == ')';
}
