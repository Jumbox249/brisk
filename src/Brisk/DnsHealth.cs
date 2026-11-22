namespace Brisk;

/// <summary>
/// Periodically checks that the ad-blocking DNS-over-HTTPS server answers. The browser engine
/// silently falls back to the normal system DNS when it doesn't, so this is how the user finds out
/// that the DNS layer is temporarily not blocking anything.
/// </summary>
sealed class DnsHealth : IDisposable
{
    // A standard DNS query for "example.com", type A, as an RFC 8484 base64url "dns" parameter.
    const string ExampleQuery = "AAABAAABAAAAAAAAB2V4YW1wbGUDY29tAAABAAE";

    // Some DoH servers (Mullvad's, for one) only speak HTTP/2, as browsers do.
    readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(6),
        DefaultRequestVersion = System.Net.HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 5 * 60 * 1000 };
    readonly string _server;
    bool _checking;

    /// <summary>True once a check has failed, until a later check succeeds.</summary>
    public bool Unreachable { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Unreachable"/> changes.</summary>
    public event Action? Changed;

    public DnsHealth(string server)
    {
        _server = server;
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/dns-message");
        _timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        Check();
        _timer.Start();
    }

    async void Check()
    {
        if (_checking) return;
        _checking = true;
        bool ok;
        try
        {
            var sep = _server.Contains('?') ? '&' : '?';
            using var response = await _http.GetAsync(_server + sep + "dns=" + ExampleQuery);
            ok = response.IsSuccessStatusCode &&
                 response.Content.Headers.ContentType?.MediaType == "application/dns-message";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ok = false;
        }
        _checking = false;
        if (Unreachable == !ok) return;
        Unreachable = !ok;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _http.Dispose();
    }
}
