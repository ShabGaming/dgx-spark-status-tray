using SparkTray.Logging;

namespace SparkTray.NvidiaSyncImport;

public sealed record NvidiaSyncImportResult(string Hostname, string Username, int Port, string PrivateKeyPath);

/// <summary>
/// Best-effort import of the SSH connection details NVIDIA Sync already set up for the
/// Spark. This is a one-shot convenience, never a runtime dependency: Sync's file layout
/// is undocumented/unofficial, so every failure mode here (not installed, moved, changed
/// format) just means "nothing to import," not a crash.
/// </summary>
public static class NvidiaSyncImporter
{
    public static NvidiaSyncImportResult? TryImport(IAppLogger logger)
    {
        try
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NVIDIA Corporation", "Sync", "config");

            var sshConfigPath = Path.Combine(configDir, "ssh_config");
            if (!File.Exists(sshConfigPath))
            {
                return null;
            }

            var entries = SshConfigParser.Parse(sshConfigPath);
            var entry = entries.FirstOrDefault(e => e.HostAlias.Contains("spark", StringComparison.OrdinalIgnoreCase))
                        ?? entries.FirstOrDefault();

            if (entry is null || string.IsNullOrWhiteSpace(entry.Hostname) || string.IsNullOrWhiteSpace(entry.User))
            {
                return null;
            }

            var keyPath = entry.IdentityFile ?? Path.Combine(configDir, "nvsync.key");
            if (!File.Exists(keyPath))
            {
                return null;
            }

            return new NvidiaSyncImportResult(entry.Hostname, entry.User, entry.Port ?? 22, keyPath);
        }
        catch (Exception ex)
        {
            logger.Warn($"NVIDIA Sync import failed (non-fatal, falling back to manual entry): {ex.Message}");
            return null;
        }
    }
}
