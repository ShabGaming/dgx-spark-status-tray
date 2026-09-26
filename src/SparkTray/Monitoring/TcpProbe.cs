using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SparkTray.Monitoring;

public sealed record ProbeResult(bool TcpReachable, bool? IcmpReachable);

/// <summary>
/// TCP-connect to the target's SSH port is the primary online/offline signal - it's what
/// we need to reach anyway, and unlike ICMP it isn't routinely firewalled off on a Linux box.
/// ICMP is kept as a secondary, informational-only signal (never used to flip state) purely
/// so the tray can show "reachable but not answering ping" instead of a flat offline.
/// </summary>
public sealed class TcpProbe
{
    public async Task<ProbeResult> ProbeAsync(IPAddress address, int port, bool checkIcmp, TimeSpan timeout, CancellationToken ct)
    {
        var tcpTask = ProbeTcpAsync(address, port, timeout, ct);
        var icmpTask = checkIcmp ? ProbeIcmpAsync(address, timeout, ct) : Task.FromResult<bool?>(null);

        await Task.WhenAll(tcpTask, icmpTask).ConfigureAwait(false);
        return new ProbeResult(tcpTask.Result, icmpTask.Result);
    }

    private static async Task<bool> ProbeTcpAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await client.ConnectAsync(address, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool?> ProbeIcmpAsync(IPAddress address, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, (int)timeout.TotalMilliseconds).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return null;
        }
    }
}
