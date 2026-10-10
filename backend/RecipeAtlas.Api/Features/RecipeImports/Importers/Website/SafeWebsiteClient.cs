using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed record DownloadedRecipePage(string Html, Uri FinalUrl);

public sealed class SafeWebsiteClient
{
    private const int MaxPageBytes = 2 * 1024 * 1024;
    private const int MaxRedirects = 4;

    private readonly HttpClient _httpClient;

    private static readonly IPNetwork[] BlockedNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4")
    ];

    public SafeWebsiteClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 2,
            MaxResponseHeadersLength = 32,
            ConnectCallback = ConnectAsync
        };
    }

    public async Task<DownloadedRecipePage> DownloadAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        var currentUrl = url;

        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            ValidateUrl(currentUrl);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                currentUrl);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects)
                {
                    throw new RecipeImportException(
                        "The website redirected too many times.");
                }

                var location = response.Headers.Location;

                if (location is null ||
                    !Uri.TryCreate(currentUrl, location.ToString(), out var next))
                {
                    throw new RecipeImportException(
                        "The website returned an invalid redirect.");
                }

                currentUrl = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new RecipeImportException(
                    $"The website returned HTTP {(int)response.StatusCode}.",
                    502);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;

            if (!string.Equals(
                    mediaType, "text/html", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    mediaType,
                    "application/xhtml+xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new RecipeImportException(
                    "The URL did not return an HTML page.");
            }

            if (response.Content.Headers.ContentLength > MaxPageBytes)
            {
                throw new RecipeImportException(
                    "The recipe page exceeds the 2 MiB download limit.");
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            using var output = new MemoryStream();
            var buffer = new byte[8192];

            while (true)
            {
                var count = await stream.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);

                if (count == 0)
                    break;

                if (output.Length + count > MaxPageBytes)
                {
                    throw new RecipeImportException(
                        "The recipe page exceeds the 2 MiB download limit.");
                }

                output.Write(buffer, 0, count);
            }

            var encoding = Encoding.UTF8;
            var charset = response.Content.Headers.ContentType?.CharSet;

            if (!string.IsNullOrWhiteSpace(charset))
            {
                try
                {
                    encoding = Encoding.GetEncoding(charset.Trim('"', '\''));
                }
                catch (ArgumentException)
                {
                    throw new RecipeImportException(
                        "The website uses an unsupported text encoding.");
                }
            }

            output.Position = 0;

            using var reader = new StreamReader(
                output,
                encoding,
                detectEncodingFromByteOrderMarks: true);

            var html = await reader.ReadToEndAsync(cancellationToken);

            return new DownloadedRecipePage(html, currentUrl);
        }

        throw new RecipeImportException("The page could not be downloaded.");
    }

    private static void ValidateUrl(Uri url)
    {
        if (!url.IsAbsoluteUri ||
            url.Scheme != Uri.UriSchemeHttps ||
            url.Port != 443 ||
            !string.IsNullOrEmpty(url.UserInfo) ||
            url.AbsoluteUri.Length > 2048 ||
            string.IsNullOrWhiteSpace(url.Host))
        {
            throw new RecipeImportException(
                "The URL or redirect is not an allowed HTTPS address.",
                400);
        }
    }

    private static bool IsRedirect(HttpStatusCode status)
    {
        return status is
            HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
    }

    public static bool IsPublicIPv4(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetwork &&
               !BlockedNetworks.Any(network => network.Contains(address));
    }

    public static async Task ValidatePublicHostAsync(Uri url, CancellationToken cancellationToken)
    {
        ValidateUrl(url);
        var addresses = await Dns.GetHostAddressesAsync(url.IdnHost, cancellationToken);
        if (!addresses.Any(IsPublicIPv4) || addresses.Any(address => !IsPublicAddress(address)))
            throw new RecipeImportException("The source host must resolve to public network addresses.",
                400, "unsafe_source");
    }

    // The external retriever can use IPv6, unlike our IPv4-pinned HTTP handler.
    public static bool IsPublicAddress(IPAddress address) => address.AddressFamily switch
    {
        AddressFamily.InterNetwork => IsPublicIPv4(address),
        AddressFamily.InterNetworkV6 => !address.IsIPv4MappedToIPv6 &&
            IPNetwork.Parse("2000::/3").Contains(address) &&
            !IPNetwork.Parse("2001:db8::/32").Contains(address) &&
            !IPNetwork.Parse("2001::/32").Contains(address),
        _ => false
    };

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.DnsEndPoint.Port != 443)
            throw new HttpRequestException("Only HTTPS port 443 is allowed.");

        var addresses = await Dns.GetHostAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);

        var allowedAddresses = addresses
            .Where(IsPublicIPv4)
            .Distinct()
            .ToArray();

        if (allowedAddresses.Length == 0)
        {
            throw new HttpRequestException(
                "The host has no allowed public IPv4 address.");
        }

        foreach (var address in allowedAddresses)
        {
            var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            try
            {
                // Connect to the checked IP, without a second DNS lookup.
                // HttpClient still validates TLS for the original hostname.
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("Could not connect to the website.");
    }
}
