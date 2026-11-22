using System.Text.Json;

namespace Brisk;

sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime LastVisit { get; set; }
    public int Visits { get; set; }
}

sealed class Bookmark
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Added { get; set; }
}

/// <summary>JSON-file persistence shared by the history and bookmark stores.</summary>
static class JsonFile
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static List<T> Load<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Options) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    public static void Save<T>(string path, List<T> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(items, Options));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (IOException) { }
    }

    /// <summary>The key a page is stored under: the URL without its #fragment.</summary>
    public static string Key(string url)
    {
        var hash = url.IndexOf('#');
        return hash < 0 ? url : url[..hash];
    }

    public static bool IsRecordable(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? !url.StartsWith(StartPage.Url, StringComparison.OrdinalIgnoreCase)
            : false;
}

/// <summary>Pages visited in normal windows (never private ones). Saved in the background.</summary>
sealed class HistoryStore
{
    const int MaxEntries = 10_000;
    readonly string _path;
    readonly List<HistoryEntry> _entries;
    readonly Dictionary<string, HistoryEntry> _byUrl;
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 3000 };

    public HistoryStore(string path)
    {
        _path = path;
        _entries = JsonFile.Load<HistoryEntry>(path);
        _byUrl = _entries.GroupBy(e => e.Url).ToDictionary(g => g.Key, g => g.First());
        _saveTimer.Tick += (_, _) => Flush();
    }

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Record(string url, string title)
    {
        if (!JsonFile.IsRecordable(url)) return;
        url = JsonFile.Key(url);
        if (!_byUrl.TryGetValue(url, out var entry))
        {
            entry = new HistoryEntry { Url = url };
            _entries.Add(entry);
            _byUrl[url] = entry;
            if (_entries.Count > MaxEntries) Prune();
        }
        entry.Visits++;
        entry.LastVisit = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(title)) entry.Title = title;
        ScheduleSave();
    }

    public void UpdateTitle(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(title) || !_byUrl.TryGetValue(JsonFile.Key(url), out var entry) || entry.Title == title) return;
        entry.Title = title;
        ScheduleSave();
    }

    /// <summary>Most recently visited first.</summary>
    public IEnumerable<HistoryEntry> Recent(int count) => _entries.OrderByDescending(e => e.LastVisit).Take(count);

    /// <summary>Sites (by host) visited most often lately, for the start page.</summary>
    public IEnumerable<HistoryEntry> TopSites(int count)
    {
        var now = DateTime.UtcNow;
        return _entries
            .GroupBy(e => Uri.TryCreate(e.Url, UriKind.Absolute, out var u) ? u.Host : e.Url)
            .Select(g => (Best: g.OrderByDescending(e => e.Visits).First(),
                          Score: g.Sum(e => e.Visits / (1 + (now - e.LastVisit).TotalDays / 7))))
            .OrderByDescending(x => x.Score)
            .Take(count)
            .Select(x => x.Best);
    }

    public void Clear()
    {
        _entries.Clear();
        _byUrl.Clear();
        Flush();
    }

    void Prune()
    {
        foreach (var old in _entries.OrderBy(e => e.LastVisit).Take(_entries.Count - MaxEntries).ToList())
        {
            _entries.Remove(old);
            _byUrl.Remove(old.Url);
        }
    }

    void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Flush()
    {
        _saveTimer.Stop();
        JsonFile.Save(_path, _entries);
    }
}

sealed class BookmarkStore
{
    readonly string _path;
    readonly List<Bookmark> _items;

    public BookmarkStore(string path)
    {
        _path = path;
        _items = JsonFile.Load<Bookmark>(path);
    }

    public IReadOnlyList<Bookmark> Items => _items;

    public bool Contains(string url) => _items.Any(b => b.Url == JsonFile.Key(url));

    /// <summary>Adds the page, or removes it if it's already bookmarked. Returns true if now bookmarked.</summary>
    public bool Toggle(string url, string title)
    {
        var key = JsonFile.Key(url);
        if (_items.RemoveAll(b => b.Url == key) == 0)
            _items.Add(new Bookmark { Url = key, Title = string.IsNullOrWhiteSpace(title) ? key : title, Added = DateTime.UtcNow });
        JsonFile.Save(_path, _items);
        return Contains(key);
    }

    public void Remove(string url)
    {
        if (_items.RemoveAll(b => b.Url == JsonFile.Key(url)) > 0) JsonFile.Save(_path, _items);
    }
}
