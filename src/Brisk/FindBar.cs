namespace Brisk;

/// <summary>
/// Safari-style find banner under the toolbar: a search field, the match count, previous/next and Done.
/// The searching itself is the engine's (CoreWebView2.Find), with its own highlighting suppressed
/// in favour of this bar.
/// </summary>
sealed class FindBar : ChromeControl
{
    readonly Panel _field = new();
    readonly TextBox _box = new() { BorderStyle = BorderStyle.None, PlaceholderText = "Find on page", AccessibleName = "Find on page" };
    readonly GlyphButton _prev, _next;
    readonly PushButton _done = new("Done");
    string _count = "";

    public event Action<string>? QueryChanged;
    public event Action? Next, Previous, Closed;

    public FindBar(ToolTip tips)
    {
        _prev = new GlyphButton(Up, "Previous Match (Shift+Enter)", tips) { GlyphSize = 14 };
        _next = new GlyphButton(Down, "Next Match (Enter)", tips) { GlyphSize = 14 };
        _prev.Click += (_, _) => Previous?.Invoke();
        _next.Click += (_, _) => Next?.Invoke();
        _done.Click += (_, _) => Closed?.Invoke();
        _box.TextChanged += (_, _) => QueryChanged?.Invoke(_box.Text);
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { (e.Shift ? Previous : Next)?.Invoke(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { Closed?.Invoke(); e.SuppressKeyPress = true; }
        };
        _field.Paint += PaintField;
        _field.Controls.Add(_box);
        Controls.AddRange([_field, _prev, _next, _done]);
        Theme.Changed += ApplyTheme;
        ApplyTheme();
    }

    public string Query => _box.Text;

    /// <summary>"3 of 12", "Not found" or nothing.</summary>
    public string Count { set { _count = value; _field.Invalidate(); } }

    public void FocusField()
    {
        _box.Focus();
        _box.SelectAll();
    }

    void ApplyTheme()
    {
        BackColor = T.Toolbar;
        _field.BackColor = T.FieldEditing;
        _box.BackColor = T.FieldEditing;
        _box.ForeColor = T.Text;
        Invalidate(true);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int D(int v) => LogicalToDeviceUnits(v);
        int h = Height, fieldW = Math.Min(D(360), Width - D(220)), fieldH = D(26);
        int x = Math.Max(D(12), (Width - fieldW) / 2 - D(60));
        _field.SetBounds(x, (h - fieldH) / 2, Math.Max(D(120), fieldW), fieldH);
        using (var round = Shapes.Rounded(new RectangleF(0, 0, _field.Width, _field.Height), Dp(6)))
        {
            _field.Region?.Dispose();
            _field.Region = new Region(round);
        }
        _box.SetBounds(D(28), (fieldH - _box.PreferredHeight) / 2 + D(1), _field.Width - D(28) - D(90), _box.PreferredHeight);
        int bx = _field.Right + D(6), btn = D(26);
        _prev.SetBounds(bx, (h - btn) / 2, btn, btn);
        _next.SetBounds(bx + btn + D(2), (h - btn) / 2, btn, btn);
        _done.SetBounds(_next.Right + D(10), (h - btn) / 2, D(64), btn);
    }

    void PaintField(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Shapes.Smooth(g);
        float s = Dp(13);
        Glyphs.Search(g, new RectangleF(Dp(9), (_field.Height - s) / 2, s, s), T.Secondary, Dp(1.6f));
        if (_count.Length > 0)
            TextRenderer.DrawText(g, _count, Font, new Rectangle(0, 0, _field.Width - (int)Dp(10), _field.Height), T.Secondary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(T.Toolbar);
        using var p = new Pen(T.Separator);
        e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        // Round the field's corners against the bar.
        Shapes.Smooth(e.Graphics);
        using var outline = Shapes.Rounded(new RectangleF(_field.Left - 0.5f, _field.Top - 0.5f, _field.Width, _field.Height), Dp(6));
        using var pen = new Pen(T.Separator);
        e.Graphics.DrawPath(pen, outline);
    }

    static void Up(Graphics g, RectangleF r, Color c, float w)
    {
        using var p = new Pen(c, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        g.DrawLines(p, [new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.66f), new PointF(r.X + r.Width * 0.5f, r.Y + r.Height * 0.34f), new PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.66f)]);
    }

    static void Down(Graphics g, RectangleF r, Color c, float w)
    {
        using var p = new Pen(c, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        g.DrawLines(p, [new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.34f), new PointF(r.X + r.Width * 0.5f, r.Y + r.Height * 0.66f), new PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.34f)]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= ApplyTheme;
        base.Dispose(disposing);
    }
}
