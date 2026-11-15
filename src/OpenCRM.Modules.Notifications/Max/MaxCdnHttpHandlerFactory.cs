using System.Net;
using System.Net.Sockets;

namespace OpenCRM.Modules.Notifications.Max;

/// <summary> Закрепляет CDN-соединение на публичном IP после проверки доверенного домена. </summary>
internal static class MaxCdnHttpHandlerFactory
{
    public static SocketsHttpHandler Create()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectToPublicAddressAsync
        };
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        if (!IsTrustedHost(host) || IPAddress.TryParse(host, out _))
        {
            throw new HttpRequestException("MAX CDN returned an invalid attachment host.");
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = addresses.FirstOrDefault(IsPublicAddress)
            ?? throw new HttpRequestException("MAX CDN host did not resolve to a public IP address.");
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsTrustedHost(string host) =>
        host.EndsWith(".okcdn.ru", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".oneme.ru", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast
            || address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && bytes[0] < 224;
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            && (bytes[0] & 0xE0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
    }
}
