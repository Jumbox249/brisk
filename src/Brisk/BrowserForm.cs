using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Brisk;

sealed class BrowserTab
{
    public WebView2? View;
    public Task? Ready;
    public string Url = "";
    public string Title = "New Tab";
    public Image? Favicon;
    /// <summary>Top-level page URL, used as the source when matching subresource requests.</summary>
    public string PageUrl = "";
    public bool AdblockOn = true;
    /// <summary>Scriptlet scripts registered in this tab's view, by host (null: the host has none).</summary>
    public readonly Dictionary<string, string?> Scriptlets = new(StringComparer.OrdinalIgnoreCase);
    public int Blocked;
    public bool Loading;
    public float Progress;
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

    // Logical (96-dpi) sizes of the window chrome.
    const int ToolbarHeight = 52, TabBarHeight = 32, CaptionWidth = 46;

    readonly Settings _settings = Settings.Load();
    readonly AdBlocker _adblock;
    readonly Task<CoreWebView2Environment> _env;
    readonly List<BrowserTab> _tabs = new();
    readonly Stack<(string Url, int Index)> _closedTabs = new();
    BrowserTab? _active;

    readonly ToolTip _tips = new();
    readonly DragPanel _toolbar = new();
    readonly GlyphButton _back, _forward, _newTab, _menu;
    readonly ShieldButton _shield = new();
    readonly AddressField _address;
    readonly CaptionButton _min = new(CaptionButton.Kind.Minimize);
    readonly CaptionButton _max = new(CaptionButton.Kind.Maximize);
    readonly CaptionButton _close = new(CaptionButton.Kind.Close);
    readonly TabBar _tabBar;
    readonly Panel _separator = new();
    readonly Panel _content = new() { BackColor = Color.White };
    readonly MacMenu _menuStrip = new();
    readonly System.Windows.Forms.Timer _progressTimer = new() { Interval = 16 };

    bool _fullscreen;
    FormWindowState _preFullscreenState;
    DnsHealth? _dnsHealth;
    (bool On, string Server) _appliedDns;

    public BrowserForm()
    {
        Text = "Brisk";
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(LogicalToDeviceUnits(1280), LogicalToDeviceUnits(820));
        MinimumSize = new Size(LogicalToDeviceUnits(520), LogicalToDeviceUnits(360));
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Ui(9f);
        Icon = LoadIcon();

        _adblock = new AdBlocker(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        _adblock.Updated += msg => { if (msg.StartsWith("Couldn't")) _tips.Show(msg, _shield, 0, _shield.Height, 4000); };
        _adblock.Start();
        _env = CreateEnvironment();

        _back = new GlyphButton(Glyphs.Back, "Back (Alt+Left)", _tips) { GlyphSize = 18 };
        _forward = new GlyphButton(Glyphs.Forward, "Forward (Alt+Right)", _tips) { GlyphSize = 18 };
        _newTab = new GlyphButton(Glyphs.Plus, "New Tab (Ctrl+T)", _tips) { GlyphSize = 15 };
        _menu = new GlyphButton(Glyphs.Ellipsis, "More", _tips) { GlyphSize = 17 };
        _address = new AddressField(_tips);
        _tabBar = new TabBar(_tips);

        _back.Click += (_, _) => { if (_active?.View?.CanGoBack == true) _active.View.GoBack(); };
        _forward.Click += (_, _) => { if (_active?.View?.CanGoForward == true) _active.View.GoForward(); };
        _newTab.Click += (_, _) => NewTab(_settings.HomePage, editAddress: true);
        _menu.Click += (_, _) => ShowMenu();
        _shield.Click += (_, _) => ToggleAdblockForSite();
        _tips.SetToolTip(_shield, "Ad blocking for this site. Click to turn it off or on.");
        _address.Submitted += Go;
        _address.ReloadClicked += () =>
        {
            if (_active?.View is not { } v) return;
            if (_active.Loading) v.Stop(); else v.Reload();
        };
        _min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _max.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        _close.Click += (_, _) => Close();
        _tabBar.TabSelected += Activate;
        _tabBar.TabClosed += CloseTab;

        foreach (var c in new Control[] { _back, _forward, _shield, _address, _newTab, _menu, _min, _max, _close })
            _toolbar.Controls.Add(c);
        Controls.AddRange([_toolbar, _tabBar, _separator, _content]);

        _progressTimer.Tick += (_, _) => AnimateProgress();
        UpdatePlaceholder();
        Theme.Changed += ApplyTheme;
        ApplyTheme();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        InstallWheelHook();
        RestoreSession();
        _ = RefreshScriptletsAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, Theme.Current.Dark);
        // Recompute the frame now that WM_NCCALCSIZE removes the title bar.
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027 /* NOSIZE|NOMOVE|NOZORDER|FRAMECHANGED */);
    }

    void ApplyTheme()
    {
        var t = Theme.Current;
        BackColor = t.Toolbar;
        _toolbar.BackColor = t.Toolbar;
        _separator.BackColor = t.Separator;
        if (IsHandleCreated) Native.StyleWindow(Handle, t.Dark);
        foreach (Control c in _toolbar.Controls) c.Invalidate();
        _tabBar.Invalidate();
    }

    static Icon? LoadIcon()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("brisk.ico");
        return s is null ? null : new Icon(s);
    }

    // ---------- Custom window frame ----------

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // Let Windows compute the normal frame, then reclaim the title bar so the toolbar sits at
            // the very top. Keeping the side/bottom frame preserves resizing, snapping and the shadow.
            var before = Marshal.PtrToStructure<Native.RECT>(m.LParam);
            base.WndProc(ref m);
            var after = Marshal.PtrToStructure<Native.RECT>(m.LParam);
            after.Top = WindowState == FormWindowState.Maximized && !_fullscreen
                ? before.Top + (after.Left - before.Left)
                : before.Top;
            Marshal.StructureToPtr(after, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == Native.WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == Native.HTCLIENT)
            {
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                int edge = LogicalToDeviceUnits(6);
                if (WindowState == FormWindowState.Normal && p.Y < edge)
                    m.Result = p.X < edge * 2 ? Native.HTTOPLEFT : p.X > Width - edge * 2 ? Native.HTTOPRIGHT : Native.HTTOP;
                else if (!_fullscreen && p.Y < _toolbar.Bottom)
                    m.Result = Native.HTCAPTION;
            }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _max.Invalidate();
    }

    protected override void OnActivated(EventArgs e) { base.OnActivated(e); InvalidateCaption(); }
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); InvalidateCaption(); }

    void InvalidateCaption() { _min.Invalidate(); _max.Invalidate(); _close.Invalidate(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_tabBar is null) return;
        var cs = ClientSize;
        int toolbarH = _fullscreen ? 0 : LogicalToDeviceUnits(ToolbarHeight);
        int tabsH = _fullscreen || _tabs.Count < 2 ? 0 : LogicalToDeviceUnits(TabBarHeight);
        int sepH = _fullscreen ? 0 : 1;
        _toolbar.SetBounds(0, 0, cs.Width, toolbarH);
        _tabBar.SetBounds(0, toolbarH, cs.Width, tabsH);
        _tabBar.Visible = tabsH > 0;
        _separator.SetBounds(0, toolbarH + tabsH, cs.Width, sepH);
        int top = toolbarH + tabsH + sepH;
        _content.SetBounds(0, top, cs.Width, Math.Max(0, cs.Height - top));
        LayoutToolbar();
    }

    void LayoutToolbar()
    {
        int w = _toolbar.Width, h = _toolbar.Height;
        if (h == 0) return;
        int D(int v) => LogicalToDeviceUnits(v);
        int btn = D(30), y = (h - btn) / 2;

        int capW = D(CaptionWidth);
        _close.SetBounds(w - capW, 0, capW, h);
        _max.SetBounds(w - capW * 2, 0, capW, h);
        _min.SetBounds(w - capW * 3, 0, capW, h);

        int x = D(14);
        _back.SetBounds(x, y, btn, btn); x += btn + D(2);
        _forward.SetBounds(x, y, btn, btn); x += btn + D(8);
        int shieldW = D(58);
        _shield.SetBounds(x, y, shieldW, btn); x += shieldW;
        int leftEdge = x + D(12);

        int right = w - capW * 3 - D(10);
        _menu.SetBounds(right - btn, y, btn, btn);
        _newTab.SetBounds(right - btn * 2 - D(2), y, btn, btn);
        int rightEdge = _newTab.Left - D(12);

        // Centre the field in the window when there is room, like Safari.
        int fieldH = D(34), maxW = D(680);
        int fieldW = Math.Max(0, Math.Min(maxW, rightEdge - leftEdge));
        int fx = Math.Clamp((w - fieldW) / 2, leftEdge, Math.Max(leftEdge, rightEdge - fieldW));
        _address.SetBounds(fx, (h - fieldH) / 2, fieldW, fieldH);
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
        _appliedDns = (_settings.DnsBlocking, _settings.DnsServer);
        await Task.Run(() => ConfigureSecureDns(userDataDir));
        if (_settings.DnsBlocking)
        {
            _dnsHealth = new DnsHealth(_settings.DnsServer);
            _dnsHealth.Changed += UpdateShield;
            _dnsHealth.Start();
        }
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
    /// server. "automatic" mode falls back to the system resolver if that server is unreachable, so
    /// an outage never stops pages loading (DnsHealth warns the user instead). WebView2 ignores the DoH feature
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
                state["dns_over_https"] = new JsonObject { ["mode"] = "automatic", ["templates"] = _settings.DnsServer };
            else if (!state.Remove("dns_over_https"))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, state.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    /// <summary>Rebuilds uBO's scriptlet resources in the background when they are missing or old.</summary>
    async Task RefreshScriptletsAsync()
    {
        if (!AdBlocker.ResourcesStale) return;
        var env = await _env;
        await Task.Delay(TimeSpan.FromSeconds(3)); // let the first page get going first
        var json = await ScriptletConverter.ConvertAsync(env, Handle);
        if (json is not null) _adblock.SetResources(json);
    }

    // ---------- Tabs ----------

    BrowserTab NewTab(string url, bool activate = true, bool editAddress = false, int? index = null)
    {
        var tab = new BrowserTab { Url = url, Title = TitleFor(url) };
        _tabs.Insert(index ?? _tabs.Count, tab);
        _tabBar.Tabs = _tabs;
        if (activate) Activate(tab);
        PerformLayout();
        _tabBar.Invalidate();
        if (editAddress) _address.BeginEdit();
        return tab;
    }

    void Activate(BrowserTab tab)
    {
        if (_active == tab) return;
        var previous = _active;
        _active = tab;
        _tabBar.Active = tab;

        // Tabs are created lazily: a restored tab costs nothing until it is first shown.
        if (tab.View is null) _ = EnsureView(tab, navigate: true);
        else tab.View.Visible = true;
        if (previous?.View is not null) previous.View.Visible = false;

        tab.View?.BringToFront();
        _tabBar.Invalidate();
        UpdateChrome();
        if (!_address.Editing) tab.View?.Focus();
    }

    void CloseTab(BrowserTab tab)
    {
        var i = _tabs.IndexOf(tab);
        if (i < 0) return;
        if (_tabs.Count == 1) { Close(); return; }
        if (!string.IsNullOrEmpty(tab.Url)) _closedTabs.Push((tab.Url, i));
        _tabs.RemoveAt(i);
        if (_active == tab)
        {
            _active = null;
            Activate(_tabs[Math.Min(i, _tabs.Count - 1)]);
        }
        tab.View?.Dispose();
        tab.Favicon?.Dispose();
        PerformLayout();
        _tabBar.Invalidate();
    }

    async Task EnsureView(BrowserTab tab, bool navigate)
    {
        if (tab.View is null)
        {
            var view = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Theme.Current.Dark ? Color.FromArgb(30, 30, 30) : Color.White,
                Visible = tab == _active,
            };
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
        HookAccelerators(view);

        var s = core.Settings;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsReputationCheckingRequired = false; // SmartScreen sends visited URLs to Microsoft.
        s.IsStatusBarEnabled = true;
        core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(CosmeticScript);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);

        core.WebResourceRequested += (_, e) => OnResourceRequested(tab, env, e);
        core.WebMessageReceived += (_, e) => OnWebMessage(tab, core, e);
        core.NavigationStarting += (_, e) => OnNavigationStarting(tab, core, e);
        core.FrameNavigationStarting += (_, e) =>
        {
            // Embedded frames (a YouTube player on another site, say) get their host's scriptlets
            // too. Frames can't be held back, so this is best effort for the first frame load.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var frameUri) && NeedsScriptlets(tab, frameUri, out var script))
                _ = RegisterScriptletsAsync(tab, core, frameUri.Host, script);
        };
        core.ContentLoading += (_, _) => { tab.Progress = Math.Max(tab.Progress, 0.45f); };
        core.NavigationCompleted += (_, _) =>
        {
            tab.Loading = false;
            tab.Progress = 1;
            if (tab == _active) UpdateChrome();
        };
        core.SourceChanged += (_, _) => { tab.Url = core.Source; if (tab == _active) UpdateChrome(); };
        core.HistoryChanged += (_, _) => { if (tab == _active) UpdateChrome(); };
        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? TitleFor(core.Source) : core.DocumentTitle;
            _tabBar.Invalidate();
            if (tab == _active) Text = tab.Title + " â€” Brisk";
        };
        core.FaviconChanged += async (_, _) =>
        {
            Image? icon = null;
            try
            {
                if (!string.IsNullOrEmpty(core.FaviconUri))
                {
                    using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                    if (stream is not null && stream.Length > 0)
                    {
                        using var img = Image.FromStream(stream);
                        icon = new Bitmap(img);
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or COMException or InvalidOperationException) { }
            tab.Favicon?.Dispose();
            tab.Favicon = icon;
            _tabBar.Invalidate();
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

        // Scriptlets have to be in place before the page's own scripts run. The first time this tab
        // visits a host that has them, hold the navigation until they are registered.
        if (uri is not null && NeedsScriptlets(tab, uri, out var script))
        {
            e.Cancel = true;
            _ = RegisterScriptletsAsync(tab, core, uri.Host, script).ContinueWith(
                _ => core.Navigate(uri.AbsoluteUri), TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }

        tab.Blocked = 0;
        tab.Loading = true;
        tab.Progress = 0.08f;
        if (tab == _active)
        {
            UpdateChrome();
            _progressTimer.Start();
        }
    }

    bool NeedsScriptlets(BrowserTab tab, Uri uri, out string script)
    {
        script = "";
        if (!tab.AdblockOn || !_adblock.IsReady || uri.Scheme is not ("http" or "https") || tab.Scriptlets.ContainsKey(uri.Host))
            return false;
        script = _adblock.InjectedScript(uri.AbsoluteUri);
        if (script.Length == 0) tab.Scriptlets[uri.Host] = null;
        return script.Length > 0;
    }

    static async Task RegisterScriptletsAsync(BrowserTab tab, CoreWebView2 core, string host, string script)
    {
        tab.Scriptlets[host] = null; // reserve, so a second navigation doesn't register twice
        // Document-created scripts run in every frame of every later document, so scope this one to its host.
        var wrapped = $"if (location.hostname === {JsonSerializer.Serialize(host)}) {{\n{script}\n}}";
        try { tab.Scriptlets[host] = await core.AddScriptToExecuteOnDocumentCreatedAsync(wrapped); }
        catch (COMException) { tab.Scriptlets.Remove(host); }
    }

    /// <summary>Unregisters scriptlets for a site in every tab, e.g. after ad blocking was turned off there.</summary>
    void ForgetScriptlets(string siteKey)
    {
        foreach (var tab in _tabs)
        {
            foreach (var host in tab.Scriptlets.Keys.Where(h => Settings.SiteKey(new Uri("https://" + h)) == siteKey).ToList())
            {
                if (tab.Scriptlets[host] is { } id) tab.View?.CoreWebView2?.RemoveScriptToExecuteOnDocumentCreated(id);
                tab.Scriptlets.Remove(host);
            }
        }
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
                    // The page only needs the hiding rules; scriptlets are injected separately.
                    var cosmetics = JsonNode.Parse(_adblock.UrlCosmetics(url.GetString() ?? "")) as JsonObject ?? new JsonObject();
                    cosmetics.Remove("injected_script");
                    core.PostWebMessageAsJson("{\"type\":\"brisk-init\",\"r\":" + cosmetics.ToJsonString() + "}");
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
        ForgetScriptlets(key);
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
        _address.SetUrl(_active.Url);
        _address.Loading = _active.Loading;
        _back.Enabled = _active.View?.CanGoBack == true;
        _forward.Enabled = _active.View?.CanGoForward == true;
        Text = _active.Title + " â€” Brisk";
        UpdateShield();
        if (_active.Loading || _address.Progress > 0) _progressTimer.Start();
    }

    void UpdateShield()
    {
        if (_active is null) return;
        _shield.SetState(_active.AdblockOn, _active.Blocked, _dnsHealth?.Unreachable == true);
        _tips.SetToolTip(_shield, _dnsHealth?.Unreachable == true
            ? $"{_settings.DnsName} can't be reached, so Brisk is using your normal DNS for now. The main ad blocker still works."
            : "Ad blocking for this site. Click to turn it off or on.");
    }

    /// <summary>Eases the field's progress bar toward the tab's real progress, then fades it out.</summary>
    void AnimateProgress()
    {
        if (_active is null) { _progressTimer.Stop(); return; }
        var tab = _active;
        if (tab.Loading)
        {
            // Creep toward 90% while waiting so the bar never looks stuck.
            tab.Progress = Math.Min(0.9f, tab.Progress + (0.9f - tab.Progress) * 0.012f);
            _address.Progress += (tab.Progress - _address.Progress) * 0.25f;
        }
        else if (_address.Progress > 0)
        {
            _address.Progress = _address.Progress >= 0.995f ? 0 : _address.Progress + (1 - _address.Progress) * 0.35f;
        }
        else
        {
            _progressTimer.Stop();
        }
    }

    void Go(string input)
    {
        var url = ToUrl(input);
        if (_active is null) { NewTab(url); return; }
        _active.Url = url;
        _address.SetUrl(url);
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

    void UpdatePlaceholder() =>
        _address.Placeholder = _settings.CurrentSearchEngine is { } e
            ? $"Search {e.Name} or enter website name"
            : "Search or enter website name";

    static string TitleFor(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Length > 0 ? Settings.SiteKey(u) : "New Tab";

    void ShowMenu()
    {
        _menuStrip.Items.Clear();
        _menuStrip.Items.Add(new ToolStripMenuItem("New Tab", null, (_, _) => NewTab(_settings.HomePage, editAddress: true))
        { ShortcutKeyDisplayString = "Ctrl+T" });
        _menuStrip.Items.Add(new ToolStripSeparator());

        var site = _active is not null && Uri.TryCreate(_active.Url, UriKind.Absolute, out var u) ? Settings.SiteKey(u) : "";
        if (site.Length > 0)
        {
            var item = new ToolStripMenuItem($"Block Ads on {site}") { Checked = _active!.AdblockOn };
            item.Click += (_, _) => ToggleAdblockForSite();
            _menuStrip.Items.Add(item);
        }

        var dns = new ToolStripMenuItem("Ad-Blocking DNS");
        void AddDns(string name, bool on, string server)
        {
            bool current = _settings.DnsBlocking == on && (!on || _settings.DnsServer == server);
            var item = new ToolStripMenuItem(name) { Checked = current };
            item.Click += (_, _) =>
            {
                _settings.DnsBlocking = on;
                if (on) _settings.DnsServer = server;
                _settings.Save();
            };
            dns.DropDownItems.Add(item);
        }
        AddDns("Off", false, "");
        foreach (var provider in Settings.DnsProviders) AddDns(provider.Name, true, provider.Url);
        _menuStrip.Items.Add(dns);
        if ((_settings.DnsBlocking, _settings.DnsServer) != _appliedDns && (_settings.DnsBlocking || _appliedDns.On))
            _menuStrip.Items.Add(new ToolStripMenuItem("Restart Brisk to Apply the DNS Change") { Enabled = false });
        else if (_dnsHealth?.Unreachable == true)
            _menuStrip.Items.Add(new ToolStripMenuItem($"{_settings.DnsName} Is Unreachable, Using Normal DNS") { Enabled = false });
        _menuStrip.Items.Add("Update Filter Lists", null, (_, _) => _adblock.UpdateLists());
        _menuStrip.Items.Add(new ToolStripSeparator());

        var engines = new ToolStripMenuItem("Search Engine");
        foreach (var engine in Settings.SearchEngines)
        {
            var item = new ToolStripMenuItem(engine.Name) { Checked = _settings.CurrentSearchEngine == engine };
            item.Click += (_, _) => { _settings.UseSearchEngine(engine); UpdatePlaceholder(); };
            engines.DropDownItems.Add(item);
        }
        _menuStrip.Items.Add(engines);

        if (_settings.AdblockDisabledSites.Count > 0)
        {
            var allowed = new ToolStripMenuItem("Sites with Ad Blocking Off");
            foreach (var s in _settings.AdblockDisabledSites.Order())
            {
                allowed.DropDownItems.Add($"Turn Back On for {s}", null, (_, _) =>
                {
                    _settings.AdblockDisabledSites.Remove(s);
                    _settings.Save();
                    ForgetScriptlets(s);
                    foreach (var t in _tabs.Where(t => TitleFor(t.Url) == s)) { t.AdblockOn = true; t.View?.Reload(); }
                    UpdateShield();
                });
            }
            _menuStrip.Items.Add(allowed);
        }

        _menuStrip.Items.Add(new ToolStripSeparator());
        _menuStrip.Items.Add("About Brisk", null, (_, _) => { using var about = new AboutForm(Icon); about.ShowDialog(this); });
        var size = _menuStrip.GetPreferredSize(Size.Empty);
        _menuStrip.Show(_menu, new Point(_menu.Width - size.Width, _menu.Height + LogicalToDeviceUnits(6)));
    }

    void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        _fullscreen = on;
        if (on)
        {
            _preFullscreenState = WindowState;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _preFullscreenState;
            Native.StyleWindow(Handle, Theme.Current.Dark);
        }
        PerformLayout();
    }

    // ---------- Keyboard ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>
    /// While a page has focus, key presses go to the browser engine rather than to this window, so
    /// shortcuts are also picked up from the engine's accelerator-key event. The WinForms control
    /// keeps its controller private; it is the only way to reach that event.
    /// </summary>
    void HookAccelerators(WebView2 view)
    {
        var field = typeof(WebView2).GetField("_coreWebView2Controller", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(view) is not CoreWebView2Controller controller) return;
        controller.AcceleratorKeyPressed += (_, e) =>
        {
            if (e.Handled || e.KeyEventKind is not (CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown))
                return;
            if (HandleShortcut((Keys)e.VirtualKey | ModifierKeys)) e.Handled = true;
        };
    }

    bool HandleShortcut(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.T: NewTab(_settings.HomePage, editAddress: true); return true;
            case Keys.Control | Keys.W:
            case Keys.Control | Keys.F4: if (_active is not null) CloseTab(_active); return true;
            case Keys.Control | Keys.Shift | Keys.T: ReopenClosedTab(); return true;
            case Keys.Control | Keys.L:
            case Keys.Alt | Keys.D:
            case Keys.F6: _address.BeginEdit(); return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.PageDown: CycleTab(1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab:
            case Keys.Control | Keys.PageUp: CycleTab(-1); return true;
            case Keys.F5:
            case Keys.Control | Keys.R: _active?.View?.Reload(); return true;
            case Keys.Alt | Keys.Left: _back.PerformClick(); return true;
            case Keys.Alt | Keys.Right: _forward.PerformClick(); return true;
            case Keys.F11: SetFullscreen(!_fullscreen); return true;
        }
        if (keyData >= (Keys.Control | Keys.D1) && keyData <= (Keys.Control | Keys.D9) && _tabs.Count > 0)
        {
            int n = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
            Activate(n == 8 ? _tabs[^1] : _tabs[Math.Min(n, _tabs.Count - 1)]);
            return true;
        }
        return false;
    }

    void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0) return;
        var (url, index) = _closedTabs.Pop();
        NewTab(url, index: Math.Min(index, _tabs.Count));
    }

    // ---------- Alt + mouse wheel switches tabs ----------

    // A low-level mouse hook sees the wheel before the page does, including over iframes, video
    // players and PDFs, which an in-page script would miss. Ctrl + wheel is left alone for zooming.
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public Point pt; public int mouseData, flags, time; public IntPtr extra; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    const int WH_MOUSE_LL = 14, WM_MOUSEWHEEL = 0x020A, VK_MENU = 0x12;
    HookProc? _wheelProc;
    IntPtr _wheelHook;
    int _wheelAccumulated;
    bool _altMasked;

    void InstallWheelHook()
    {
        _wheelProc = WheelHook;
        _wheelHook = SetWindowsHookEx(WH_MOUSE_LL, _wheelProc, GetModuleHandle(null), 0);
    }

    IntPtr WheelHook(int code, IntPtr wParam, IntPtr lParam)
    {
        // Keys typed into a page go to the engine's process, so this thread's keyboard state may not
        // know Alt is down; ask for the physical key state instead.
        bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        if (!alt) _altMasked = false;
        if (code >= 0 && (int)wParam == WM_MOUSEWHEEL && alt)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (ActiveForm == this && GetAncestor(WindowFromPoint(info.pt), 2 /* GA_ROOT */) == Handle)
            {
                if (!_altMasked)
                {
                    // Releasing Alt on its own would put the window into menu mode and steal
                    // keyboard focus from the page. A harmless unassigned key press in between
                    // (VK 0xE8) tells Windows the Alt press was part of a combination.
                    keybd_event(0xE8, 0, 0, IntPtr.Zero);
                    keybd_event(0xE8, 0, 2 /* KEYUP */, IntPtr.Zero);
                    _altMasked = true;
                }
                // Touchpads send many small deltas; switch once per full notch (120).
                _wheelAccumulated += (short)(info.mouseData >> 16);
                while (Math.Abs(_wheelAccumulated) >= 120)
                {
                    int dir = _wheelAccumulated > 0 ? -1 : 1; // wheel up = previous tab
                    _wheelAccumulated -= Math.Sign(_wheelAccumulated) * 120;
                    BeginInvoke(() => CycleTab(dir));
                }
                return 1; // swallow it so the page doesn't also scroll
            }
        }
        return CallNextHookEx(_wheelHook, code, wParam, lParam);
    }

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
        if (disposing)
        {
            Theme.Changed -= ApplyTheme;
            if (_wheelHook != IntPtr.Zero) UnhookWindowsHookEx(_wheelHook);
            _progressTimer.Dispose();
            _dnsHealth?.Dispose();
            _adblock.Dispose();
        }
        base.Dispose(disposing);
    }

    static string ReadResource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
