// 1.1-alpha.40 — native network readiness and Wake-on-LAN transport.
// Not connected to production synchronization. No power-off operation is issued.
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Performs opt-in device connectivity checks and sends a standard Wake-on-LAN packet.</summary>
public sealed class NativeZidooConnectivityPowerService
{
    private readonly HttpClient _client;
    public NativeZidooConnectivityPowerService(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Tests HTTP transport only; does not establish Zidoo model identity.</summary>
    public async Task<bool> IsHttpReachableAsync(string host, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            throw new ArgumentException("Valid host and TCP port required.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder("http", host, port).Uri);
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return true; // An HTTP response proves transport only, even if status is an error.
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    /// <summary>Explicit Wake-on-LAN only; callers must first establish initial power state.</summary>
    public async Task SendWakeOnLanAsync(string mac, string broadcast, int udpPort, CancellationToken ct = default)
    {
        if (udpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(udpPort));
        byte[] address = ParseMac(mac);
        if (!IPAddress.TryParse(broadcast, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("IPv4 broadcast address required.", nameof(broadcast));
        byte[] packet = new byte[102];
        Array.Fill(packet, (byte)0xff, 0, 6);
        for (int i = 0; i < 16; i++) Buffer.BlockCopy(address, 0, packet, 6 + i * 6, 6);
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        await udp.SendAsync(packet, new IPEndPoint(ip, udpPort), ct);
    }

    private static byte[] ParseMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) throw new ArgumentException("MAC required.", nameof(mac));
        string clean = mac.Replace(":", "").Replace("-", "");
        if (clean.Length != 12) throw new ArgumentException("Expected six-byte MAC.", nameof(mac));
        try { return Convert.FromHexString(clean); }
        catch (FormatException ex) { throw new ArgumentException("Invalid MAC.", nameof(mac), ex); }
    }
}
