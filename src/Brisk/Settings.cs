using System.Text.Json;
using System.Text.Json.Serialization;

namespace Brisk;

/// <summary>User preferences, stored as JSON in %LOCALAPPDATA%\Brisk\settings.json.</summary>
sealed class Settings
{
    public static readonly string DataDir =
        Environment.GetEnvironmentVariable("BRISK_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Brisk");

    static readonly string FilePath = Path.Combine(DataDir, "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Sites (host without "www.") where the user switched ad blocking off.</summary>
    public HashSet<string> AdblockDisabledSites { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve DNS through AdGuard's ad-blocking DNS-over-HTTPS server. Needs a restart.</summary>
    public bool DnsBlocking { get; set; } = true;

    public string DnsServer { get; set; } = "https://dns.adguard-dns.com/dns-query";

    public static readonly DnsProvider[] DnsProviders =
    [
        new("AdGuard DNS", "https://dns.adguard-dns.com/dns-query"),
        new("Mullvad DNS", "https://adblock.dns.mullvad.net/dns-query"),
        new("Control D", "https://freedns.controld.com/p2"),
    ];

    [JsonIgnore]
    public string DnsName => DnsProviders.FirstOrDefault(p => p.Url == DnsServer)?.Name ?? "Ad-blocking DNS";

    /// <summary>Search URL; the query is appended URL-encoded.</summary>
    public string SearchUrl { get; set; } = "https://duckduckgo.com/?q=";

    public static readonly SearchEngine[] SearchEngines =
    [
        new("DuckDuckGo", "https://duckduckgo.com/?q="),
        new("Google", "https://www.google.com/search?q="),
        new("Bing", "https://www.bing.com/search?q="),
        new("Brave Search", "https://search.brave.com/search?q="),
        new("Startpage", "https://www.startpage.com/do/search?q="),
        new("Ecosia", "https://www.ecosia.org/search?q="),
    ];

    /// <summary>The preset matching <see cref="SearchUrl"/>, or null for a custom URL.</summary>
    [JsonIgnore]
    public SearchEngine? CurrentSearchEngine => SearchEngines.FirstOrDefault(e => e.SearchUrl == SearchUrl);

    public void UseSearchEngine(SearchEngine engine)
    {
        SearchUrl = engine.SearchUrl;
        Save();
    }

    public List<string> SessionTabs { get; set; } = new();

    public int SessionActiveTab { get; set; }

    /// <summary>How many of the session's first tabs are pinned.</summary>
    public int SessionPinned { get; set; }

    /// <summary>Background tabs are suspended after this many minutes; 0 turns it off.</summary>
    public double SleepTabsAfterMinutes { get; set; } = 10;

    /// <summary>Offer to save passwords and fill them in on the next visit (never in private windows).</summary>
    public bool SavePasswords { get; set; } = true;

    /// <summary>Hide cookie-consent banners with the cookie notice filter lists.</summary>
    public bool HideCookieBanners { get; set; } = true;

    public static string SiteKey(Uri? uri)
    {
        if (uri is null || string.IsNullOrEmpty(uri.Host)) return "";
        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    public bool IsAdblockEnabledFor(Uri? uri) => !AdblockDisabledSites.Contains(SiteKey(uri));

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json);
                if (s is not null)
                {
                    s.AdblockDisabledSites = new(s.AdblockDisabledSites, StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch (Exception) { /* fall back to defaults on a corrupt file */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException) { /* best effort */ }
    }
}

sealed record SearchEngine(string Name, string SearchUrl);

sealed record DnsProvider(string Name, string Url);
