using System.Net;
using System.Net.Http.Headers;
using Microsoft.Web.WebView2.Core;

namespace Brisk;

/// <summary>
/// Makes the shield's "off" switch cover the DNS layer too. The engine's DNS-over-HTTPS setting is
/// global, so on a site where the user turned ad blocking off, requests to hosts that the
/// ad-blocking DNS refuses are fetched here instead, through the normal system resolver, and handed
/// back to the page. Each host is checked once per run against the DoH server.
/// </summary>
static class DnsBypass
{
    static readonly Dictionary<string, Task<bool>> Verdicts = new(StringComparer.OrdinalIgnoreCase);

    static readonly HttpClient Doh = new()
    {
        Timeout = TimeSpan.FromSeconds(4),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };

    // A plain HTTP client: system DNS, no cookie jar of its own, redirects passed back to the page.
    static readonly HttpClient Fetch = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    const long MaxBody = 32 * 1024 * 1024;

    static readonly HashSet<string> SkipRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Accept-Encoding", "Connection", "Content-Length", "Transfer-Encoding", "Upgrade", "Keep-Alive",
    };

    static readonly HashSet<string> SkipResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Encoding", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive",
    };

    /// <summary>Whether this request should be checked at all.</summary>
    public static bool Applies(string url) =>
        Services.AppliedDns.On && Services.DnsHealth?.Unreachable != true &&
        (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Fetches the request ourselves if the ad-blocking DNS would block its host. Runs on the UI
    /// thread; holds the request with a deferral while it works.
    /// </summary>
    public static async void Handle(CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || uri.HostNameType != UriHostNameType.Dns) return;
        var verdict = Verdict(uri.IdnHost);
        if (verdict.IsCompletedSuccessfully && !verdict.Result) return; // the usual case: nothing to do

        var request = e.Request;
        var deferral = e.GetDeferral();
        try
        {
            if (!await verdict) return;

            using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
            if (request.Content is { } body)
            {
                var copy = new MemoryStream();
                await body.CopyToAsync(copy);
                copy.Position = 0;
                message.Content = new StreamContent(copy);
            }
            foreach (var h in request.Headers)
            {
                if (SkipRequestHeaders.Contains(h.Key)) continue;
                if (!message.Headers.TryAddWithoutValidation(h.Key, h.Value))
                    message.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            using var response = await Fetch.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
            if (response.Content.Headers.ContentLength > MaxBody) return;
            var data = await response.Content.ReadAsByteArrayAsync();

            var headers = new List<string>();
            void Add(HttpHeaders hs)
            {
                foreach (var h in hs)
                    if (!SkipResponseHeaders.Contains(h.Key))
                        foreach (var v in h.Value) headers.Add(h.Key + ": " + v);
            }
            Add(response.Headers);
            Add(response.Content.Headers);
            e.Response = env.CreateWebResourceResponse(new MemoryStream(data), (int)response.StatusCode,
                response.ReasonPhrase ?? "", string.Join("\n", headers));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or ArgumentException)
        {
            // Leave the request to the engine; it fails exactly as it would have.
        }
        finally { deferral.Complete(); }
    }

    static Task<bool> Verdict(string host)
    {
        if (!Verdicts.TryGetValue(host, out var task))
            Verdicts[host] = task = IsBlockedAsync(host);
        return task;
    }

    /// <summary>
    /// True when the DoH server answers with a blocking response (0.0.0.0, :: or "no such domain")
    /// but the system resolver finds a real address, i.e. the host exists and only the DNS blocks it.
    /// </summary>
    static async Task<bool> IsBlockedAsync(string host)
    {
        try
        {
            var server = Services.AppliedDns.Server;
            var sep = server.Contains('?') ? '&' : '?';
            using var request = new HttpRequestMessage(HttpMethod.Get, server + sep + "dns=" + Query(host));
            request.Headers.Accept.ParseAdd("application/dns-message");
            using var response = await Doh.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;
            var answer = await response.Content.ReadAsByteArrayAsync();
            if (!LooksBlocked(answer)) return false;

            var real = await Dns.GetHostAddressesAsync(host);
            return real.Any(a => !IPAddress.Any.Equals(a) && !IPAddress.IPv6Any.Equals(a) && !IPAddress.IsLoopback(a));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>An RFC 8484 GET parameter: a DNS query for the host's A record, base64url-encoded.</summary>
    static string Query(string host)
    {
        var q = new List<byte> { 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 }; // id 0, recursion desired, 1 question
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(label);
            q.Add((byte)bytes.Length);
            q.AddRange(bytes);
        }
        q.AddRange(new byte[] { 0, 0, 1, 0, 1 }); // root, type A, class IN
        return Convert.ToBase64String(q.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static bool LooksBlocked(byte[] m)
    {
        if (m.Length < 12) return false;
        int rcode = m[3] & 0x0F;
        if (rcode == 3) return true; // NXDOMAIN
        if (rcode != 0) return false;
        int qd = m[4] << 8 | m[5], an = m[6] << 8 | m[7];
        if (an == 0) return false;
        int i = 12;
        for (int k = 0; k < qd; k++) { i = SkipName(m, i) + 4; }
        for (int k = 0; k < an && i + 10 <= m.Length; k++)
        {
            i = SkipName(m, i);
            if (i + 10 > m.Length) return false;
            int type = m[i] << 8 | m[i + 1];
            int len = m[i + 8] << 8 | m[i + 9];
            i += 10;
            if (i + len > m.Length) return false;
            if (type == 1 && len == 4) return m[i] == 0 && m[i + 1] == 0 && m[i + 2] == 0 && m[i + 3] == 0;
            i += len;
        }
        return false;
    }

    static int SkipName(byte[] m, int i)
    {
        while (i < m.Length)
        {
            int len = m[i];
            if (len == 0) return i + 1;
            if ((len & 0xC0) == 0xC0) return i + 2;
            i += len + 1;
        }
        return i;
    }
}
