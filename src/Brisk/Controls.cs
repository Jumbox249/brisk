using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Brisk;

/// <summary>Light and dark palettes, following the Windows app theme.</summary>
sealed class Theme
{
    public required Color Toolbar, TabBar, TabActive, TabHover, Field, FieldEditing, Text, Secondary,
        Glyph, Separator, Accent, Hover, Pressed, MenuBack, MenuBorder;
    public bool Dark;

    static readonly Theme Light = new()
    {
        Toolbar = Hex(0xF6F6F6), TabBar = Hex(0xE3E3E5), TabActive = Hex(0xF6F6F6), TabHover = Hex(0xDBDBDE),
        Field = Hex(0xE6E6E9), FieldEditing = Hex(0xFFFFFF), Text = Hex(0x1D1D1F), Secondary = Hex(0x86868B),
        Glyph = Hex(0x4A4A4F), Separator = Hex(0xD2D2D7), Accent = Hex(0x007AFF), Hover = Hex(0xE6E6E9),
        Pressed = Hex(0xD9D9DD), MenuBack = Hex(0xF9F9FA), MenuBorder = Hex(0xD0D0D5),
    };

    static readonly Theme DarkTheme = new()
    {
        Dark = true,
        Toolbar = Hex(0x2C2C2E), TabBar = Hex(0x1F1F21), TabActive = Hex(0x2C2C2E), TabHover = Hex(0x262628),
        Field = Hex(0x3A3A3C), FieldEditing = Hex(0x1C1C1E), Text = Hex(0xF5F5F7), Secondary = Hex(0x98989D),
        Glyph = Hex(0xD1D1D6), Separator = Hex(0x0E0E10), Accent = Hex(0x0A84FF), Hover = Hex(0x3A3A3C),
        Pressed = Hex(0x48484A), MenuBack = Hex(0x2C2C2E), MenuBorder = Hex(0x48484A),
    };

    public static Theme Current { get; private set; } = Detect();

    public static event Action? Changed;

    static Theme()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            var next = Detect();
            if (next == Current) return;
            Current = next;
            Changed?.Invoke();
        };
    }

    static Theme Detect()
    {
        var forced = Environment.GetEnvironmentVariable("BRISK_THEME");
        if (forced is "light" or "dark") return forced == "dark" ? DarkTheme : Light;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0 ? DarkTheme : Light;
    }

    static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static readonly string UiFontName =
        FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    public static readonly string DisplayFontName =
        FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";

    public static readonly string CaptionFontName =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new(UiFontName, size, style);
}

/// <summary>Thin, round-capped vector icons in the spirit of SF Symbols.</summary>
static class Glyphs
{
    public delegate void Painter(Graphics g, RectangleF r, Color c, float stroke);

    static Pen P(Color c, float w) => new(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    static PointF At(RectangleF r, float x, float y) => new(r.X + r.Width * x, r.Y + r.Height * y);

    public static void Back(Graphics g, RectangleF r, Color c, float w)
    {
        using var p = P(c, w);
        g.DrawLines(p, [At(r, 0.62f, 0.18f), At(r, 0.30f, 0.5f), At(r, 0.62f, 0.82f)]);
    }

    public static void Forward(Graphics g, RectangleF r, Color c, float w)
    {
        using var p = P(c, w);
        g.DrawLines(p, [At(r, 0.38f, 0.18f), At(r, 0.70f, 0.5f), At(r, 0.38f, 0.82f)]);
    }

    public static void Reload(Graphics g, RectangleF r, Color c, float w)
    {
        using var p = P(c, w);
        // Clockwise arc open at the top right, ending at 12 o'clock with an arrowhead pointing right.
        var box = RectangleF.Inflate(r, -r.Width * 0.14f, -r.Height * 0.14f);
        g.DrawArc(p, box, -30, 300);
        var tip = new PointF(box.X + box.Width * 0.5f + r.Width * 0.06f, box.Y);
        float h = r.Width * 0.2f;
        g.DrawLines(p, [new PointF(tip.X - h, tip.Y - h), tip, new PointF(tip.X - h, tip.Y + h)]);
    }

    public static void Stop(Graphics g, RectangleF r, Color c, float w) => XMark(g, RectangleF.Inflate(r, -r.Width * 0.06f, -r.Height * 0.06f), c, w);

    public static void Plus(Graphics g, RectangleF r, Color c, float w)
    {
        using var p = P(c, w);
        g.DrawLine(p, At(r, 0.5f, 0.18f), At(r, 0.5f, 0.82f));
        g.DrawLine(p, At(r, 0.18f, 0.5f), At(r, 0.82f, 0.5f));
    }

    public static void XMark(Graphics g, RectangleF r, Color c, float w)
    {
        using var p = P(c, w);
        g.DrawLine(p, At(r, 0.25f, 0.25f), At(r, 0.75f, 0.75f));
        g.DrawLine(p, At(r, 0.75f, 0.25f), At(r, 0.25f, 0.75f));
    }

    public static void Ellipsis(Graphics g, RectangleF r, Color c, float w)
    {
        using var b = new SolidBrush(c);
        float d = w * 1.45f;
        foreach (var x in new[] { 0.2f, 0.5f, 0.8f })
        {
            var pt = At(r, x, 0.5f);
            g.FillEllipse(b, pt.X - d / 2, pt.Y - d / 2, d, d);
        }
    }

    public static void Lock(Graphics g, RectangleF r, Color c, float w)
    {
        using var b = new SolidBrush(c);
        using var p = P(c, w);
        var body = new RectangleF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.45f, r.Width * 0.6f, r.Height * 0.47f);
        using var path = Shapes.Rounded(body, r.Width * 0.1f);
        g.FillPath(b, path);
        g.DrawArc(p, r.X + r.Width * 0.32f, r.Y + r.Height * 0.1f, r.Width * 0.36f, r.Height * 0.62f, 180, 180);
        g.DrawLine(p, r.X + r.Width * 0.32f, r.Y + r.Height * 0.41f, r.X + r.Width * 0.32f, r.Y + r.Height * 0.47f);
        g.DrawLine(p, r.X + r.Width * 0.68f, r.Y + r.Height * 0.41f, r.X + r.Width * 0.68f, r.Y + r.Height * 0.47f);
    }

    public static GraphicsPath ShieldPath(RectangleF r)
    {
        var s = new GraphicsPath();
        s.AddBezier(At(r, 0.5f, 0.06f), At(r, 0.36f, 0.16f), At(r, 0.24f, 0.19f), At(r, 0.13f, 0.2f));
        s.AddBezier(At(r, 0.13f, 0.2f), At(r, 0.12f, 0.62f), At(r, 0.26f, 0.82f), At(r, 0.5f, 0.95f));
        s.AddBezier(At(r, 0.5f, 0.95f), At(r, 0.74f, 0.82f), At(r, 0.88f, 0.62f), At(r, 0.87f, 0.2f));
        s.AddBezier(At(r, 0.87f, 0.2f), At(r, 0.76f, 0.19f), At(r, 0.64f, 0.16f), At(r, 0.5f, 0.06f));
        s.CloseFigure();
        return s;
    }

    public static void Shield(Graphics g, RectangleF r, Color c, float w, bool on)
    {
        using var path = ShieldPath(r);
        using var p = P(c, w);
        if (on)
        {
            using var b = new SolidBrush(c);
            g.FillPath(b, path);
        }
        else
        {
            g.DrawPath(p, path);
            g.DrawLine(p, At(r, 0.08f, 0.12f), At(r, 0.92f, 0.92f));
        }
    }
}

static class Shapes
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}

static class Native
{
    public const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1, HTCLIENT = 1, HTCAPTION = 2, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Rounded corners (Windows 11) and dark/light non-client rendering for a window.</summary>
    public static void StyleWindow(IntPtr hwnd, bool dark, bool roundSmall = false)
    {
        int dm = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dm, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        int corner = roundSmall ? 3 : 2;                        // DWMWCP_ROUNDSMALL / DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
    }
}

/// <summary>Base for toolbar controls: hover/press tracking and theme-aware double-buffered painting.</summary>
abstract class ChromeControl : Control
{
    protected bool Hovered, Pressed;

    protected ChromeControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Theme.Changed += Invalidate;
    }

    protected Theme T => Theme.Current;

    protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hovered = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= Invalidate;
        base.Dispose(disposing);
    }

    protected float Dp(float v) => v * DeviceDpi / 96f;
}

/// <summary>Borderless toolbar button with a vector glyph and a soft rounded hover.</summary>
sealed class GlyphButton : ChromeControl
{
    readonly Glyphs.Painter _paint;
    public float GlyphSize = 16;

    public GlyphButton(Glyphs.Painter paint, string tip, ToolTip tips)
    {
        _paint = paint;
        tips.SetToolTip(this, tip);
    }

    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? T.Toolbar);
        Shapes.Smooth(g);
        if (Enabled && (Hovered || Pressed))
        {
            using var b = new SolidBrush(Pressed ? T.Pressed : T.Hover);
            using var path = Shapes.Rounded(new RectangleF(0, 0, Width, Height), Dp(7));
            g.FillPath(b, path);
        }
        float s = Dp(GlyphSize);
        var r = new RectangleF((Width - s) / 2, (Height - s) / 2, s, s);
        var color = Enabled ? T.Glyph : Color.FromArgb(T.Dark ? 90 : 70, T.Glyph);
        _paint(g, r, color, Dp(1.7f));
    }
}

/// <summary>Ad-blocking toggle: filled shield with the number of blocked requests, or a slashed outline when off.</summary>
sealed class ShieldButton : ChromeControl
{
    bool _on = true, _warning;
    int _count;
    readonly Font _font = Theme.Ui(8.5f, FontStyle.Bold);

    /// <param name="warning">Shows an orange dot: the ad-blocking DNS server is unreachable.</param>
    public void SetState(bool on, int count, bool warning = false)
    {
        if (on == _on && count == _count && warning == _warning) return;
        _on = on;
        _count = count;
        _warning = warning;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? T.Toolbar);
        Shapes.Smooth(g);
        if (Hovered || Pressed)
        {
            using var b = new SolidBrush(Pressed ? T.Pressed : T.Hover);
            using var path = Shapes.Rounded(new RectangleF(0, 0, Width, Height), Dp(7));
            g.FillPath(b, path);
        }

        var label = _on ? (_count > 999 ? "999+" : _count.ToString()) : "Off";
        var textSize = TextRenderer.MeasureText(g, label, _font, Size.Empty, TextFormatFlags.NoPadding);
        float s = Dp(15), gap = Dp(5);
        float total = s + gap + textSize.Width;
        float x = (Width - total) / 2;
        var color = _on ? T.Glyph : T.Secondary;
        Glyphs.Shield(g, new RectangleF(x, (Height - s) / 2, s, s), color, Dp(1.5f), _on);
        if (_warning)
        {
            float d = Dp(7);
            using var ring = new SolidBrush(Parent?.BackColor ?? T.Toolbar);
            using var dot = new SolidBrush(Color.FromArgb(255, 149, 0));
            g.FillEllipse(ring, x + s - d * 0.6f - Dp(1.5f), (Height - s) / 2 - Dp(1.5f), d + Dp(3), d + Dp(3));
            g.FillEllipse(dot, x + s - d * 0.6f, (Height - s) / 2, d, d);
        }
        TextRenderer.DrawText(g, label, _font, new Point((int)(x + s + gap), (Height - textSize.Height) / 2), color, TextFormatFlags.NoPadding);
    }
}

/// <summary>Windows caption button (minimise / maximise / close) drawn to sit inside the toolbar.</summary>
sealed class CaptionButton : ChromeControl
{
    public enum Kind { Minimize, Maximize, Close }

    readonly Kind _kind;
    readonly Font _font;

    public CaptionButton(Kind kind)
    {
        _kind = kind;
        _font = new Font(Theme.CaptionFontName, 7.5f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var form = FindForm();
        bool close = _kind == Kind.Close;
        var bg = Parent?.BackColor ?? T.Toolbar;
        if (Hovered || Pressed)
            bg = close ? (Pressed ? Color.FromArgb(241, 112, 122) : Color.FromArgb(232, 17, 35)) : (Pressed ? T.Pressed : T.Hover);
        g.Clear(bg);
        var glyph = _kind switch
        {
            Kind.Minimize => "",
            Kind.Maximize => form?.WindowState == FormWindowState.Maximized ? "" : "",
            _ => "",
        };
        var color = close && (Hovered || Pressed) ? Color.White : T.Text;
        if (form is not null && !form.ContainsFocus && !Hovered) color = T.Secondary;
        TextRenderer.DrawText(g, glyph, _font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>A panel whose empty areas let the window be dragged (they hit-test as the title bar).</summary>
sealed class DragPanel : Panel
{
    public DragPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCHITTEST) { m.Result = Native.HTTRANSPARENT; return; }
        base.WndProc(ref m);
    }
}

/// <summary>
/// Safari-style address field. At rest it shows a centred padlock and the site name; clicking it
/// switches to an editable, left-aligned full URL. A thin progress bar runs along its bottom edge.
/// </summary>
sealed class AddressField : ChromeControl
{
    readonly TextBox _box = new() { BorderStyle = BorderStyle.None };
    string _placeholder = "Search or enter website name";
    readonly Font _font = Theme.Ui(10f);
    readonly GlyphButton _reload;
    string _url = "";
    bool _loading;
    float _progress;

    public event Action<string>? Submitted;
    public event Action? ReloadClicked;

    public AddressField(ToolTip tips)
    {
        _box.Font = _font;
        _box.Visible = false;
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; var text = _box.Text; EndEdit(); Submitted?.Invoke(text); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndEdit(); }
        };
        _box.LostFocus += (_, _) => EndEdit();
        Controls.Add(_box);

        _reload = new GlyphButton((g, r, c, w) => { if (_loading) Glyphs.Stop(g, r, c, w); else Glyphs.Reload(g, r, c, w); }, "Reload (F5)", tips)
        { GlyphSize = 13 };
        _reload.Click += (_, _) => ReloadClicked?.Invoke();
        Controls.Add(_reload);
        Cursor = Cursors.IBeam;
        Theme.Changed += ApplyTheme;
        ApplyTheme();
    }

    void ApplyTheme()
    {
        _box.BackColor = T.FieldEditing;
        _box.ForeColor = T.Text;
        BackColor = T.Field;
        Invalidate();
    }

    public bool Editing => _box.Visible;

    public bool Loading
    {
        get => _loading;
        set { if (_loading != value) { _loading = value; _reload.Invalidate(); } }
    }

    /// <summary>0..1 while loading; any negative value hides the bar.</summary>
    public float Progress
    {
        get => _progress;
        set { if (Math.Abs(_progress - value) > 0.001f) { _progress = value; Invalidate(); } }
    }

    /// <summary>Hint shown when the field is empty, e.g. "Search Google or enter website name".</summary>
    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; _box.PlaceholderText = value; Invalidate(); }
    }

    public void SetUrl(string url)
    {
        _url = url;
        if (!Editing) Invalidate();
    }

    public void BeginEdit()
    {
        _box.Text = _url.StartsWith("about:", StringComparison.Ordinal) ? "" : _url;
        _box.Visible = true;
        _reload.Visible = false;
        LayoutParts();
        _box.Focus();
        _box.SelectAll();
        Invalidate();
    }

    void EndEdit()
    {
        if (!Editing) return;
        _box.Visible = false;
        _reload.Visible = true;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && !Editing) BeginEdit();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        LayoutParts();
    }

    void LayoutParts()
    {
        if (_reload is null) return; // layout runs while the constructor is still adding children
        int pad = (int)Dp(12), btn = Height - (int)Dp(6);
        _reload.SetBounds(Width - btn - (int)Dp(3), (Height - btn) / 2, btn, btn);
        int h = _box.PreferredHeight;
        _box.SetBounds(pad, (Height - h) / 2 + 1, Math.Max(0, Width - pad * 2), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? T.Toolbar);
        Shapes.Smooth(g);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float radius = Dp(8);
        using var path = Shapes.Rounded(rect, radius);

        if (Editing)
        {
            using var fill = new SolidBrush(T.FieldEditing);
            g.FillPath(fill, path);
            using var ring = new Pen(Color.FromArgb(150, T.Accent), Dp(2.5f));
            using var inner = Shapes.Rounded(RectangleF.Inflate(rect, -Dp(1.25f), -Dp(1.25f)), radius - Dp(1.25f));
            g.DrawPath(ring, inner);
        }
        else
        {
            using var fill = new SolidBrush(Hovered ? ControlPaint.Dark(T.Field, T.Dark ? -0.15f : 0.02f) : T.Field);
            g.FillPath(fill, path);
            DrawRestingText(g);
        }

        if (_progress > 0)
        {
            var clip = g.Clip;
            g.SetClip(path);
            using var bar = new SolidBrush(T.Accent);
            g.FillRectangle(bar, 0, Height - Dp(2.5f), Width * Math.Clamp(_progress, 0, 1), Dp(2.5f));
            g.Clip = clip;
        }
    }

    void DrawRestingText(Graphics g)
    {
        string text;
        bool secure = false;
        if (Uri.TryCreate(_url, UriKind.Absolute, out var u) && u.Host.Length > 0)
        {
            text = Settings.SiteKey(u);
            secure = u.Scheme == "https";
        }
        else
        {
            text = _url.Length == 0 || _url.StartsWith("about:", StringComparison.Ordinal) ? _placeholder : _url;
        }

        bool placeholder = text == _placeholder;
        int maxText = Width - (int)Dp(80);
        var size = TextRenderer.MeasureText(g, text, _font, new Size(maxText, Height), TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        size.Width = Math.Min(size.Width, maxText);
        float lockW = secure ? Dp(11) + Dp(6) : 0;
        float x = (Width - (size.Width + lockW)) / 2;
        if (secure)
        {
            float s = Dp(11);
            Glyphs.Lock(g, new RectangleF(x, (Height - s) / 2 - Dp(0.5f), s, s), T.Secondary, Dp(1.4f));
        }
        TextRenderer.DrawText(g, text, _font, new Rectangle((int)(x + lockW), (Height - size.Height) / 2, size.Width, size.Height),
            placeholder ? T.Secondary : T.Text, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= ApplyTheme;
        base.Dispose(disposing);
    }
}

/// <summary>Safari-style tab bar: equal-width tabs that fill the row, favicon + centred title, close button on hover.</summary>
sealed class TabBar : ChromeControl
{
    readonly Font _font = Theme.Ui(9f);
    readonly ToolTip _tips;
    int _hover = -1;
    bool _hoverClose;
    string _tip = "";

    public IReadOnlyList<BrowserTab> Tabs = [];
    public BrowserTab? Active;

    public event Action<BrowserTab>? TabSelected;
    public event Action<BrowserTab>? TabClosed;

    public TabBar(ToolTip tips) => _tips = tips;

    RectangleF TabRect(int i)
    {
        float w = (float)Width / Math.Max(1, Tabs.Count);
        return new RectangleF(i * w, 0, w, Height);
    }

    RectangleF CloseRect(RectangleF tab)
    {
        float s = Dp(18);
        return new RectangleF(tab.X + Dp(7), (Height - s) / 2, s, s);
    }

    int HitTab(Point p)
    {
        for (int i = 0; i < Tabs.Count; i++) if (TabRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = HitTab(e.Location);
        var overClose = i >= 0 && CloseRect(TabRect(i)).Contains(e.Location);
        if (i != _hover || overClose != _hoverClose)
        {
            _hover = i;
            _hoverClose = overClose;
            Invalidate();
            var tip = i >= 0 ? Tabs[i].Title : "";
            if (tip != _tip) { _tip = tip; _tips.SetToolTip(this, tip); }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        _hoverClose = false;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var i = HitTab(e.Location);
        if (i >= 0 && e.Button == MouseButtons.Left && !CloseRect(TabRect(i)).Contains(e.Location)) TabSelected?.Invoke(Tabs[i]);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var i = HitTab(e.Location);
        if (i < 0) return;
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect(TabRect(i)).Contains(e.Location)))
            TabClosed?.Invoke(Tabs[i]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(T.TabBar);
        Shapes.Smooth(g);
        using var sepPen = new Pen(T.Separator, 1);

        int activeIndex = Active is null ? -1 : Tabs.ToList().IndexOf(Active);
        for (int i = 0; i < Tabs.Count; i++)
        {
            var tab = Tabs[i];
            var r = TabRect(i);
            bool active = i == activeIndex, hover = i == _hover;

            if (active || hover)
            {
                using var b = new SolidBrush(active ? T.TabActive : T.TabHover);
                g.FillRectangle(b, r);
            }
            // Hairline dividers between inactive tabs.
            if (i > 0 && !active && i - 1 != activeIndex && !hover && i - 1 != _hover)
                g.DrawLine(sepPen, r.X, Dp(8), r.X, Height - Dp(8));

            float icon = Dp(16), gap = Dp(6);
            float maxText = r.Width - Dp(60) - icon - gap;
            var text = tab.Title;
            var ts = TextRenderer.MeasureText(g, text, _font, new Size((int)Math.Max(0, maxText), Height), TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            float textW = Math.Min(ts.Width, Math.Max(0, maxText));
            float total = icon + gap + textW;
            float x = r.X + (r.Width - total) / 2;
            float y = (Height - icon) / 2;
            if (tab.Favicon is { } fav)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(fav, x, y, icon, icon);
            }
            else
            {
                DrawGlobe(g, new RectangleF(x + Dp(1), y + Dp(1), icon - Dp(2), icon - Dp(2)));
            }
            if (textW > 4)
            {
                TextRenderer.DrawText(g, text, _font, new Rectangle((int)(x + icon + gap), (Height - ts.Height) / 2, (int)textW, ts.Height),
                    active ? T.Text : T.Secondary, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            if (hover || (active && Tabs.Count > 1 && _hover == i))
            {
                var cr = CloseRect(r);
                if (_hoverClose)
                {
                    using var hb = new SolidBrush(T.Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
                    using var hp = Shapes.Rounded(cr, Dp(4));
                    g.FillPath(hb, hp);
                }
                Glyphs.XMark(g, RectangleF.Inflate(cr, -Dp(3), -Dp(3)), T.Secondary, Dp(1.4f));
            }
        }

        // Hairline between the toolbar and the tab bar.
        g.DrawLine(sepPen, 0, 0, Width, 0);
    }

    void DrawGlobe(Graphics g, RectangleF r)
    {
        using var p = new Pen(T.Secondary, Dp(1.2f));
        g.DrawEllipse(p, r);
        g.DrawEllipse(p, r.X + r.Width * 0.3f, r.Y, r.Width * 0.4f, r.Height);
        g.DrawLine(p, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
    }
}

/// <summary>macOS-style menus: rounded accent highlight, quiet separators, check marks without boxes.</summary>
sealed class MacMenuRenderer : ToolStripRenderer
{
    static Theme T => Theme.Current;

    protected override void Initialize(ToolStrip ts)
    {
        base.Initialize(ts);
        ts.Padding = new Padding(0, 5, 0, 5);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(T.MenuBack);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(T.MenuBorder);
        e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        Shapes.Smooth(g);
        float inset = e.Item.Owner?.LogicalToDeviceUnits(5) ?? 5;
        var r = new RectangleF(inset, 0, e.Item.Width - inset * 2, e.Item.Height);
        using var b = new SolidBrush(T.Accent);
        using var path = Shapes.Rounded(r, inset);
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Shortcut text is right-aligned; dim it like macOS does.
        bool shortcut = (e.TextFormat & TextFormatFlags.Right) != 0;
        e.TextColor = !e.Item.Enabled ? T.Secondary : e.Item.Selected ? Color.White : shortcut ? T.Secondary : T.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        Shapes.Smooth(g);
        var r = e.ImageRectangle;
        using var p = new Pen(e.Item.Selected ? Color.White : T.Text, e.Item.Owner!.LogicalToDeviceUnits(2))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(p, [
            new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.55f),
            new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f),
            new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.28f),
        ]);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var g = e.Graphics;
        Shapes.Smooth(g);
        var r = e.ArrowRectangle;
        float s = e.Item!.Owner!.LogicalToDeviceUnits(9);
        Glyphs.Forward(g, new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s),
            e.Item.Selected ? Color.White : T.Secondary, e.Item.Owner.LogicalToDeviceUnits(2));
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int inset = e.Item.Owner?.LogicalToDeviceUnits(12) ?? 12;
        using var p = new Pen(T.Separator);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, inset, y, e.Item.Width - inset, y);
    }
}

/// <summary>A context menu with the macOS look, rounded corners and theme-aware colours.</summary>
sealed class MacMenu : ContextMenuStrip
{
    public MacMenu()
    {
        Renderer = new MacMenuRenderer();
        Font = Theme.Ui(9.5f);
        ShowImageMargin = false;
        ShowCheckMargin = true;
        DropShadowEnabled = false;
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        base.OnOpening(e);
        foreach (ToolStripItem item in Items) Style(item);
    }

    void Style(ToolStripItem item)
    {
        if (item is ToolStripSeparator) { item.Height = LogicalToDeviceUnits(11); return; }
        item.Padding = new Padding(0, LogicalToDeviceUnits(3), LogicalToDeviceUnits(8), LogicalToDeviceUnits(3));
        if (item is ToolStripMenuItem mi && mi.HasDropDownItems && mi.DropDown is ToolStripDropDownMenu dd)
        {
            dd.Renderer = Renderer;
            dd.Font = Font;
            dd.ShowImageMargin = false;
            dd.ShowCheckMargin = dd.Items.OfType<ToolStripMenuItem>().Any(i => i.Checked);
            dd.DropShadowEnabled = false;
            dd.Opened += (_, _) => Native.StyleWindow(dd.Handle, Theme.Current.Dark);
            foreach (ToolStripItem child in dd.Items) Style(child);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        Native.StyleWindow(Handle, Theme.Current.Dark);
        base.OnOpened(e);
    }
}
