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

    /// <summary>Search URL; the query is appended URL-encoded.</summary>
    public string SearchUrl { get; set; } = "https://duckduckgo.com/?q=";

    public string HomePage { get; set; } = "https://duckduckgo.com/";

    public static readonly SearchEngine[] SearchEngines =
    [
        new("DuckDuckGo", "https://duckduckgo.com/?q=", "https://duckduckgo.com/"),
        new("Google", "https://www.google.com/search?q=", "https://www.google.com/"),
        new("Bing", "https://www.bing.com/search?q=", "https://www.bing.com/"),
        new("Brave Search", "https://search.brave.com/search?q=", "https://search.brave.com/"),
        new("Startpage", "https://www.startpage.com/do/search?q=", "https://www.startpage.com/"),
        new("Ecosia", "https://www.ecosia.org/search?q=", "https://www.ecosia.org/"),
    ];

    /// <summary>The preset matching <see cref="SearchUrl"/>, or null for a custom URL.</summary>
    [JsonIgnore]
    public SearchEngine? CurrentSearchEngine => SearchEngines.FirstOrDefault(e => e.SearchUrl == SearchUrl);

    /// <summary>Switches search engine. The new-tab page follows it unless the user set their own.</summary>
    public void UseSearchEngine(SearchEngine engine)
    {
        if (CurrentSearchEngine is { } old && HomePage == old.HomePage) HomePage = engine.HomePage;
        SearchUrl = engine.SearchUrl;
        Save();
    }

    public List<string> SessionTabs { get; set; } = new();

    public int SessionActiveTab { get; set; }

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

sealed record SearchEngine(string Name, string SearchUrl, string HomePage);
