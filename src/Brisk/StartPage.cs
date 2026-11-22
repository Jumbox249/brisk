using System.Net;
using System.Text;

namespace Brisk;

/// <summary>
/// The page new tabs open on: Favorites (bookmarks), Frequently Visited and a privacy report.
/// It is generated on each load and served from a reserved host by the request interceptor, so it
/// never touches the network. Site tiles are lettered, which also avoids fetching favicons.
/// </summary>
static class StartPage
{
    public const string Url = "https://start.brisk/";

    public static bool Is(string url) => url.StartsWith(Url, StringComparison.OrdinalIgnoreCase);

    public static string Html(bool isPrivate)
    {
        var favorites = Services.Bookmarks.Items.Take(16).Select(b => (b.Url, b.Title)).ToList();
        var frequent = isPrivate
            ? []
            : Services.History.TopSites(12)
                .Where(h => !favorites.Any(f => SameSite(f.Url, h.Url)))
                .Take(8)
                .Select(h => (h.Url, h.Title))
                .ToList();

        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html><head><meta charset="utf-8"><title>Start Page</title>
            <meta name="color-scheme" content="light dark">
            <style>
            :root { --bg:#f5f5f7; --card:#fff; --text:#1d1d1f; --muted:#86868b; --line:#e5e5ea; }
            @media (prefers-color-scheme: dark) { :root { --bg:#1e1e20; --card:#2c2c2e; --text:#f5f5f7; --muted:#98989d; --line:#3a3a3c; } }
            * { box-sizing: border-box; }
            body { margin:0; background:var(--bg); color:var(--text);
                   font: 14px "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif; }
            main { max-width: 760px; margin: 0 auto; padding: 64px 32px; }
            h2 { font: 600 19px "Segoe UI Variable Display", "Segoe UI", sans-serif; margin: 0 0 18px; letter-spacing: -.01em; }
            section { margin-bottom: 44px; }
            .grid { display:grid; grid-template-columns: repeat(auto-fill, minmax(88px, 1fr)); gap: 22px 14px; }
            a.tile { display:flex; flex-direction:column; align-items:center; gap:8px; text-decoration:none; color:var(--text); min-width:0; }
            .icon { width:64px; height:64px; border-radius:14px; display:grid; place-items:center;
                    font: 600 26px "Segoe UI Variable Display", "Segoe UI", sans-serif; color:#fff;
                    box-shadow: 0 1px 2px rgba(0,0,0,.08), 0 2px 8px rgba(0,0,0,.06); transition: transform .12s ease; }
            a.tile:hover .icon { transform: scale(1.05); }
            .name { font-size:12px; max-width:100%; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
            .report { background:var(--card); border-radius:14px; padding:18px 20px; display:flex; align-items:center; gap:16px;
                      box-shadow: 0 1px 2px rgba(0,0,0,.05); }
            .report b { font: 600 28px "Segoe UI Variable Display", "Segoe UI", sans-serif; min-width:56px; text-align:center; }
            .report span { color:var(--muted); line-height:1.4; }
            .empty { color:var(--muted); }
            </style></head><body><main>
            """);

        Section(sb, "Favorites", favorites,
            "Bookmark pages with the star in the address bar or Ctrl+D, and they'll show up here.");
        if (!isPrivate) Section(sb, "Frequently Visited", frequent, null);

        sb.Append("<section><h2>Privacy Report</h2><div class=\"report\"><b>")
          .Append(Services.BlockedTotal.ToString("N0"))
          .Append("</b><span>")
          .Append(isPrivate
              ? "ads and trackers blocked since Brisk started. This private window keeps no history, and its cookies are deleted when you close it."
              : "ads and trackers blocked since Brisk started.")
          .Append("</span></div></section></main></body></html>");
        return sb.ToString();
    }

    static void Section(StringBuilder sb, string title, List<(string Url, string Title)> sites, string? emptyText)
    {
        if (sites.Count == 0 && emptyText is null) return;
        sb.Append("<section><h2>").Append(title).Append("</h2>");
        if (sites.Count == 0)
        {
            sb.Append("<p class=\"empty\">").Append(WebUtility.HtmlEncode(emptyText)).Append("</p></section>");
            return;
        }
        sb.Append("<div class=\"grid\">");
        foreach (var (url, name) in sites)
        {
            var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? Settings.SiteKey(u) : url;
            var label = string.IsNullOrWhiteSpace(name) ? host : name;
            var letter = host.Length > 0 ? char.ToUpperInvariant(host[0]).ToString() : "?";
            sb.Append("<a class=\"tile\" href=\"").Append(WebUtility.HtmlEncode(url)).Append("\" title=\"")
              .Append(WebUtility.HtmlEncode(label)).Append("\"><div class=\"icon\" style=\"background:")
              .Append(TileColor(host)).Append("\">").Append(WebUtility.HtmlEncode(letter))
              .Append("</div><div class=\"name\">").Append(WebUtility.HtmlEncode(ShortName(label, host)))
              .Append("</div></a>");
        }
        sb.Append("</div></section>");
    }

    static bool SameSite(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub) &&
        Settings.SiteKey(ua) == Settings.SiteKey(ub);

    /// <summary>A tidy tile label: the page title up to its first separator, or the site name.</summary>
    static string ShortName(string title, string host)
    {
        var cut = title.IndexOfAny(['|', '-', '—', '·', ':']);
        var name = (cut > 2 ? title[..cut] : title).Trim();
        return name.Length is > 0 and <= 40 ? name : host;
    }

    /// <summary>A stable, pleasant colour per site.</summary>
    static string TileColor(string host)
    {
        string[] palette = ["#0a84ff", "#30b0c7", "#34c759", "#ff9f0a", "#ff375f", "#bf5af2", "#5e5ce6", "#ac8e68", "#ff6b35", "#32ade6"];
        uint h = 2166136261;
        foreach (var c in host) h = (h ^ c) * 16777619;
        return palette[h % (uint)palette.Length];
    }
}
