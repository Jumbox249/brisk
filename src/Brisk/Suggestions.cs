namespace Brisk;

enum SuggestionKind { Search, Bookmark, History }

sealed record Suggestion(SuggestionKind Kind, string Title, string Url);

static class SuggestionSource
{
    /// <summary>
    /// Ranks bookmarks and history against what was typed. A strong address match (the site name
    /// starts with the text) goes first; otherwise the "Search for…" row leads.
    /// </summary>
    public static List<Suggestion> For(string text, string searchUrl, string engineName, bool includeHistory)
    {
        var q = text.Trim();
        var results = new List<Suggestion>();
        if (q.Length == 0) return results;
        var ql = q.ToLowerInvariant();

        var scored = new Dictionary<string, (Suggestion S, double Score)>();
        void Consider(SuggestionKind kind, string url, string title, double boost, double visits, DateTime last)
        {
            var bare = Bare(url);
            double score = 0;
            if (bare.StartsWith(ql, StringComparison.Ordinal)) score += 100;
            else if (bare.Contains(ql, StringComparison.Ordinal)) score += 30;
            if (title.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 25;
            if (score == 0) return;
            score += boost + Math.Log(1 + visits) * 8 - Math.Min(30, (DateTime.UtcNow - last).TotalDays);
            if (!scored.TryGetValue(url, out var existing) || existing.Score < score)
                scored[url] = (new Suggestion(kind, string.IsNullOrWhiteSpace(title) ? bare : title, url), score);
        }

        foreach (var b in Services.Bookmarks.Items) Consider(SuggestionKind.Bookmark, b.Url, b.Title, 40, 0, DateTime.UtcNow);
        if (includeHistory)
            foreach (var h in Services.History.Entries) Consider(SuggestionKind.History, h.Url, h.Title, 0, h.Visits, h.LastVisit);

        var top = scored.Values.OrderByDescending(x => x.Score).Take(6).ToList();
        var search = new Suggestion(SuggestionKind.Search, $"Search {engineName} for “{q}”", searchUrl + Uri.EscapeDataString(q));
        bool strongMatch = top.Count > 0 && top[0].Score >= 100;
        if (strongMatch) results.Add(top[0].S);
        results.Add(search);
        results.AddRange(top.Skip(strongMatch ? 1 : 0).Select(x => x.S));
        return results;
    }

    /// <summary>"https://www.example.com/a" → "example.com/a", for matching what people type.</summary>
    public static string Bare(string url) => Display(url).ToLowerInvariant();

    /// <summary>"https://www.example.com/A" → "example.com/A", as shown under a suggestion.</summary>
    public static string Display(string url)
    {
        var s = url;
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return s.TrimEnd('/');
    }
}

/// <summary>
/// The list under the address bar while typing. It never takes focus (the text box keeps it), so
/// typing, arrow keys and Enter all keep working in the field.
/// </summary>
sealed class SuggestionPopup : Form
{
    readonly Font _title = Theme.Ui(9.5f);
    readonly Font _url = Theme.Ui(8.5f);
    List<Suggestion> _items = [];
    int _selected = -1, _hover = -1;

    public event Action<Suggestion>? Chosen;

    public SuggestionPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021 /* WM_MOUSEACTIVATE */) { m.Result = 3 /* MA_NOACTIVATE */; return; }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, Theme.Current.Dark, roundSmall: true);
    }

    int RowHeight => LogicalToDeviceUnits(40);
    int Pad => LogicalToDeviceUnits(6);

    public Suggestion? Selected => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    /// <summary>Shows the list under the given screen rectangle (the address field), or hides it if empty.</summary>
    public void ShowFor(Form owner, Rectangle field, List<Suggestion> items)
    {
        _items = items;
        _selected = -1;
        _hover = -1;
        if (items.Count == 0) { Hide(); return; }
        Bounds = new Rectangle(field.X, field.Bottom + LogicalToDeviceUnits(6), field.Width, items.Count * RowHeight + Pad * 2);
        if (!Visible)
        {
            Owner = owner;
            Show(owner);
        }
        Invalidate();
    }

    /// <summary>Moves the highlight with the arrow keys. Returns false when there is nothing to move through.</summary>
    public bool MoveSelection(int delta)
    {
        if (!Visible || _items.Count == 0) return false;
        _selected = _selected < 0 ? (delta > 0 ? 0 : _items.Count - 1) : Math.Clamp(_selected + delta, -1, _items.Count - 1);
        Invalidate();
        return true;
    }

    int RowAt(Point p) => p.Y < Pad ? -1 : Math.Min(_items.Count - 1, (p.Y - Pad) / RowHeight);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = RowAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var i = RowAt(e.Location);
        if (e.Button == MouseButtons.Left && i >= 0) Chosen?.Invoke(_items[i]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(t.MenuBack);
        Shapes.Smooth(g);
        int pad = Pad, row = RowHeight;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var r = new Rectangle(pad, pad + i * row, Width - pad * 2, row);
            bool sel = i == _selected, hov = i == _hover;
            if (sel || hov)
            {
                using var b = new SolidBrush(sel ? t.Accent : t.Hover);
                using var path = Shapes.Rounded(r, LogicalToDeviceUnits(6));
                g.FillPath(b, path);
            }
            var fg = sel ? Color.White : t.Text;
            var sub = sel ? Color.FromArgb(220, 255, 255, 255) : t.Secondary;

            float s = LogicalToDeviceUnits(15);
            var icon = new RectangleF(r.X + LogicalToDeviceUnits(10), r.Y + (row - s) / 2, s, s);
            float w = 1.5f * DeviceDpi / 96f;
            switch (item.Kind)
            {
                case SuggestionKind.Search: Glyphs.Search(g, icon, sub, w); break;
                case SuggestionKind.Bookmark: Glyphs.Star(g, icon, sub, w, false); break;
                default: Glyphs.Clock(g, icon, sub, w); break;
            }

            int textX = (int)icon.Right + LogicalToDeviceUnits(12);
            var textRect = new Rectangle(textX, r.Y, r.Right - textX - LogicalToDeviceUnits(10), row);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (item.Kind == SuggestionKind.Search)
            {
                TextRenderer.DrawText(g, item.Title, _title, textRect, fg, flags | TextFormatFlags.VerticalCenter);
                continue;
            }
            int titleH = TextRenderer.MeasureText(g, "Ag", _title, Size.Empty, flags).Height;
            int urlH = TextRenderer.MeasureText(g, "Ag", _url, Size.Empty, flags).Height;
            int top = r.Y + (row - titleH - urlH - LogicalToDeviceUnits(1)) / 2;
            TextRenderer.DrawText(g, item.Title, _title, new Rectangle(textRect.X, top, textRect.Width, titleH), fg, flags);
            TextRenderer.DrawText(g, SuggestionSource.Display(item.Url), _url,
                new Rectangle(textRect.X, top + titleH + LogicalToDeviceUnits(1), textRect.Width, urlH), sub, flags);
        }
    }
}
