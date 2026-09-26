namespace SparkTray.Ssh;

public sealed record GpuProcess(int Pid, string ProcessName, string UsedMemory);

public sealed record GpuActivityResult(bool Available, IReadOnlyList<GpuProcess> Processes, string? UtilizationSummary, string? Error);

/// <summary>
/// Detects active GPU/CUDA work via nvidia-smi - the strongest, lowest-false-positive
/// signal that "real work" is running on the Spark before we let someone shut it down.
/// </summary>
public static class GpuActivityCheck
{
    private const string ComputeAppsCommand =
        "nvidia-smi --query-compute-apps=pid,process_name,used_memory --format=csv,noheader";

    private const string UtilizationCommand =
        "nvidia-smi --query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader";

    public static async Task<GpuActivityResult> RunAsync(ISshCommandRunner runner, SshConnectionInfo connection, TimeSpan timeout, CancellationToken ct)
    {
        var appsResult = await runner.RunAsync(connection, ComputeAppsCommand, timeout, ct).ConfigureAwait(false);
        if (!appsResult.Succeeded)
        {
            return new GpuActivityResult(false, Array.Empty<GpuProcess>(), null, appsResult.Error ?? "nvidia-smi unavailable");
        }

        var processes = ParseComputeApps(appsResult.StandardOutput);

        string? utilization = null;
        var utilResult = await runner.RunAsync(connection, UtilizationCommand, timeout, ct).ConfigureAwait(false);
        if (utilResult.Succeeded)
        {
            utilization = utilResult.StandardOutput.Trim();
        }

        return new GpuActivityResult(true, processes, utilization, null);
    }

    private static List<GpuProcess> ParseComputeApps(string output)
    {
        var processes = new List<GpuProcess>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length != 3)
            {
                continue;
            }

            if (int.TryParse(parts[0].Trim(), out var pid))
            {
                processes.Add(new GpuProcess(pid, parts[1].Trim(), parts[2].Trim()));
            }
        }

        return processes;
    }
}
