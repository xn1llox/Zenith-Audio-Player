using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ZenithAudio.Core.Remote;

public interface IZenithRemoteBridge
{
    Task<ZenithRemoteState> GetStateAsync();
    Task<ZenithRemoteLibraryResponse> QueryLibraryAsync(string? query, string? filter, int offset, int limit);
    Task<ZenithRemoteActionResult> ExecuteActionAsync(string action, JsonElement payload);
    ZenithRemoteCoverData? GetCurrentCover();
}

/// <summary>
/// Lightweight embedded HTTP/1.1 LAN remote server built directly on TcpListener
/// so Windows never requires Administrator privileges or URLACL reservations.
/// </summary>
public sealed class ZenithRemoteServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly byte[] FallbackLogoSvgBytes = Encoding.UTF8.GetBytes("""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
          <defs>
            <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stop-color="#071929"/>
              <stop offset="100%" stop-color="#020810"/>
            </linearGradient>
            <linearGradient id="cy" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stop-color="#76D8FF"/>
              <stop offset="100%" stop-color="#2CA4E8"/>
            </linearGradient>
          </defs>
          <rect width="256" height="256" rx="44" fill="url(#bg)"/>
          <rect x="8" y="8" width="240" height="240" rx="38" fill="none" stroke="#184C72" stroke-width="4"/>
          <path d="M68 78h120l-82 100h82" fill="none" stroke="url(#cy)" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
          <circle cx="188" cy="78" r="10" fill="#8BE0FF"/>
          <circle cx="68" cy="178" r="10" fill="#38B6FF"/>
        </svg>
        """);

    private static readonly byte[] ManifestJsonBytes = Encoding.UTF8.GetBytes("""
        {
          "name": "Zenith Audio Remote",
          "short_name": "Zenith Remote",
          "start_url": "/",
          "display": "standalone",
          "background_color": "#000000",
          "theme_color": "#040910",
          "orientation": "portrait-primary",
          "icons": [
            {
              "src": "/logo.png",
              "sizes": "512x512",
              "type": "image/png",
              "purpose": "any maskable"
            },
            {
              "src": "/favicon.ico",
              "sizes": "64x64 48x48 32x32 16x16",
              "type": "image/x-icon"
            }
          ]
        }
        """);

    private readonly IZenithRemoteBridge _bridge;
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentClients = new(StringComparer.OrdinalIgnoreCase);

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private string _pin = string.Empty;

    public ZenithRemoteServer(IZenithRemoteBridge bridge)
    {
        _bridge = bridge;
    }

    public bool IsRunning { get; private set; }
    public int ActivePort { get; private set; }
    public bool ActiveAllowLan { get; private set; }
    public string PrimaryUrl { get; private set; } = string.Empty;
    public IReadOnlyList<string> AccessibleUrls { get; private set; } = [];

    public event Action? ClientsChanged;

    public int GetActiveClientCount(TimeSpan? window = null)
    {
        var cutoff = DateTimeOffset.UtcNow - (window ?? TimeSpan.FromMinutes(2));
        var count = 0;
        foreach (var pair in _recentClients)
        {
            if (pair.Value >= cutoff)
            {
                count++;
            }
            else
            {
                _recentClients.TryRemove(pair.Key, out _);
            }
        }

        return count;
    }

    public IReadOnlyList<string> GetActiveClientIps(TimeSpan? window = null)
    {
        var cutoff = DateTimeOffset.UtcNow - (window ?? TimeSpan.FromMinutes(2));
        return _recentClients
            .Where(pair => pair.Value >= cutoff)
            .OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToList();
    }

    public void Start(ZenithRemoteSettings settings)
    {
        settings.Normalize();
        lock (_lifecycleLock)
        {
            if (IsRunning)
            {
                StopInternal();
            }

            var bindAddress = settings.AllowLan ? IPAddress.Any : IPAddress.Loopback;
            var listener = new TcpListener(bindAddress, settings.Port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();

            _listener = listener;
            _pin = settings.Pin;
            ActivePort = settings.Port;
            ActiveAllowLan = settings.AllowLan;
            AccessibleUrls = DiscoverAccessibleUrls(settings.Port, settings.AllowLan);
            PrimaryUrl = AccessibleUrls.FirstOrDefault() ?? $"http://localhost:{settings.Port}";
            _recentClients.Clear();

            _cts = new CancellationTokenSource();
            IsRunning = true;
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        IsRunning = false;
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Ignore shutdown races.
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Ignore shutdown races.
        }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _acceptLoopTask = null;
        _recentClients.Clear();
    }

    public static IReadOnlyList<string> DiscoverAccessibleUrls(int port, bool allowLan)
    {
        var urls = new List<string>();
        if (allowLan)
        {
            var lanIps = new List<(int Priority, string Ip)>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    var desc = $"{ni.Name} {ni.Description}".ToLowerInvariant();
                    var isVirtual = desc.Contains("vethernet")
                        || desc.Contains("wsl")
                        || desc.Contains("hyper-v")
                        || desc.Contains("vmware")
                        || desc.Contains("virtualbox")
                        || desc.Contains("loopback")
                        || desc.Contains("bluetooth");

                    var ipProps = ni.GetIPProperties();
                    var hasGateway = ipProps.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(g.Address) &&
                        !g.Address.Equals(IPAddress.Any));

                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        var ip = unicast.Address.ToString();
                        if (ip.StartsWith("127.", StringComparison.Ordinal) ||
                            ip.StartsWith("169.254.", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var priority = 50;
                        if (!isVirtual && hasGateway)
                        {
                            priority = 10;
                        }
                        else if (!isVirtual)
                        {
                            priority = 20;
                        }
                        else if (hasGateway)
                        {
                            priority = 40;
                        }

                        if (ip.StartsWith("192.168.", StringComparison.Ordinal))
                        {
                            priority -= 5;
                        }
                        else if (ip.StartsWith("10.", StringComparison.Ordinal))
                        {
                            priority -= 3;
                        }

                        lanIps.Add((priority, ip));
                    }
                }
            }
            catch
            {
                // Ignore interface enumeration failures.
            }

            foreach (var ip in lanIps.OrderBy(x => x.Priority).Select(x => x.Ip).Distinct(StringComparer.Ordinal))
            {
                urls.Add($"http://{ip}:{port}");
            }
        }

        urls.Add($"http://localhost:{port}");
        return urls;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                break;
            }
            catch (SocketException)
            {
                client?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch
            {
                client?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.ReceiveTimeout = 8000;
            client.SendTimeout = 8000;

            var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "desconocido";
            using var stream = client.GetStream();

            var request = await ReadHttpRequestAsync(stream, cancellationToken);
            if (request is null)
            {
                return;
            }

            var isNewOrUpdated = !_recentClients.ContainsKey(remoteIp);
            _recentClients[remoteIp] = DateTimeOffset.UtcNow;
            if (isNewOrUpdated)
            {
                ClientsChanged?.Invoke();
            }

            try
            {
                await RouteRequestAsync(stream, request, cancellationToken);
            }
            catch (Exception ex)
            {
                var errJson = JsonSerializer.Serialize(new ZenithRemoteActionResult(false, ex.Message), JsonOptions);
                await WriteResponseAsync(stream, 500, "Internal Server Error", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(errJson), cancellationToken);
            }
        }
    }

    private async Task RouteRequestAsync(NetworkStream stream, ParsedHttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, 204, "No Content", "text/plain; charset=utf-8", [], cancellationToken);
            return;
        }

        if (request.Path == "/" || request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            var htmlBytes = LoadRemoteIndexHtmlBytes();
            await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", htmlBytes, cancellationToken, cacheControl: "no-cache");
            return;
        }

        if (request.Path.Equals("/logo.png", StringComparison.OrdinalIgnoreCase))
        {
            var logoBytes = TryLoadBrandAssetBytes("Logo.png");
            if (logoBytes is not null)
            {
                await WriteResponseAsync(stream, 200, "OK", "image/png", logoBytes, cancellationToken, cacheControl: "public, max-age=3600");
                return;
            }

            await WriteResponseAsync(stream, 200, "OK", "image/svg+xml", FallbackLogoSvgBytes, cancellationToken, cacheControl: "public, max-age=3600");
            return;
        }

        if (request.Path.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, 200, "OK", "application/manifest+json; charset=utf-8", ManifestJsonBytes, cancellationToken, cacheControl: "public, max-age=3600");
            return;
        }

        if (request.Path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
            request.Path.Equals("/icono.ico", StringComparison.OrdinalIgnoreCase))
        {
            var iconBytes = TryLoadBrandAssetBytes("Icono.ico");
            if (iconBytes is not null)
            {
                await WriteResponseAsync(stream, 200, "OK", "image/x-icon", iconBytes, cancellationToken, cacheControl: "public, max-age=3600");
                return;
            }

            await WriteResponseAsync(stream, 200, "OK", "image/svg+xml", FallbackLogoSvgBytes, cancellationToken, cacheControl: "public, max-age=3600");
            return;
        }

        if (request.Path.Equals("/icon.svg", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, 200, "OK", "image/svg+xml", FallbackLogoSvgBytes, cancellationToken, cacheControl: "public, max-age=3600");
            return;
        }

        if (request.Path.Equals("/api/auth-check", StringComparison.OrdinalIgnoreCase))
        {
            var authorized = IsAuthorized(request);
            var payload = JsonSerializer.Serialize(new { authorized, requiresPin = !string.IsNullOrEmpty(_pin) }, JsonOptions);
            await WriteResponseAsync(stream, authorized ? 200 : 401, authorized ? "OK" : "Unauthorized", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(payload), cancellationToken);
            return;
        }

        if (!IsAuthorized(request))
        {
            var payload = JsonSerializer.Serialize(new { ok = false, requiresPin = true, message = "PIN requerido para controlar Zenith Audio." }, JsonOptions);
            await WriteResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(payload), cancellationToken);
            return;
        }

        if (request.Path.Equals("/api/state", StringComparison.OrdinalIgnoreCase) && request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var state = await _bridge.GetStateAsync();
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), cancellationToken);
            return;
        }

        if (request.Path.Equals("/api/cover", StringComparison.OrdinalIgnoreCase) && request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var cover = _bridge.GetCurrentCover();
            if (cover is not null && cover.Bytes.Length > 0)
            {
                await WriteResponseAsync(stream, 200, "OK", cover.ContentType, cover.Bytes, cancellationToken, cacheControl: "no-store");
            }
            else
            {
                var brandLogo = TryLoadBrandAssetBytes("Logo.png");
                if (brandLogo is not null)
                {
                    await WriteResponseAsync(stream, 200, "OK", "image/png", brandLogo, cancellationToken, cacheControl: "no-store");
                }
                else if (TryLoadBrandAssetBytes("Icono.ico") is { } brandIcon)
                {
                    await WriteResponseAsync(stream, 200, "OK", "image/x-icon", brandIcon, cancellationToken, cacheControl: "no-store");
                }
                else
                {
                    await WriteResponseAsync(stream, 200, "OK", "image/svg+xml", FallbackLogoSvgBytes, cancellationToken, cacheControl: "no-store");
                }
            }

            return;
        }

        if (request.Path.Equals("/api/library", StringComparison.OrdinalIgnoreCase) && request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            request.Query.TryGetValue("q", out var q);
            request.Query.TryGetValue("filter", out var filter);
            var offset = request.Query.TryGetValue("offset", out var offRaw) && int.TryParse(offRaw, out var offVal) ? Math.Max(0, offVal) : 0;
            var limit = request.Query.TryGetValue("limit", out var limRaw) && int.TryParse(limRaw, out var limVal) ? Math.Clamp(limVal, 1, 250) : 100;

            var library = await _bridge.QueryLibraryAsync(q, filter, offset, limit);
            var json = JsonSerializer.Serialize(library, JsonOptions);
            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), cancellationToken);
            return;
        }

        if (request.Path.Equals("/api/action", StringComparison.OrdinalIgnoreCase) && request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Body))
            {
                var emptyErr = JsonSerializer.Serialize(new ZenithRemoteActionResult(false, "Cuerpo JSON vacío."), JsonOptions);
                await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(emptyErr), cancellationToken);
                return;
            }

            using var doc = JsonDocument.Parse(request.Body);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var actionProp) && actionProp.ValueKind == JsonValueKind.String
                ? actionProp.GetString() ?? string.Empty
                : string.Empty;

            var result = await _bridge.ExecuteActionAsync(action, root);
            var json = JsonSerializer.Serialize(result, JsonOptions);
            await WriteResponseAsync(stream, result.Ok ? 200 : 400, result.Ok ? "OK" : "Bad Request", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), cancellationToken);
            return;
        }

        var notFound = JsonSerializer.Serialize(new ZenithRemoteActionResult(false, "Ruta no encontrada."), JsonOptions);
        await WriteResponseAsync(stream, 404, "Not Found", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(notFound), cancellationToken);
    }

    private static byte[]? TryLoadBrandAssetBytes(string fileName)
    {
        try
        {
            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
                Path.Combine(AppContext.BaseDirectory, "Assets", "Remote", fileName),
                Path.Combine(AppContext.BaseDirectory, "Imagenes", fileName)
            };

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && dir is not null; i++)
            {
                candidates.Add(Path.Combine(dir.FullName, "Imagenes", fileName));
                candidates.Add(Path.Combine(dir.FullName, "Assets", fileName));
                dir = dir.Parent;
            }

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }
            }
        }
        catch
        {
            // Ignore unreadable disk paths and fall back cleanly.
        }

        return null;
    }

    private static byte[] LoadRemoteIndexHtmlBytes()
    {
        try
        {
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Remote", "index.html");
            if (File.Exists(htmlPath))
            {
                return File.ReadAllBytes(htmlPath);
            }
        }
        catch
        {
            // Fall back to embedded HTML if disk asset cannot be read.
        }

        return Encoding.UTF8.GetBytes(ZenithRemoteWebApp.Html);
    }

    private bool IsAuthorized(ParsedHttpRequest request)
    {
        if (string.IsNullOrEmpty(_pin))
        {
            return true;
        }

        if (request.Headers.TryGetValue("x-zenith-pin", out var headerPin) &&
            string.Equals(headerPin.Trim(), _pin, StringComparison.Ordinal))
        {
            return true;
        }

        if (request.Query.TryGetValue("pin", out var queryPin) &&
            string.Equals(queryPin.Trim(), _pin, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static async Task<ParsedHttpRequest?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        const int maxHeaderBytes = 32 * 1024;
        const int maxBodyBytes = 512 * 1024;

        var buffer = new byte[4096];
        using var headerStream = new MemoryStream();
        var headerEndIndex = -1;

        while (headerStream.Length < maxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                return null;
            }

            headerStream.Write(buffer, 0, read);
            var data = headerStream.GetBuffer();
            var len = (int)headerStream.Length;

            for (var i = Math.Max(0, len - read - 3); i <= len - 4; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                {
                    headerEndIndex = i + 4;
                    break;
                }
            }

            if (headerEndIndex >= 0)
            {
                break;
            }
        }

        if (headerEndIndex < 0)
        {
            return null;
        }

        var allBytes = headerStream.ToArray();
        var headerText = Encoding.ASCII.GetString(allBytes, 0, headerEndIndex - 4);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return null;
        }

        var requestLineParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLineParts.Length < 2)
        {
            return null;
        }

        var method = requestLineParts[0].Trim();
        var rawTarget = requestLineParts[1].Trim();

        var path = rawTarget;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIndex = rawTarget.IndexOf('?');
        if (qIndex >= 0)
        {
            path = rawTarget[..qIndex];
            var queryString = rawTarget[(qIndex + 1)..];
            foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq >= 0)
                {
                    var key = Uri.UnescapeDataString(pair[..eq].Replace('+', ' '));
                    var val = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                    query[key] = val;
                }
                else
                {
                    query[Uri.UnescapeDataString(pair.Replace('+', ' '))] = string.Empty;
                }
            }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
            {
                var key = lines[i][..colon].Trim();
                var value = lines[i][(colon + 1)..].Trim();
                headers[key] = value;
            }
        }

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var clRaw) && int.TryParse(clRaw, out var parsedCl))
        {
            contentLength = Math.Clamp(parsedCl, 0, maxBodyBytes);
        }

        var body = string.Empty;
        if (contentLength > 0)
        {
            var bodyBytes = new byte[contentLength];
            var alreadyHave = Math.Min(contentLength, allBytes.Length - headerEndIndex);
            if (alreadyHave > 0)
            {
                Buffer.BlockCopy(allBytes, headerEndIndex, bodyBytes, 0, alreadyHave);
            }

            var totalRead = alreadyHave;
            while (totalRead < contentLength)
            {
                var n = await stream.ReadAsync(bodyBytes.AsMemory(totalRead, contentLength - totalRead), cancellationToken);
                if (n <= 0)
                {
                    break;
                }

                totalRead += n;
            }

            body = Encoding.UTF8.GetString(bodyBytes, 0, totalRead);
        }

        return new ParsedHttpRequest(method, path, query, headers, body);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string reasonPhrase,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken,
        string cacheControl = "no-store")
    {
        var header = new StringBuilder()
            .Append($"HTTP/1.1 {statusCode} {reasonPhrase}\r\n")
            .Append($"Content-Type: {contentType}\r\n")
            .Append($"Content-Length: {body.Length}\r\n")
            .Append($"Cache-Control: {cacheControl}\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Headers: Content-Type, X-Zenith-Pin\r\n")
            .Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, cancellationToken);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed record ParsedHttpRequest(
        string Method,
        string Path,
        Dictionary<string, string> Query,
        Dictionary<string, string> Headers,
        string Body);
}
