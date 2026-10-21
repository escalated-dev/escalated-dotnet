using System.Net;
using System.Net.Sockets;

namespace Escalated.Services;

/// <summary>
/// Keeps server-side requests to admin- or sender-supplied URLs (webhooks, inbound
/// attachment downloads) off loopback, private, link-local and cloud-metadata addresses.
///
/// <para>Checking the URL's host string is not enough: a public-looking name can
/// resolve to <c>127.0.0.1</c> or <c>169.254.169.254</c>, and a public endpoint can
/// redirect to one. <see cref="CreateHandler"/> therefore resolves the name itself at
/// connect time, refuses the connection if <em>any</em> resolved address is blocked, and
/// connects to the address it vetted, so a second lookup cannot swap it. It never
/// follows redirects and ignores the system proxy (which would make the connect step
/// see only the proxy's address).</para>
/// </summary>
public static class OutboundUrlGuard
{
    /// <summary>The resolver the handler uses; replaceable in tests.</summary>
    public delegate Task<IPAddress[]> Resolver(string host, CancellationToken ct);

    private static readonly Resolver SystemResolver = (host, ct) => Dns.GetHostAddressesAsync(host, ct);

    /// <summary>
    /// An HTTP handler that only connects to public addresses and does not follow redirects.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(Resolver? resolve = null)
    {
        resolve ??= SystemResolver;
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                var addresses = IPAddress.TryParse(host, out var literal)
                    ? new[] { literal }
                    : await resolve(host, ct);

                if (addresses.Length == 0)
                    throw new HttpRequestException($"Could not resolve {host}.");
                if (IsBlockedHostName(host) || addresses.Any(IsBlockedAddress))
                    throw new HttpRequestException($"Refusing to connect to {host}: it resolves to a local, private or reserved address.");

                Exception? last = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        last = ex;
                        if (ex is OperationCanceledException) throw;
                    }
                }
                throw new HttpRequestException($"Could not connect to {host}.", last);
            },
        };
    }

    /// <summary>
    /// The cheap, synchronous check for save time: an absolute HTTP(S) URL whose host is
    /// not a local name or a blocked literal address. A hostname is only fully vetted
    /// when <see cref="CreateHandler"/> resolves it at send time.
    /// </summary>
    public static bool TryValidateUri(string url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return false;
        if (IsBlockedHostName(parsed.Host)) return false;
        if (IPAddress.TryParse(parsed.Host.Trim('[', ']'), out var address) && IsBlockedAddress(address)) return false;
        uri = parsed;
        return true;
    }

    public static bool IsBlockedHostName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("localhost.localdomain", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loopback, unspecified, private (RFC 1918, ULA), carrier-grade NAT, link-local
    /// (including the 169.254.169.254 metadata endpoint), benchmarking, multicast and
    /// reserved ranges, plus IPv6 forms that embed an IPv4 address.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                   // "this" network
                || b[0] == 10                                  // private
                || b[0] == 100 && b[1] >= 64 && b[1] <= 127    // carrier-grade NAT
                || b[0] == 127                                 // loopback
                || b[0] == 169 && b[1] == 254                  // link-local, cloud metadata
                || b[0] == 172 && b[1] >= 16 && b[1] <= 31     // private
                || b[0] == 192 && b[1] == 0 && b[2] == 0       // IETF protocol assignments
                || b[0] == 192 && b[1] == 0 && b[2] == 2       // TEST-NET-1
                || b[0] == 192 && b[1] == 168                  // private
                || b[0] == 198 && (b[1] == 18 || b[1] == 19)   // benchmarking
                || b[0] == 198 && b[1] == 51 && b[2] == 100    // TEST-NET-2
                || b[0] == 203 && b[1] == 0 && b[2] == 113     // TEST-NET-3
                || b[0] >= 224;                                // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any)) return true;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xfe) == 0xfc) return true;                       // fc00::/7 unique local
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b) // 64:ff9b::/96 NAT64
                return IsBlockedAddress(new IPAddress(b[12..16]));
            if (b[0] == 0x20 && b[1] == 0x02)                             // 2002::/16 6to4
                return IsBlockedAddress(new IPAddress(b[2..6]));
            if (b.Take(12).All(x => x == 0))                              // ::a.b.c.d (deprecated compatible)
                return true;
            return false;
        }

        return true;
    }
}
