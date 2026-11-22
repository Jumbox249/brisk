using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Brisk;

sealed class BrowserTab
{
    public required TabChip Chip;
    public WebView2? View;
    public Task? Ready;
    public string Url = "";
    public string Title = "New tab";
    /// <summary>Top-level page URL, used as the source when matching subresource requests.</summary>
    public string PageUrl = "";
    public bool AdblockOn = true;
    public int Blocked;
}

sealed class BrowserForm : Form
{
    static readonly string CosmeticScript = ReadResource("cosmetic.js");

    static readonly string[] TrackingParams =
    [
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "utm_name",
        "fbclid", "gclid", "gclsrc", "dclid", "msclkid", "yclid", "twclid", "ttclid", "igshid",
        "mc_eid", "mc_cid", "_hsenc", "_hsmi", "mkt_tok", "oly_anon_id", "oly_enc_id", "vero_id",
    ];

    readonly Settings _settings = Settings.Load();
    readonly AdBlocker _adblock;
    readonly Task<CoreWebView2Environment> _env;
    readonly List<BrowserTab> _tabs = new();
    BrowserTab? _active;

    readonly ToolTip _tips = new();
    readonly Panel _strip = new() { Dock = DockStyle.Fill, BackColor = Theme.Strip };
    readonly IconButton _newTab;
    readonly IconButton _back, _forward, _reload, _menu;
    readonly TextBox _address = new()
    {
        BorderStyle = BorderStyle.None,
        Font = new Font("Segoe UI", 10.5f),
        BackColor = Color.White,
        ForeColor = Theme.Text,
    };
    readonly Panel _addressBox = new() { BackColor = Color.White };
    readonly ShieldButton _shield = new();
    readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    readonly TableLayoutPanel _layout;
    readonly ContextMenuStrip _menuStrip = new();

    bool _fullscreen;
    FormWindowState _preFullscreenState;

    public BrowserForm()
    {
        Text = "Brisk";
        BackColor = Theme.Toolbar;
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(LogicalToDeviceUnits(1280), LogicalToDeviceUnits(820));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }

        _adblock = new AdBlocker(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        _adblock.Updated += msg => { if (msg.StartsWith("Couldn't")) _tips.Show(msg, _shield, 0, _shield.Height, 4000); };
        _adblock.Start();
        _env = CreateEnvironment();

        _newTab = new IconButton("", "New tab (Ctrl+T)", _tips);
        _back = new IconButton("", "Back (Alt+Left)", _tips);
        _forward = new IconButton("", "Forward (Alt+Right)", _tips);
        _reload = new IconButton("", "Reload (F5)", _tips);
        _menu = new IconButton("", "Menu", _tips);

        _newTab.Click += (_, _) => NewTab(_settings.HomePage, focusAddress: true);
        _back.Click += (_, _) => { if (_active?.View?.CanGoBack == true) _active.View.GoBack(); };
        _forward.Click += (_, _) => { if (_active?.View?.CanGoForward == true) _active.View.GoForward(); };
        _reload.Click += (_, _) => _active?.View?.Reload();
        _menu.Click += (_, _) => ShowMenu();
        _shield.Click += (_, _) => ToggleAdblockForSite();
        _tips.SetToolTip(_shield, "Ad blocking for this site — click to turn off or on");

        _address.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(_address.Text); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; ShowAddress(); _active?.View?.Focus(); }
        };
        // Clicking into the address bar selects the whole URL, like other browsers.
        _address.Enter += (_, _) => { _selectOnClick = true; BeginInvoke(_address.SelectAll); };
        _address.MouseUp += (_, _) => { if (_selectOnClick && _address.SelectionLength == 0) _address.SelectAll(); _selectOnClick = false; };
        _addressBox.Controls.Add(_address);
        _addressBox.Resize += (_, _) => LayoutAddress();
        _addressBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Theme.Toolbar);
            using var b = new SolidBrush(Color.White);
            using var pen = new Pen(_address.Focused ? Color.FromArgb(0, 103, 192) : Color.FromArgb(214, 214, 220));
            using var path = IconButton.Rounded(_addressBox.ClientRectangle, LogicalToDeviceUnits(8));
            e.Graphics.FillPath(b, path);
            e.Graphics.DrawPath(pen, path);
        };
        _address.GotFocus += (_, _) => _addressBox.Invalidate();
        _address.LostFocus += (_, _) => _addressBox.Invalidate();

        _strip.Controls.Add(_newTab);
        _strip.Resize += (_, _) => LayoutTabs();

        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Toolbar };
        foreach (var c in new Control[] { _back, _forward, _reload, _addressBox, _shield, _menu }) toolbar.Controls.Add(c);
        toolbar.Resize += (_, _) => LayoutToolbar(toolbar);

        _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(38)));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(44)));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var c in new Control[] { _strip, toolbar, _content }) { c.Margin = Padding.Empty; _layout.Controls.Add(c); }
        Controls.Add(_layout);
    }

    bool _selectOnClick;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestoreSession();
    }

    // ---------- Engine ----------

    async Task<CoreWebView2Environment> CreateEnvironment()
    {
        var args = new List<string>
        {
            // Never expose the local IP address through WebRTC.
            "--force-webrtc-ip-handling-policy=default_public_interface_only",
        };
        var userDataDir = Path.Combine(Settings.DataDir, "Profile");
        await Task.Run(() => ConfigureSecureDns(userDataDir));
        var port = Environment.GetEnvironmentVariable("BRISK_REMOTE_DEBUGGING_PORT");
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
    /// server in secure mode (no fallback to the system resolver). WebView2 ignores the DoH feature
    /// flags, but honours the same "Local State" preferences Chromium uses, so set them before the
    /// browser process starts.
    /// </summary>
    void ConfigureSecureDns(string userDataDir)
    {
        var path = Path.Combine(userDataDir, "EBWebView", "Local State");
        try
        {
            var state = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
                : new JsonObject();
            if (_settings.DnsBlocking)
                state["dns_over_https"] = new JsonObject { ["mode"] = "secure", ["templates"] = _settings.DnsServer };
            else if (!state.Remove("dns_over_https"))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, state.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    // ---------- Tabs ----------

    BrowserTab NewTab(string url, bool activate = true, bool focusAddress = false, int? index = null)
    {
        var chip = new TabChip();
        var tab = new BrowserTab { Chip = chip, Url = url, Title = TitleFor(url) };
        chip.SetTitle(tab.Title);
        chip.Click += (_, _) => Activate(tab);
        chip.CloseClicked += () => CloseTab(tab);
        _tips.SetToolTip(chip, tab.Title);
        _tabs.Insert(index ?? _tabs.Count, tab);
        _strip.Controls.Add(chip);
        LayoutTabs();
        if (activate) Activate(tab);
        if (focusAddress) { _address.Focus(); _address.SelectAll(); }
        return tab;
    }

    void Activate(BrowserTab tab)
    {
        if (_active == tab) return;
        var previous = _active;
        _active = tab;
        foreach (var t in _tabs) t.Chip.Active = t == tab;

        // Tabs are created lazily: a restored tab costs nothing until it is first shown.
        if (tab.View is null) _ = EnsureView(tab, navigate: true);
        else tab.View.Visible = true;
        if (previous?.View is not null) previous.View.Visible = false;

        tab.View?.BringToFront();
        UpdateChrome();
        if (!_address.Focused) tab.View?.Focus();
    }

    void CloseTab(BrowserTab tab)
    {
        var i = _tabs.IndexOf(tab);
        if (i < 0) return;
        if (_tabs.Count == 1) { Close(); return; }
        _tabs.RemoveAt(i);
        _strip.Controls.Remove(tab.Chip);
        tab.Chip.Dispose();
        if (_active == tab)
        {
            _active = null;
            Activate(_tabs[Math.Min(i, _tabs.Count - 1)]);
        }
        tab.View?.Dispose();
        LayoutTabs();
    }

    async Task EnsureView(BrowserTab tab, bool navigate)
    {
        if (tab.View is null)
        {
            var view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White, Visible = tab == _active };
            tab.View = view;
            _content.Controls.Add(view);
            _ = view.Handle;
            tab.Ready = InitView(tab, view, navigate);
        }
        await tab.Ready!;
    }

    async Task InitView(BrowserTab tab, WebView2 view, bool navigate)
    {
        var env = await _env;
        await view.EnsureCoreWebView2Async(env);
        var core = view.CoreWebView2;

        var s = core.Settings;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsReputationCheckingRequired = false; // SmartScreen sends visited URLs to Microsoft.
        s.AreBrowserAcceleratorKeysEnabled = true;
        core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(CosmeticScript);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);

        core.WebResourceRequested += (_, e) => OnResourceRequested(tab, env, e);
        core.WebMessageReceived += (_, e) => OnWebMessage(tab, core, e);
        core.NavigationStarting += (_, e) => OnNavigationStarting(tab, core, e);
        core.SourceChanged += (_, _) => { tab.Url = core.Source; if (tab == _active) UpdateChrome(); };
        core.HistoryChanged += (_, _) => { if (tab == _active) UpdateChrome(); };
        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? TitleFor(core.Source) : core.DocumentTitle;
            tab.Chip.SetTitle(tab.Title);
            _tips.SetToolTip(tab.Chip, tab.Title);
            if (tab == _active) Text = tab.Title + " — Brisk";
        };
        core.NewWindowRequested += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                var child = NewTab(e.Uri, activate: e.IsUserInitiated, index: _tabs.IndexOf(tab) + 1);
                await EnsureView(child, navigate: false);
                e.NewWindow = child.View!.CoreWebView2;
            }
            finally { deferral.Complete(); }
        };
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
        core.WindowCloseRequested += (_, _) => CloseTab(tab);

        if (navigate) core.Navigate(tab.Url);
    }

    // ---------- Blocking ----------

    void OnNavigationStarting(BrowserTab tab, CoreWebView2 core, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            var cleaned = StripTrackingParams(uri);
            if (cleaned is not null)
            {
                e.Cancel = true;
                core.Navigate(cleaned);
                return;
            }
        }
        tab.PageUrl = e.Uri;
        tab.AdblockOn = _settings.IsAdblockEnabledFor(uri);
        tab.Blocked = 0;
        if (tab == _active) UpdateShield();
    }

    void OnResourceRequested(BrowserTab tab, CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!tab.AdblockOn || !_adblock.IsReady) return;
        var url = e.Request.Uri;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("ws", StringComparison.OrdinalIgnoreCase)) return;

        string type;
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document)
        {
            if (url == tab.PageUrl) return; // never block the page the user asked for
            type = "sub_frame";
        }
        else
        {
            type = RequestType(e.ResourceContext);
        }

        if (_adblock.ShouldBlock(url, tab.PageUrl, type))
        {
            e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
            tab.Blocked++;
            if (tab == _active) UpdateShield();
        }
    }

    static string RequestType(CoreWebView2WebResourceContext c) => c switch
    {
        CoreWebView2WebResourceContext.Stylesheet => "stylesheet",
        CoreWebView2WebResourceContext.Image => "image",
        CoreWebView2WebResourceContext.Media => "media",
        CoreWebView2WebResourceContext.Font => "font",
        CoreWebView2WebResourceContext.Script => "script",
        CoreWebView2WebResourceContext.XmlHttpRequest => "xmlhttprequest",
        CoreWebView2WebResourceContext.Fetch => "xmlhttprequest",
        CoreWebView2WebResourceContext.Websocket => "websocket",
        CoreWebView2WebResourceContext.Ping => "ping",
        CoreWebView2WebResourceContext.CspViolationReport => "csp_report",
        _ => "other",
    };

    void OnWebMessage(BrowserTab tab, CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!tab.AdblockOn || !_adblock.IsReady) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("type", out var type)) return;
            switch (type.GetString())
            {
                case "brisk-init" when m.TryGetProperty("url", out var url):
                    core.PostWebMessageAsJson("{\"type\":\"brisk-init\",\"r\":" + _adblock.UrlCosmetics(url.GetString() ?? "") + "}");
                    break;
                case "brisk-classes" when m.TryGetProperty("classes", out var classes) &&
                                          m.TryGetProperty("ids", out var ids) &&
                                          m.TryGetProperty("exceptions", out var exceptions):
                    var selectors = _adblock.HiddenSelectors(classes, ids, exceptions);
                    if (selectors != "[]") core.PostWebMessageAsJson("{\"type\":\"brisk-hide\",\"selectors\":" + selectors + "}");
                    break;
            }
        }
        catch (JsonException) { }
    }

    void ToggleAdblockForSite()
    {
        if (_active is null || !Uri.TryCreate(_active.Url, UriKind.Absolute, out var uri)) return;
        var key = Settings.SiteKey(uri);
        if (key.Length == 0) return;
        if (!_settings.AdblockDisabledSites.Remove(key)) _settings.AdblockDisabledSites.Add(key);
        _settings.Save();
        _active.AdblockOn = _settings.IsAdblockEnabledFor(uri);
        UpdateShield();
        _active.View?.Reload();
    }

    static string? StripTrackingParams(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query) || uri.Scheme is not ("http" or "https")) return null;
        var parts = uri.Query.TrimStart('?').Split('&');
        var kept = parts.Where(p =>
        {
            var name = Uri.UnescapeDataString(p.Split('=', 2)[0]);
            return !TrackingParams.Contains(name, StringComparer.OrdinalIgnoreCase);
        }).ToArray();
        if (kept.Length == parts.Length) return null;
        var b = new UriBuilder(uri) { Query = string.Join('&', kept) };
        return b.Uri.AbsoluteUri;
    }

    // ---------- Chrome ----------

    void UpdateChrome()
    {
        if (_active is null) return;
        if (!_address.Focused) ShowAddress();
        _back.Enabled = _active.View?.CanGoBack == true;
        _forward.Enabled = _active.View?.CanGoForward == true;
        Text = _active.Title + " — Brisk";
        UpdateShield();
    }

    void ShowAddress() => _address.Text = _active?.Url ?? "";

    void UpdateShield()
    {
        if (_active is null) return;
        _shield.SetState(_active.AdblockOn, _active.Blocked);
    }

    void Go(string input)
    {
        var url = ToUrl(input);
        if (_active is null) { NewTab(url); return; }
        _active.Url = url;
        _active.View?.Focus();
        if (_active.View?.CoreWebView2 is { } core) core.Navigate(url);
        else _ = EnsureView(_active, navigate: true);
    }

    string ToUrl(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return _settings.HomePage;
        if (Uri.TryCreate(input, UriKind.Absolute, out var u) &&
            u.Scheme is "http" or "https" or "file" or "about" or "edge" or "data" or "view-source")
            return input;
        if (!input.Contains(' '))
        {
            var isLocal = input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                          System.Text.RegularExpressions.Regex.IsMatch(input, @"^\d{1,3}(\.\d{1,3}){3}(:\d+)?(/|$)");
            if (isLocal && Uri.TryCreate("http://" + input, UriKind.Absolute, out u)) return u.AbsoluteUri;
            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out u) && u.Host.Contains('.') &&
                !u.Host.EndsWith('.') && !u.Host.StartsWith('.'))
                return u.AbsoluteUri;
        }
        return _settings.SearchUrl + Uri.EscapeDataString(input);
    }

    static string TitleFor(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Length > 0 ? Settings.SiteKey(u) : "New tab";

    void ShowMenu()
    {
        _menuStrip.Items.Clear();
        _menuStrip.Items.Add("New tab\tCtrl+T", null, (_, _) => NewTab(_settings.HomePage, focusAddress: true));

        var site = _active is not null && Uri.TryCreate(_active.Url, UriKind.Absolute, out var u) ? Settings.SiteKey(u) : "";
        if (site.Length > 0)
        {
            var item = new ToolStripMenuItem($"Block ads on {site}") { Checked = _active!.AdblockOn };
            item.Click += (_, _) => ToggleAdblockForSite();
            _menuStrip.Items.Add(item);
        }

        var dns = new ToolStripMenuItem("Ad-blocking DNS (AdGuard)") { Checked = _settings.DnsBlocking };
        dns.Click += (_, _) =>
        {
            _settings.DnsBlocking = !_settings.DnsBlocking;
            _settings.Save();
            MessageBox.Show(this, "Restart Brisk for the DNS change to take effect.", "Brisk");
        };
        _menuStrip.Items.Add(dns);
        _menuStrip.Items.Add("Update filter lists now", null, (_, _) => _adblock.UpdateLists());

        if (_settings.AdblockDisabledSites.Count > 0)
        {
            var allowed = new ToolStripMenuItem($"Sites with ad blocking off ({_settings.AdblockDisabledSites.Count})");
            foreach (var s in _settings.AdblockDisabledSites.Order())
            {
                allowed.DropDownItems.Add($"Turn back on for {s}", null, (_, _) =>
                {
                    _settings.AdblockDisabledSites.Remove(s);
                    _settings.Save();
                    foreach (var t in _tabs.Where(t => TitleFor(t.Url) == s)) t.View?.Reload();
                });
            }
            _menuStrip.Items.Add(allowed);
        }

        _menuStrip.Items.Add(new ToolStripSeparator());
        _menuStrip.Items.Add("About Brisk", null, (_, _) => MessageBox.Show(this,
            "Brisk " + Application.ProductVersion.Split('+')[0] +
            "\nA small, fast, private browser for Windows.\n\nAd blocking: Brave's adblock-rust engine with EasyList, EasyPrivacy and Peter Lowe's list, plus AdGuard DNS.",
            "About Brisk"));
        _menuStrip.Show(_menu, new Point(_menu.Width - _menuStrip.PreferredSize.Width, _menu.Height));
    }

    void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        _fullscreen = on;
        SuspendLayout();
        if (on)
        {
            _preFullscreenState = WindowState;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _layout.RowStyles[0].Height = 0;
            _layout.RowStyles[1].Height = 0;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _preFullscreenState;
            _layout.RowStyles[0].Height = LogicalToDeviceUnits(38);
            _layout.RowStyles[1].Height = LogicalToDeviceUnits(44);
        }
        ResumeLayout();
    }

    // ---------- Layout ----------

    void LayoutTabs()
    {
        if (_strip.Width == 0) return;
        int pad = LogicalToDeviceUnits(8), gap = LogicalToDeviceUnits(2);
        int btn = LogicalToDeviceUnits(30);
        int available = _strip.Width - pad * 2 - btn - gap;
        int width = _tabs.Count == 0 ? 0 : Math.Clamp(available / _tabs.Count - gap, LogicalToDeviceUnits(48), LogicalToDeviceUnits(220));
        int x = pad;
        foreach (var t in _tabs)
        {
            t.Chip.SetBounds(x, 0, width, _strip.Height);
            x += width + gap;
        }
        _newTab.SetBounds(x + gap, (_strip.Height - btn) / 2 + LogicalToDeviceUnits(2), btn, btn);
    }

    void LayoutToolbar(Panel bar)
    {
        int h = bar.Height, b = LogicalToDeviceUnits(32), y = (h - b) / 2, x = LogicalToDeviceUnits(8);
        int gap = LogicalToDeviceUnits(2);
        foreach (var c in new Control[] { _back, _forward, _reload }) { c.SetBounds(x, y, b, b); x += b + gap; }
        int right = bar.Width - LogicalToDeviceUnits(8);
        _menu.SetBounds(right - b, y, b, b);
        int shieldW = LogicalToDeviceUnits(64);
        _shield.SetBounds(right - b - gap - shieldW, y, shieldW, b);
        int addrX = x + LogicalToDeviceUnits(6);
        _addressBox.SetBounds(addrX, y, Math.Max(0, _shield.Left - LogicalToDeviceUnits(6) - addrX), b);
    }

    void LayoutAddress()
    {
        int pad = LogicalToDeviceUnits(12);
        _address.SetBounds(pad, (_addressBox.Height - _address.PreferredHeight) / 2,
            Math.Max(0, _addressBox.Width - pad * 2), _address.PreferredHeight);
        _addressBox.Invalidate();
    }

    // ---------- Keyboard ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.T: NewTab(_settings.HomePage, focusAddress: true); return true;
            case Keys.Control | Keys.W: if (_active is not null) CloseTab(_active); return true;
            case Keys.Control | Keys.L:
            case Keys.Alt | Keys.D:
            case Keys.F6: _address.Focus(); _address.SelectAll(); return true;
            case Keys.Control | Keys.Tab: CycleTab(1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: CycleTab(-1); return true;
            case Keys.F5:
            case Keys.Control | Keys.R: _active?.View?.Reload(); return true;
            case Keys.Alt | Keys.Left: _back.PerformClick(); return true;
            case Keys.Alt | Keys.Right: _forward.PerformClick(); return true;
            case Keys.F11: ToggleWindowFullscreen(); return true;
        }
        if (keyData >= (Keys.Control | Keys.D1) && keyData <= (Keys.Control | Keys.D9) && _tabs.Count > 0)
        {
            int n = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
            Activate(n == 8 ? _tabs[^1] : _tabs[Math.Min(n, _tabs.Count - 1)]);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void ToggleWindowFullscreen() => SetFullscreen(!_fullscreen);

    void CycleTab(int delta)
    {
        if (_active is null || _tabs.Count < 2) return;
        var i = (_tabs.IndexOf(_active) + delta + _tabs.Count) % _tabs.Count;
        Activate(_tabs[i]);
    }

    // ---------- Session ----------

    void RestoreSession()
    {
        var urls = _settings.SessionTabs.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (urls.Count == 0) { NewTab(_settings.HomePage); return; }
        foreach (var u in urls) NewTab(u, activate: false);
        Activate(_tabs[Math.Clamp(_settings.SessionActiveTab, 0, _tabs.Count - 1)]);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _settings.SessionTabs = _tabs.Select(t => t.Url).ToList();
        _settings.SessionActiveTab = _active is null ? 0 : _tabs.IndexOf(_active);
        _settings.Save();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _adblock.Dispose();
        base.Dispose(disposing);
    }

    static string ReadResource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
