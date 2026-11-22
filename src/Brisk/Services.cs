using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Brisk;

/// <summary>
/// State shared by every Brisk window: settings, the ad blocker, the browser engine, the DNS health
/// check, history and bookmarks. Everything runs on the UI thread.
/// </summary>
static class Services
{
    public static Settings Settings { get; } = Settings.Load();
    public static HistoryStore History { get; } = new(Path.Combine(Settings.DataDir, "history.json"));
    public static BookmarkStore Bookmarks { get; } = new(Path.Combine(Settings.DataDir, "bookmarks.json"));

    public static AdBlocker AdBlocker { get; private set; } = null!;
    public static Task<CoreWebView2Environment> Environment { get; private set; } = null!;
    public static DnsHealth? DnsHealth { get; private set; }

    /// <summary>The normal (non-private) browsing profile, once a normal tab has started; used for saved passwords.</summary>
    public static CoreWebView2Profile? Profile { get; set; }

    /// <summary>The DNS setup the running browser engine was started with (changes need a restart).</summary>
    public static (bool On, string Server) AppliedDns { get; private set; }

    /// <summary>Ads and trackers blocked since Brisk started, shown on the start page.</summary>
    public static int BlockedTotal;

    static bool _started;

    /// <summary>Starts the shared services. Call once, on the UI thread, before the first window.</summary>
    public static void Start()
    {
        if (_started) return;
        _started = true;
        // No window exists yet, so WinForms hasn't installed its context. Without it, the engine's
        // async start-up would resume on a thread-pool thread, and WebView2 must stay on the UI thread.
        if (SynchronizationContext.Current is null)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        AdBlocker = new AdBlocker(SynchronizationContext.Current!);
        AdBlocker.Start();
        Environment = CreateEnvironment();
    }

    static async Task<CoreWebView2Environment> CreateEnvironment()
    {
        var args = new List<string>
        {
            // Never expose the local IP address through WebRTC.
            "--force-webrtc-ip-handling-policy=default_public_interface_only",
        };
        var userDataDir = Path.Combine(Settings.DataDir, "Profile");
        AppliedDns = (Settings.DnsBlocking, Settings.DnsServer);
        await Task.Run(() => ConfigureSecureDns(userDataDir));
        if (Settings.DnsBlocking)
        {
            DnsHealth = new DnsHealth(Settings.DnsServer);
            DnsHealth.Start();
        }
        var port = System.Environment.GetEnvironmentVariable("BRISK_REMOTE_DEBUGGING_PORT");
        if (int.TryParse(port, out var p)) args.Add($"--remote-debugging-port={p}");

        var options = new CoreWebView2EnvironmentOptions(string.Join(' ', args))
        {
            EnableTrackingPrevention = true,
            AreBrowserExtensionsEnabled = false,
        };
        return await CoreWebView2Environment.CreateAsync(null, userDataDir, options);
    }

    /// <summary>
    /// The backup ad-blocking layer: resolve every hostname through an ad-blocking DNS-over-HTTPS
    /// server. "automatic" mode falls back to the system resolver if that server is unreachable, so
    /// an outage never stops pages loading (DnsHealth warns the user instead). WebView2 ignores the
    /// DoH feature flags, but honours the same "Local State" preferences Chromium uses, so set them
    /// before the browser process starts.
    /// </summary>
    static void ConfigureSecureDns(string userDataDir)
    {
        var path = Path.Combine(userDataDir, "EBWebView", "Local State");
        try
        {
            var state = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
                : new JsonObject();
            if (Settings.DnsBlocking)
                state["dns_over_https"] = new JsonObject { ["mode"] = "automatic", ["templates"] = Settings.DnsServer };
            else if (!state.Remove("dns_over_https"))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, state.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    static bool _scriptletsChecked;

    /// <summary>Rebuilds uBO's scriptlet resources in the background when they are missing or old.</summary>
    public static async Task RefreshScriptletsAsync(IntPtr window)
    {
        if (_scriptletsChecked || !AdBlocker.ResourcesStale) return;
        _scriptletsChecked = true;
        var env = await Environment;
        await Task.Delay(TimeSpan.FromSeconds(3)); // let the first page get going first
        var json = await ScriptletConverter.ConvertAsync(env, window);
        if (json is not null) AdBlocker.SetResources(json);
    }

    public static void Shutdown()
    {
        History.Flush();
        DnsHealth?.Dispose();
        AdBlocker?.Dispose();
    }
}

/// <summary>Keeps Brisk running while any window is open, so the private window can outlive the main one.</summary>
sealed class BriskContext : ApplicationContext
{
    readonly List<BrowserForm> _windows = new();

    public static BriskContext Current { get; private set; } = null!;

    public BriskContext(IReadOnlyList<string> startUrls, bool privateWindow)
    {
        Current = this;
        Services.Start();
        Open(new BrowserForm(startUrls, isPrivate: privateWindow));
    }

    /// <summary>The normal (non-private) window, if one is open.</summary>
    public BrowserForm? MainWindow => _windows.FirstOrDefault(w => !w.IsPrivate);

    public void Open(BrowserForm form)
    {
        _windows.Add(form);
        form.FormClosed += (_, _) =>
        {
            _windows.Remove(form);
            if (_windows.Count == 0) ExitThread();
        };
        form.Show();
    }

    public void OpenPrivateWindow(IReadOnlyList<string>? urls = null) => Open(new BrowserForm(urls ?? [], isPrivate: true));

    /// <summary>Links from another launch go to the main window, opening one if only private windows remain.</summary>
    public void OpenFromOutside(IReadOnlyList<string> urls)
    {
        if (MainWindow is { } main) main.OpenFromOutside(urls);
        else Open(new BrowserForm(urls, isPrivate: false));
    }

    protected override void ExitThreadCore()
    {
        Services.Shutdown();
        base.ExitThreadCore();
    }
}
