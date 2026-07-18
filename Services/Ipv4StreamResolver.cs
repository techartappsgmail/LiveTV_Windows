using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace IPTVPlayer.Services;

public static class Ipv4StreamResolver
{
    public static bool ShouldResolve(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string?> TryResolveRedirectAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var originalUri))
        {
            DiagnosticLog.Warning("IPv4", $"Invalid stream URL: {url}");
            return null;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(originalUri.Host, cancellationToken);
            DiagnosticLog.Info(
                "IPv4",
                $"Preflight start: {DiagnosticLog.FormatUrl(url)}; DNS={string.Join(',', addresses.Select(address => address.ToString()))}");

            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ConnectCallback = ConnectIpv4Async
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VLC/3.0.23 LibVLC/3.0.23");

            using var request = new HttpRequestMessage(HttpMethod.Get, originalUri);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLog.Warning("IPv4", $"Preflight returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                return null;
            }

            var finalUri = response.RequestMessage?.RequestUri;
            var resolvedUrl = finalUri != null &&
                !originalUri.Equals(finalUri) &&
                IPAddress.TryParse(finalUri.Host, out var address) &&
                address.AddressFamily == AddressFamily.InterNetwork
                    ? finalUri.AbsoluteUri
                    : null;

            DiagnosticLog.Info(
                "IPv4",
                resolvedUrl == null
                    ? $"Preflight completed without a literal IPv4 redirect; final={DiagnosticLog.FormatUrl(finalUri?.AbsoluteUri ?? url)}"
                    : $"Preflight selected {DiagnosticLog.FormatUrl(resolvedUrl)}");
            return resolvedUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            Debug.WriteLine($"[IPTV] IPv4 stream preflight failed: {ex.Message}");
            DiagnosticLog.Error("IPv4", ex);
            return null;
        }
    }

    private static async ValueTask<Stream> ConnectIpv4Async(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(
            context.DnsEndPoint.Host,
            AddressFamily.InterNetwork,
            cancellationToken);
        DiagnosticLog.Info(
            "IPv4",
            $"Connecting {context.DnsEndPoint.Host}:{context.DnsEndPoint.Port} via {string.Join(',', addresses.Select(address => address.ToString()))}");
        SocketException? lastError = null;

        foreach (var address in addresses)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                DiagnosticLog.Info("IPv4", $"Connected to {address}:{context.DnsEndPoint.Port}");
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                DiagnosticLog.Warning("IPv4", $"Connection to {address}:{context.DnsEndPoint.Port} failed: {ex.SocketErrorCode}");
                lastError = ex;
                socket.Dispose();
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }
}