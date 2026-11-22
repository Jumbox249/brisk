using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Brisk;

/// <summary>
/// Brisk's Settings window, laid out like a Mac app's preferences: a row of page icons at the top
/// (General, Privacy, Passwords) and right-aligned labels beside their controls.
/// </summary>
sealed class SettingsForm : Form
{
    static SettingsForm? _open;

    /// <summary>Shows the Settings window, or brings the open one forward.</summary>
    public static void ShowFor(Form owner, string page = "General")
    {
        if (_open is { IsDisposed: false })
        {
            _open.Select(page);
            if (_open.WindowState == FormWindowState.Minimized) _open.WindowState = FormWindowState.Normal;
            _open.Activate();
            return;
        }
        _open = new SettingsForm(owner.Icon);
        _open.Select(page);
        _open.Show(owner);
    }

    static Settings S => Services.Settings;
    static IEnumerable<BrowserForm> Windows => Application.OpenForms.OfType<BrowserForm>().ToList();

    readonly PageStrip _strip;
    readonly Dictionary<string, Panel> _pages = new();
    readonly ToolTip _tips = new();
    string _page = "";

    SettingsForm(Icon? icon)
    {
        Text = "Settings";
        Icon = icon;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        Font = Theme.Ui(9f);
        ClientSize = new Size(D(640), D(540));
        _strip = new PageStrip(["General", "Privacy", "Passwords"]) { Dock = DockStyle.Top, Height = D(66) };
        _strip.Selected += Select;
        Controls.Add(_strip);
        _pages["General"] = BuildGeneral();
        _pages["Privacy"] = BuildPrivacy();
        _pages["Passwords"] = BuildPasswords();
        foreach (var p in _pages.Values) { p.Visible = false; Controls.Add(p); p.BringToFront(); }
        ApplyTheme();
        Theme.Changed += ApplyTheme;
        Services.AdBlocker.Updated += OnListsUpdated;
    }

    int D(int v) => LogicalToDeviceUnits(v);

    public void Select(string page)
    {
        if (!_pages.ContainsKey(page) || page == _page) return;
        if (_pages.TryGetValue(_page, out var old)) old.Visible = false;
        _page = page;
        _strip.Current = page;
        var panel = _pages[page];
        if (page == "Passwords") RefreshPasswords();
        if (page == "Privacy") RefreshPrivacy();
        if (page == "General") RefreshGeneral();
        panel.Visible = true;
        Text = page;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, Theme.Current.Dark);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    void ApplyTheme()
    {
        var t = Theme.Current;
        BackColor = t.Toolbar;
        if (IsHandleCreated) Native.StyleWindow(Handle, t.Dark);
        foreach (var p in _pages.Values) Recolor(p, t);
        Invalidate(true);
    }

    static void Recolor(Control c, Theme t)
    {
        switch (c)
        {
            case RowList list: list.ApplyTheme(); break;
            case TextBox box: box.BackColor = t.FieldEditing; box.ForeColor = t.Text; break;
            case Label label: label.ForeColor = label.Tag as string == "note" ? t.Secondary : t.Text; label.BackColor = Color.Transparent; break;
            case Panel panel: panel.BackColor = panel.Tag as string == "field" ? t.FieldEditing : t.Toolbar; break;
        }
        foreach (Control child in c.Controls) Recolor(child, t);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Theme.Current.Separator);
        e.Graphics.DrawLine(p, 0, _strip.Bottom, ClientSize.Width, _strip.Bottom);
    }

    // ---------- Layout helpers ----------

    const int LabelColumn = 190, ControlColumn = 206, RowGap = 16;

    Panel NewPage() => new() { Bounds = new Rectangle(0, D(67), ClientSize.Width, ClientSize.Height - D(67)) };

    /// <summary>A right-aligned row label ("Search engine:") in the left column, returns the row's top.</summary>
    Label RowLabel(Panel page, string text, int y)
    {
        var l = new Label
        {
            Text = text, AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
            Bounds = new Rectangle(D(10), y, D(LabelColumn - 10), D(28)),
        };
        page.Controls.Add(l);
        return l;
    }

    Label Note(Panel page, string text, int x, int y, int width)
    {
        var l = new Label
        {
            Text = text, Tag = "note", AutoSize = false, Font = Theme.Ui(8.25f),
            Bounds = new Rectangle(x, y, width, D(34)),
        };
        page.Controls.Add(l);
        return l;
    }

    // ---------- General ----------

    PopupButton _engine = null!, _sleep = null!;
    Label _defaultNote = null!;
    PushButton _makeDefault = null!;

    static readonly (string Label, double Minutes)[] SleepChoices =
        [("After 5 minutes", 5), ("After 10 minutes", 10), ("After 30 minutes", 30), ("After 1 hour", 60), ("Never", 0)];

    Panel BuildGeneral()
    {
        var page = NewPage();
        int x = D(ControlColumn), y = D(28);

        RowLabel(page, "Search engine:", y);
        _engine = new PopupButton { Bounds = new Rectangle(x, y, D(220), D(28)), AccessibleName = "Search engine" };
        _engine.Choices = Settings.SearchEngines.Select(e => (e.Name, (Action)(() =>
        {
            S.UseSearchEngine(e);
            foreach (var w in Windows) w.SettingsChanged();
            RefreshGeneral();
        }))).ToList();
        page.Controls.Add(_engine);
        Note(page, "Used when you type something that isn't a web address.", x, y + D(32), D(380));
        y += D(28 + 38 + RowGap);

        RowLabel(page, "Sleeping tabs:", y);
        _sleep = new PopupButton { Bounds = new Rectangle(x, y, D(220), D(28)), AccessibleName = "Sleeping tabs" };
        _sleep.Choices = SleepChoices.Select(c => (c.Label, (Action)(() =>
        {
            S.SleepTabsAfterMinutes = c.Minutes;
            S.Save();
            RefreshGeneral();
        }))).ToList();
        page.Controls.Add(_sleep);
        Note(page, "Tabs you haven't looked at go to sleep to free up memory. Tabs playing sound stay awake.", x, y + D(32), D(400));
        y += D(28 + 38 + RowGap);

        RowLabel(page, "Default browser:", y);
        _makeDefault = new PushButton("Make Brisk the Default…") { Bounds = new Rectangle(x, y, D(220), D(28)) };
        _makeDefault.Click += (_, _) => { DefaultBrowser.Register(); DefaultBrowser.OpenSettings(); };
        page.Controls.Add(_makeDefault);
        _defaultNote = Note(page, "", x, y + D(4), D(400));
        return page;
    }

    void RefreshGeneral()
    {
        _engine.Text = S.CurrentSearchEngine?.Name ?? "Custom";
        _sleep.Text = SleepChoices.FirstOrDefault(c => Math.Abs(c.Minutes - S.SleepTabsAfterMinutes) < 0.01).Label ?? "Custom";
        bool isDefault = DefaultBrowser.IsDefault();
        _makeDefault.Visible = !isDefault;
        _defaultNote.Visible = isDefault;
        _defaultNote.Text = "Brisk is your default browser.";
        _defaultNote.Tag = null;
        _defaultNote.ForeColor = Theme.Current.Text;
    }

    // ---------- Privacy ----------

    Switch _cookies = null!;
    PopupButton _dns = null!;
    Label _dnsNote = null!, _listsNote = null!;
    PushButton _updateLists = null!, _turnOn = null!;
    RowList _sites = null!;

    Panel BuildPrivacy()
    {
        var page = NewPage();
        int x = D(ControlColumn), y = D(24);

        RowLabel(page, "Cookie banners:", y);
        _cookies = new Switch { Location = new Point(x, y + D(3)), AccessibleName = "Hide cookie notices" };
        _cookies.CheckedChanged += () =>
        {
            S.HideCookieBanners = _cookies.Checked;
            S.Save();
            Services.AdBlocker.Reload();
            RefreshPrivacy();
        };
        page.Controls.Add(_cookies);
        var cookieLabel = new Label { Text = "Hide cookie notices", AutoSize = true, Location = new Point(x + D(50), y + D(5)) };
        page.Controls.Add(cookieLabel);
        Note(page, "Hides \"accept cookies\" pop-ups, or turns them down where a site requires an answer.", x, y + D(30), D(400));
        y += D(28 + 36 + RowGap);

        RowLabel(page, "Ad-blocking DNS:", y);
        _dns = new PopupButton { Bounds = new Rectangle(x, y, D(220), D(28)), AccessibleName = "Ad-blocking DNS" };
        var choices = new List<(string, Action)> { ("Off", () => SetDns(false, "")) };
        choices.AddRange(Settings.DnsProviders.Select(p => (p.Name, (Action)(() => SetDns(true, p.Url)))));
        _dns.Choices = choices;
        page.Controls.Add(_dns);
        _dnsNote = Note(page, "", x, y + D(32), D(400));
        y += D(28 + 36 + RowGap);

        RowLabel(page, "Filter lists:", y);
        _updateLists = new PushButton("Update Now") { Bounds = new Rectangle(x, y, D(120), D(28)) };
        _updateLists.Click += (_, _) => { _listsNote.Text = "Updating…"; Services.AdBlocker.UpdateLists(); };
        page.Controls.Add(_updateLists);
        _listsNote = Note(page, "", x + D(132), y + D(6), D(270));
        y += D(28 + RowGap + 6);

        var sitesLabel = RowLabel(page, "Ad blocking off on:", y);
        sitesLabel.TextAlign = ContentAlignment.TopRight;
        sitesLabel.Padding = new Padding(0, D(6), 0, 0);
        _sites = new RowList { Bounds = new Rectangle(x, y, D(400), D(150)), Empty = "Ad blocking is on for every site.", ListName = "Sites with ad blocking off" };
        _sites.SelectionChanged += () => _turnOn.Enabled = _sites.SelectedIndex >= 0;
        page.Controls.Add(_sites);
        y += D(150 + 8);
        _turnOn = new PushButton("Turn Ad Blocking Back On") { Bounds = new Rectangle(x, y, D(210), D(28)), Enabled = false };
        _turnOn.Click += (_, _) =>
        {
            if (_sites.SelectedItem is not string site) return;
            S.AdblockDisabledSites.Remove(site);
            S.Save();
            foreach (var w in Windows) w.AdblockTurnedOnFor(site);
            RefreshPrivacy();
        };
        page.Controls.Add(_turnOn);
        return page;
    }

    void SetDns(bool on, string server)
    {
        S.DnsBlocking = on;
        if (on) S.DnsServer = server;
        S.Save();
        RefreshPrivacy();
    }

    void RefreshPrivacy()
    {
        _cookies.Checked = S.HideCookieBanners;
        _dns.Text = S.DnsBlocking ? S.DnsName : "Off";
        var applied = Services.AppliedDns;
        bool pending = (S.DnsBlocking, S.DnsBlocking ? S.DnsServer : "") != (applied.On, applied.On ? applied.Server : "");
        _dnsNote.Text = pending
            ? "The change takes effect the next time you start Brisk."
            : Services.DnsHealth?.Unreachable == true
                ? $"{S.DnsName} can't be reached right now, so Brisk is using your normal DNS."
                : "A second layer that blocks ad and tracker servers by name, for everything the page loads.";
        ShowListsDate();
        _sites.Items = S.AdblockDisabledSites.Order(StringComparer.OrdinalIgnoreCase).Cast<object>().ToList();
        _turnOn.Enabled = _sites.SelectedIndex >= 0;
    }

    void ShowListsDate()
    {
        var when = AdBlocker.LastUpdated;
        _listsNote.Text = when is null ? "Not downloaded yet."
            : when.Value.Date == DateTime.Today ? $"Updated today at {when.Value:t}."
            : $"Updated {when.Value:d MMMM}.";
    }

    void OnListsUpdated(string message)
    {
        if (_listsNote is null || IsDisposed) return;
        if (message.StartsWith("Couldn't")) _listsNote.Text = "Couldn't update. Check your connection.";
        else ShowListsDate();
    }

    // ---------- Passwords ----------

    Switch _offer = null!;
    TextBox _search = null!;
    RowList _passwords = null!;
    PushButton _copy = null!, _reveal = null!, _delete = null!, _deleteAll = null!;
    List<SavedPassword> _allPasswords = new();
    SavedPassword? _revealed;

    Panel BuildPasswords()
    {
        var page = NewPage();
        int x = D(32), y = D(22), w = ClientSize.Width - D(64);

        _offer = new Switch { Location = new Point(x, y + D(2)), AccessibleName = "Offer to save passwords" };
        _offer.CheckedChanged += () =>
        {
            S.SavePasswords = _offer.Checked;
            S.Save();
            foreach (var win in Windows) win.ApplyPasswordSetting();
        };
        page.Controls.Add(_offer);
        page.Controls.Add(new Label { Text = "Offer to save passwords", AutoSize = true, Location = new Point(x + D(50), y + D(4)) });
        Note(page, "Passwords are saved only on this PC, encrypted with your Windows account, and filled in when you log in again.",
            x, y + D(30), w);
        y += D(70);

        var field = new Panel { Tag = "field", Bounds = new Rectangle(x, y, w, D(30)) };
        field.Paint += (_, e) =>
        {
            Shapes.Smooth(e.Graphics);
            float s = D(14);
            Glyphs.Search(e.Graphics, new RectangleF(D(9), (field.Height - s) / 2, s, s), Theme.Current.Secondary, 1.6f * DeviceDpi / 96f);
        };
        _search = new TextBox
        {
            BorderStyle = BorderStyle.None, PlaceholderText = "Search passwords", AccessibleName = "Search passwords",
            Bounds = new Rectangle(D(30), (D(30) - D(18)) / 2 + D(1), w - D(40), D(18)),
        };
        _search.TextChanged += (_, _) => FilterPasswords();
        field.Controls.Add(_search);
        page.Controls.Add(field);
        y += D(30 + 10);

        _passwords = new RowList
        {
            Bounds = new Rectangle(x, y, w, D(240)), RowHeight = 44, ListName = "Saved passwords",
            Empty = "No saved passwords yet. When you log in to a site, Brisk offers to save the password.",
            DrawRow = DrawPassword,
        };
        _passwords.SelectionChanged += () => { _revealed = null; UpdatePasswordButtons(); };
        _passwords.DoubleClicked += () => _copy.PerformClick();
        page.Controls.Add(_passwords);
        y += D(240 + 10);

        _copy = new PushButton("Copy Password") { Bounds = new Rectangle(x, y, D(130), D(28)) };
        _copy.Click += (_, _) => CopySelected();
        _reveal = new PushButton("Show") { Bounds = new Rectangle(x + D(138), y, D(80), D(28)) };
        _reveal.Click += (_, _) =>
        {
            var p = _passwords.SelectedItem as SavedPassword;
            _revealed = _revealed == p ? null : p;
            _reveal.Text = _revealed is null ? "Show" : "Hide";
            _passwords.Invalidate();
        };
        _delete = new PushButton("Delete") { Bounds = new Rectangle(x + D(226), y, D(90), D(28)) };
        _delete.Click += async (_, _) => await DeleteSelectedAsync();
        _deleteAll = new PushButton("Delete All…") { Bounds = new Rectangle(x + w - D(120), y, D(120), D(28)) };
        _deleteAll.Click += async (_, _) => await DeleteAllAsync();
        page.Controls.AddRange([_copy, _reveal, _delete, _deleteAll]);
        return page;
    }

    void RefreshPasswords()
    {
        _offer.Checked = S.SavePasswords;
        _allPasswords = PasswordStore.Load();
        FilterPasswords();
    }

    void FilterPasswords()
    {
        var q = _search.Text.Trim();
        _passwords.Items = _allPasswords
            .Where(p => q.Length == 0 || p.Site.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.Username.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Cast<object>().ToList();
        _revealed = null;
        UpdatePasswordButtons();
    }

    void UpdatePasswordButtons()
    {
        var p = _passwords.SelectedItem as SavedPassword;
        _copy.Enabled = _reveal.Enabled = p is { Never: false };
        _delete.Enabled = p is not null;
        _deleteAll.Enabled = _allPasswords.Count > 0;
        _reveal.Text = "Show";
    }

    void DrawPassword(Graphics g, Rectangle r, object item, bool selected)
    {
        var p = (SavedPassword)item;
        var t = Theme.Current;
        var main = selected ? Color.White : t.Text;
        // Solid colours only: text drawn into the list box can't be semi-transparent.
        var dim = selected ? Color.FromArgb(220, 234, 255) : t.Secondary;
        using var bold = Theme.Ui(9.5f, FontStyle.Bold);
        using var small = Theme.Ui(8.5f);
        int pad = D(12), half = r.Width * 55 / 100;
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, p.Site, bold, new Rectangle(r.X + pad, r.Y + D(5), half - pad, D(19)), main, flags);
        TextRenderer.DrawText(g, p.Never ? "Never saved for this site" : p.Username.Length > 0 ? p.Username : "(no username)",
            small, new Rectangle(r.X + pad, r.Y + D(23), half - pad, D(17)), dim, flags);
        if (p.Never) return;
        string shown = p == _revealed ? PasswordStore.Reveal(p) ?? "Can't be read" : "••••••••";
        TextRenderer.DrawText(g, shown, Theme.Ui(9.5f), new Rectangle(r.X + half, r.Y, r.Width - half - pad, r.Height), main,
            flags | TextFormatFlags.VerticalCenter);
    }

    void CopySelected()
    {
        if (_passwords.SelectedItem is not SavedPassword { Never: false } p) return;
        var text = PasswordStore.Reveal(p);
        if (string.IsNullOrEmpty(text)) return;
        // Keep the password out of Windows clipboard history and cloud clipboard.
        var data = new DataObject();
        data.SetText(text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
        Clipboard.SetDataObject(data, copy: true);
        _tips.Show("Password copied", _copy, 0, -D(28), 1500);
    }

    async Task DeleteSelectedAsync()
    {
        if (_passwords.SelectedItem is not SavedPassword p) return;
        if (Services.Profile is not { } profile) { NoEngine(); return; }
        // The engine owns the file; ask it to remove what was saved at that moment, which is this entry.
        // (A range of a few milliseconds removed nothing in testing; a second either side works.)
        var from = p.Created.AddSeconds(-1);
        var to = p.Created.AddSeconds(1);
        await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.PasswordAutosave, from, to);
        RefreshPasswords();
    }

    async Task DeleteAllAsync()
    {
        var answer = MessageBox.Show(this, "Delete every password Brisk has saved? You'll need to type them in again the next time you log in.",
            "Delete All Passwords", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) return;
        if (Services.Profile is not { } profile) { NoEngine(); return; }
        await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.PasswordAutosave);
        RefreshPasswords();
    }

    void NoEngine() => MessageBox.Show(this, "Open a page in a normal (not private) window first, then try again.", "Passwords");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= ApplyTheme;
            Services.AdBlocker.Updated -= OnListsUpdated;
            _tips.Dispose();
            if (_open == this) _open = null;
        }
        base.Dispose(disposing);
    }

    // ---------- Controls ----------

    /// <summary>The page icons across the top of the window.</summary>
    sealed class PageStrip : ChromeControl
    {
        readonly string[] _pages;
        string _current = "";
        int _hover = -1;
        public event Action<string>? Selected;

        public PageStrip(string[] pages)
        {
            _pages = pages;
            AccessibleName = "Settings pages";
        }

        public string Current { get => _current; set { _current = value; Invalidate(); } }

        Rectangle Cell(int i)
        {
            int w = LogicalToDeviceUnits(84), h = Height - LogicalToDeviceUnits(10);
            int x0 = (Width - w * _pages.Length) / 2;
            return new Rectangle(x0 + i * w, LogicalToDeviceUnits(5), w, h);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Enumerable.Range(0, _pages.Length).FirstOrDefault(i => Cell(i).Contains(e.Location), -1);
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < _pages.Length; i++)
                if (Cell(i).Contains(e.Location)) Selected?.Invoke(_pages[i]);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Toolbar);
            Shapes.Smooth(g);
            using var font = Theme.Ui(8.5f);
            for (int i = 0; i < _pages.Length; i++)
            {
                var r = Cell(i);
                bool on = _pages[i] == _current;
                if (on || i == _hover)
                {
                    using var b = new SolidBrush(on ? T.Pressed : T.Hover);
                    using var path = Shapes.Rounded(r, Dp(8));
                    g.FillPath(b, path);
                }
                var color = on ? T.Accent : T.Glyph;
                float s = Dp(22);
                var icon = new RectangleF(r.X + (r.Width - s) / 2, r.Y + Dp(6), s, s);
                switch (_pages[i])
                {
                    case "General": Gear(g, icon, color, Dp(1.7f)); break;
                    case "Privacy": Glyphs.Shield(g, icon, color, Dp(1.7f), on: false); break;
                    case "Passwords": Key(g, icon, color, Dp(1.7f)); break;
                }
                TextRenderer.DrawText(g, _pages[i], font, new Rectangle(r.X, r.Bottom - (int)Dp(22), r.Width, (int)Dp(18)),
                    on ? T.Accent : T.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }

        static void Gear(Graphics g, RectangleF r, Color c, float w)
        {
            using var p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                g.DrawLine(p, cx + cos * r.Width * 0.3f, cy + sin * r.Width * 0.3f, cx + cos * r.Width * 0.46f, cy + sin * r.Width * 0.46f);
            }
            g.DrawEllipse(p, cx - r.Width * 0.3f, cy - r.Width * 0.3f, r.Width * 0.6f, r.Width * 0.6f);
            g.DrawEllipse(p, cx - r.Width * 0.11f, cy - r.Width * 0.11f, r.Width * 0.22f, r.Width * 0.22f);
        }

        static void Key(Graphics g, RectangleF r, Color c, float w)
        {
            using var p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawEllipse(p, r.X + r.Width * 0.08f, r.Y + r.Height * 0.08f, r.Width * 0.38f, r.Height * 0.38f);
            g.DrawLine(p, r.X + r.Width * 0.4f, r.Y + r.Height * 0.4f, r.X + r.Width * 0.88f, r.Y + r.Height * 0.88f);
            g.DrawLine(p, r.X + r.Width * 0.7f, r.Y + r.Height * 0.7f, r.X + r.Width * 0.82f, r.Y + r.Height * 0.58f);
            g.DrawLine(p, r.X + r.Width * 0.58f, r.Y + r.Height * 0.58f, r.X + r.Width * 0.68f, r.Y + r.Height * 0.48f);
        }
    }
}

/// <summary>An Apple-style on/off switch.</summary>
sealed class Switch : ChromeControl
{
    bool _checked;
    public event Action? CheckedChanged;

    public Switch()
    {
        Size = new Size(LogicalToDeviceUnits(38), LogicalToDeviceUnits(22));
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); }
    }

    void Toggle() { Checked = !Checked; CheckedChanged?.Invoke(); }

    protected override void OnClick(EventArgs e) { Toggle(); base.OnClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) Toggle();
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? T.Toolbar);
        Shapes.Smooth(g);
        var track = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Shapes.Rounded(track, track.Height / 2))
        using (var b = new SolidBrush(Checked ? T.Accent : T.Dark ? Color.FromArgb(72, 72, 74) : Color.FromArgb(214, 214, 218)))
            g.FillPath(b, path);
        float d = Height - Dp(4);
        float x = Checked ? Width - d - Dp(2) : Dp(2);
        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            g.FillEllipse(shadow, x, Dp(2) + Dp(0.7f), d, d);
        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, x, Dp(2), d, d);
        if (Focused && ShowFocusCues)
        {
            using var p = new Pen(Color.FromArgb(120, T.Accent), Dp(2));
            using var path = Shapes.Rounded(RectangleF.Inflate(track, -1, -1), track.Height / 2);
            g.DrawPath(p, path);
        }
    }
}

/// <summary>A rounded push button.</summary>
sealed class PushButton : ChromeControl
{
    public PushButton(string text)
    {
        Text = text;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) PerformClick();
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? T.Toolbar);
        Shapes.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Shapes.Rounded(r, Dp(6));
        var fill = Pressed ? T.Pressed : T.Dark ? (Hovered ? Color.FromArgb(84, 84, 88) : Color.FromArgb(72, 72, 74)) : (Hovered ? Color.FromArgb(246, 246, 248) : Color.White);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(T.Dark ? Color.FromArgb(90, 90, 94) : Color.FromArgb(200, 200, 205))) g.DrawPath(p, path);
        if (Focused && ShowFocusCues)
        {
            using var p = new Pen(Color.FromArgb(140, T.Accent), Dp(2));
            g.DrawPath(p, path);
        }
        var color = Enabled ? T.Text : T.Secondary;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A pop-up menu button: shows the current choice and a chevron, and opens a menu of choices.</summary>
sealed class PopupButton : ChromeControl
{
    readonly MacMenu _menu = new();
    public List<(string Label, Action Choose)> Choices = new();

    public PopupButton()
    {
        TabStop = true;
        AccessibleRole = AccessibleRole.ButtonDropDown;
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnClick(EventArgs e)
    {
        _menu.Items.Clear();
        foreach (var (label, choose) in Choices)
        {
            var item = new ToolStripMenuItem(label) { Checked = label == Text };
            item.Click += (_, _) => choose();
            _menu.Items.Add(item);
        }
        _menu.MinimumSize = new Size(Width, 0);
        _menu.Show(this, new Point(0, Height + LogicalToDeviceUnits(2)));
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.Down) OnClick(EventArgs.Empty);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? T.Toolbar);
        Shapes.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Shapes.Rounded(r, Dp(6));
        var fill = T.Dark ? (Hovered ? Color.FromArgb(84, 84, 88) : Color.FromArgb(72, 72, 74)) : (Hovered ? Color.FromArgb(246, 246, 248) : Color.White);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(T.Dark ? Color.FromArgb(90, 90, 94) : Color.FromArgb(200, 200, 205))) g.DrawPath(p, path);
        if (Focused && ShowFocusCues)
        {
            using var p = new Pen(Color.FromArgb(140, T.Accent), Dp(2));
            g.DrawPath(p, path);
        }
        int pad = LogicalToDeviceUnits(10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(pad, 0, Width - pad * 3, Height), T.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        // Up/down chevrons, as on a Mac pop-up button.
        float s = Dp(7), cx = Width - Dp(16), cy = Height / 2f;
        using var pen = new Pen(T.Secondary, Dp(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(cx - s / 2, cy - Dp(2)), new PointF(cx, cy - Dp(2) - s / 2), new PointF(cx + s / 2, cy - Dp(2))]);
        g.DrawLines(pen, [new PointF(cx - s / 2, cy + Dp(2)), new PointF(cx, cy + Dp(2) + s / 2), new PointF(cx + s / 2, cy + Dp(2))]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>A rounded, scrollable list with custom-drawn rows and an accent selection.</summary>
sealed class RowList : Panel
{
    readonly ListBox _list = new()
    {
        BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed,
        IntegralHeight = false,
    };
    public event Action? SelectionChanged, DoubleClicked;
    public string Empty = "";
    public Action<Graphics, Rectangle, object, bool>? DrawRow;
    public int RowHeight { set => _list.ItemHeight = LogicalToDeviceUnits(value); }
    public string ListName { set => _list.AccessibleName = value; }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    public RowList()
    {
        Tag = "field";
        Padding = new Padding(1);
        _list.ItemHeight = LogicalToDeviceUnits(30);
        _list.Dock = DockStyle.Fill;
        _list.DrawItem += OnDrawItem;
        _list.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke();
        _list.DoubleClick += (_, _) => DoubleClicked?.Invoke();
        _list.Paint += (_, _) => { };
        _list.HandleCreated += (_, _) => SetWindowTheme(_list.Handle, Theme.Current.Dark ? "DarkMode_Explorer" : "Explorer", null);
        Controls.Add(_list);
        ApplyTheme();
    }

    public List<object> Items
    {
        set
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Items.AddRange(value.ToArray());
            _list.EndUpdate();
            Invalidate(true);
            _list.Visible = value.Count > 0;
        }
    }

    public int SelectedIndex => _list.SelectedIndex;
    public object? SelectedItem => _list.SelectedItem;
    public new void Invalidate() => _list.Invalidate();

    public void ApplyTheme()
    {
        var t = Theme.Current;
        BackColor = t.FieldEditing;
        _list.BackColor = t.FieldEditing;
        _list.ForeColor = t.Text;
        if (_list.IsHandleCreated) SetWindowTheme(_list.Handle, t.Dark ? "DarkMode_Explorer" : "Explorer", null);
        base.Invalidate(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        using var p = new Pen(t.Separator);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        if (_list.Items.Count == 0)
            TextRenderer.DrawText(e.Graphics, Empty, Font, Rectangle.Inflate(ClientRectangle, -LogicalToDeviceUnits(20), 0), t.Secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var t = Theme.Current;
        var g = e.Graphics;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(t.FieldEditing)) g.FillRectangle(bg, e.Bounds);
        if (selected)
        {
            Shapes.Smooth(g);
            var r = new RectangleF(e.Bounds.X + LogicalToDeviceUnits(4), e.Bounds.Y + 1, e.Bounds.Width - LogicalToDeviceUnits(8), e.Bounds.Height - 2);
            using var path = Shapes.Rounded(r, LogicalToDeviceUnits(5));
            using var b = new SolidBrush(t.Accent);
            g.FillPath(b, path);
        }
        var item = _list.Items[e.Index];
        if (DrawRow is not null) DrawRow(g, e.Bounds, item, selected);
        else
            TextRenderer.DrawText(g, item.ToString(), Font, new Rectangle(e.Bounds.X + LogicalToDeviceUnits(12), e.Bounds.Y, e.Bounds.Width - LogicalToDeviceUnits(24), e.Bounds.Height),
                selected ? Color.White : t.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
