using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Brisk;

/// <summary>
/// Wraps Brave's adblock-rust engine (native/adblock-ffi). The compiled engine is cached on disk
/// so startup only has to load it; filter lists are refreshed in the background every few days.
/// All lookups happen on the UI thread, which is where WebView2 raises its events.
/// </summary>
sealed partial class AdBlocker : IDisposable
{
    const string Lib = "brisk_adblock";

    [LibraryImport(Lib)] private static unsafe partial IntPtr brisk_engine_from_rules(byte* rules, nuint len);
    [LibraryImport(Lib)] private static unsafe partial IntPtr brisk_engine_deserialize(byte* data, nuint len);
    [LibraryImport(Lib)] private static unsafe partial byte* brisk_engine_serialize(IntPtr engine, out nuint len);
    [LibraryImport(Lib)] private static unsafe partial void brisk_free_bytes(byte* data, nuint len);
    [LibraryImport(Lib)] private static partial void brisk_engine_free(IntPtr engine);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int brisk_engine_check(IntPtr engine, string url, string sourceUrl, string requestType);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr brisk_engine_url_cosmetics(IntPtr engine, string url);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr brisk_engine_hidden_selectors(IntPtr engine, string classes, string ids, string exceptions);
    [LibraryImport(Lib)] private static partial void brisk_free_string(IntPtr s);

    static readonly string[] FilterLists =
    [
        "https://easylist.to/easylist/easylist.txt",
        "https://easylist.to/easylist/easyprivacy.txt",
        "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=adblockplus&mimetype=plaintext",
    ];

    static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(4);
    static readonly string CacheDir = Path.Combine(Settings.DataDir, "adblock");
    static readonly string EnginePath = Path.Combine(CacheDir, "engine.dat");

    readonly SynchronizationContext _ui;
    IntPtr _engine;
    bool _updating;

    public bool IsReady => _engine != IntPtr.Zero;

    /// <summary>Raised on the UI thread after a filter-list update, with a status message.</summary>
    public event Action<string>? Updated;

    public AdBlocker(SynchronizationContext ui) => _ui = ui;

    /// <summary>Loads the cached engine (fast) and schedules a list refresh if it is stale or missing.</summary>
    public void Start()
    {
        Task.Run(() =>
        {
            IntPtr loaded = IntPtr.Zero;
            try
            {
                if (File.Exists(EnginePath)) loaded = Deserialize(File.ReadAllBytes(EnginePath));
            }
            catch (IOException) { }

            if (loaded != IntPtr.Zero) _ui.Post(_ => Swap(loaded), null);

            var stale = loaded == IntPtr.Zero ||
                        DateTime.UtcNow - File.GetLastWriteTimeUtc(EnginePath) > RefreshAfter;
            if (stale) _ui.Post(_ => UpdateLists(), null);
        });
    }

    /// <summary>Downloads the filter lists, rebuilds the engine off the UI thread and swaps it in.</summary>
    public void UpdateLists()
    {
        if (_updating) return;
        _updating = true;
        Task.Run(async () =>
        {
            string message;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Brisk/0.1");
                var lists = await Task.WhenAll(FilterLists.Select(u => http.GetStringAsync(u)));
                var rules = Encoding.UTF8.GetBytes(string.Join('\n', lists));

                var engine = Build(rules);

                Directory.CreateDirectory(CacheDir);
                var tmp = EnginePath + ".tmp";
                File.WriteAllBytes(tmp, Serialize(engine));
                File.Move(tmp, EnginePath, overwrite: true);

                _ui.Post(_ => Swap(engine), null);
                message = "Filter lists updated.";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                message = "Couldn't update filter lists: " + ex.Message;
            }
            _ui.Post(_ => { _updating = false; Updated?.Invoke(message); }, null);
        });
    }

    void Swap(IntPtr engine)
    {
        var old = _engine;
        _engine = engine;
        if (old != IntPtr.Zero) brisk_engine_free(old);
    }

    static unsafe IntPtr Build(byte[] rules)
    {
        fixed (byte* p = rules) return brisk_engine_from_rules(p, (nuint)rules.Length);
    }

    static unsafe IntPtr Deserialize(byte[] data)
    {
        fixed (byte* p = data) return brisk_engine_deserialize(p, (nuint)data.Length);
    }

    static unsafe byte[] Serialize(IntPtr engine)
    {
        var p = brisk_engine_serialize(engine, out var len);
        try { return new ReadOnlySpan<byte>(p, checked((int)len)).ToArray(); }
        finally { brisk_free_bytes(p, len); }
    }

    /// <summary>True if a subresource request should be blocked.</summary>
    public bool ShouldBlock(string url, string pageUrl, string requestType) =>
        _engine != IntPtr.Zero && brisk_engine_check(_engine, url, pageUrl, requestType) == 1;

    /// <summary>Site-specific cosmetic rules: JSON with hide_selectors, exceptions and generichide.</summary>
    public string UrlCosmetics(string url) =>
        _engine == IntPtr.Zero ? "{}" : TakeString(brisk_engine_url_cosmetics(_engine, url));

    /// <summary>Generic hide selectors matching the given class names and ids (JSON arrays).</summary>
    public string HiddenSelectors(JsonElement classes, JsonElement ids, JsonElement exceptions) =>
        _engine == IntPtr.Zero
            ? "[]"
            : TakeString(brisk_engine_hidden_selectors(_engine, classes.GetRawText(), ids.GetRawText(), exceptions.GetRawText()));

    static string TakeString(IntPtr p)
    {
        if (p == IntPtr.Zero) return "null";
        try { return Marshal.PtrToStringUTF8(p) ?? "null"; }
        finally { brisk_free_string(p); }
    }

    public void Dispose() => Swap(IntPtr.Zero);
}
