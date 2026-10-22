// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled by the product owner)
// Development channel: alpha.2
// Purpose: Read-only, targeted discovery of Zidoo devices on directly connected IPv4 subnets.
// Remarks:
// - Discovery never broadcasts, pings, scans arbitrary ports, or changes network configuration.
// - Only TCP/HTTP port 9529 and Zidoo's documented read-only getModel endpoint are queried.
// - Concurrency is deliberately limited to reduce network impact.
// ============================================================================
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace MediaMonkeyZidooSync.Services;

/// <summary>One Zidoo device positively identified by the documented device-information endpoint.</summary>
public sealed record ZidooDiscoveryResult(
    string IPAddress, string Model, string DeviceName, string MacAddress,
    string WifiMacAddress, string Firmware, bool IsValidatedModel);

/// <summary>Performs conservative, read-only Zidoo discovery on active directly connected IPv4 networks.</summary>
public sealed class ZidooDiscoveryService
{
    public const int DocumentedApiPort = 9529;
    private const int MaxAddressesPerSubnet = 1024;
    private const int MaxConcurrency = 8;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Searches active local IPv4 subnets for devices that answer Zidoo's documented
    /// device-information request on the documented fixed API port.
    /// </summary>
    public async Task<IReadOnlyList<ZidooDiscoveryResult>> DiscoverAsync(
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var targets = GetCandidateAddresses(progress).Distinct().ToArray();
        if (targets.Length == 0) return Array.Empty<ZidooDiscoveryResult>();

        progress?.Report($"Checking {targets.Length} local addresses using only Zidoo API port {DocumentedApiPort}...");
        var results = new ConcurrentBag<ZidooDiscoveryResult>();
        using var gate = new SemaphoreSlim(MaxConcurrency);

        var tasks = targets.Select(async address =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await ProbeAsync(address, cancellationToken);
                if (result is not null)
                {
                    results.Add(result);
                    progress?.Report($"Found Zidoo {result.Model} at {result.IPAddress}.");
                }
            }
            finally { gate.Release(); }
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(r => IPAddress.Parse(r.IPAddress).GetAddressBytes(), ByteArrayComparer.Instance).ToList();
    }

    /// <summary>Returns host addresses from active directly connected IPv4 subnets without sending network traffic.</summary>
    private static IEnumerable<IPAddress> GetCandidateAddresses(IProgress<string>? progress)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null)
                    continue;

                var ip = ToUInt32(ua.Address);
                var mask = ToUInt32(ua.IPv4Mask);
                var network = ip & mask;
                var broadcast = network | ~mask;
                var hostCount = broadcast > network ? (long)broadcast - network - 1 : 0;

                if (hostCount <= 0) continue;
                if (hostCount > MaxAddressesPerSubnet)
                {
                    progress?.Report($"Skipped {nic.Name}: subnet contains {hostCount:N0} host addresses; automatic discovery is intentionally limited to {MaxAddressesPerSubnet:N0} addresses per subnet.");
                    continue;
                }

                progress?.Report($"Using active adapter {nic.Name}; discovery remains inside its directly connected subnet.");
                for (uint value = network + 1; value < broadcast; value++)
                    if (value != ip) yield return FromUInt32(value);
            }
        }
    }

    /// <summary>Queries only the documented Zidoo device-information endpoint at one candidate address.</summary>
    private static async Task<ZidooDiscoveryResult?> ProbeAsync(IPAddress address, CancellationToken outerToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var client = new HttpClient { Timeout = RequestTimeout };
            using var response = await client.GetAsync(
                $"http://{address}:{DocumentedApiPort}/ZidooControlCenter/getModel",
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var root = json.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetInt32() != 200 ||
                !root.TryGetProperty("model", out var modelElement) ||
                !root.TryGetProperty("net_mac", out var macElement))
                return null;

            string model = modelElement.GetString() ?? "";
            string mac = macElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(mac)) return null;

            string Get(string name) => root.TryGetProperty(name, out var e) ? e.GetString() ?? "" : "";
            return new ZidooDiscoveryResult(
                address.ToString(), model, Get("deviceName"), mac.ToUpperInvariant(),
                Get("wif_mac").ToUpperInvariant(), Get("firmware"),
                string.Equals(model, "Z9X 8K", StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Derives a device's WOL broadcast only from a matching active local IPv4 subnet.</summary>
    public static string? FindBroadcastAddress(string deviceAddress)
    {
        var candidates=new HashSet<string>(StringComparer.Ordinal);
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType!=NetworkInterfaceType.Loopback && n.NetworkInterfaceType!=NetworkInterfaceType.Tunnel))
            foreach(var local in nic.GetIPProperties().UnicastAddresses)
                if(local.Address.AddressFamily==AddressFamily.InterNetwork && local.IPv4Mask is not null &&
                   BroadcastForSubnet(deviceAddress,local.Address.ToString(),local.IPv4Mask.ToString()) is { } broadcast)
                    candidates.Add(broadcast);
        return candidates.Count==1 ? candidates.Single() : null;
    }

    /// <summary>Pure subnet calculation; returns no value for invalid, unrelated, or point-to-point networks.</summary>
    public static string? BroadcastForSubnet(string deviceAddress,string localAddress,string subnetMask)
    {
        if(!IPAddress.TryParse(deviceAddress,out var device) || !IPAddress.TryParse(localAddress,out var local) || !IPAddress.TryParse(subnetMask,out var mask) ||
           new[]{device,local,mask}.Any(ip=>ip.AddressFamily!=AddressFamily.InterNetwork))return null;
        uint m=ToUInt32(mask), inverse=~m;
        if(inverse<3 || m==0 || (inverse & (inverse+1))!=0)return null;
        uint network=ToUInt32(local)&m;
        if((ToUInt32(device)&m)!=network)return null;
        return FromUInt32(network|inverse).ToString();
    }

    private static uint ToUInt32(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static IPAddress FromUInt32(uint value) =>
        new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            for (int i=0; i<Math.Min(x.Length,y.Length); i++)
            {
                int c=x[i].CompareTo(y[i]); if(c!=0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
