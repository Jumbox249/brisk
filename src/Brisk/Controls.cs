using System.Drawing.Drawing2D;

namespace Brisk;

static class Theme
{
    public static readonly Color Strip = Color.FromArgb(232, 232, 236);
    public static readonly Color Toolbar = Color.FromArgb(249, 249, 251);
    public static readonly Color Hover = Color.FromArgb(220, 220, 226);
    public static readonly Color Text = Color.FromArgb(32, 32, 36);
    public static readonly Color Muted = Color.FromArgb(110, 110, 118);
    public static readonly Color ShieldOn = Color.FromArgb(46, 160, 67);
    public static readonly Color ShieldOff = Color.FromArgb(150, 150, 158);

    public static readonly string IconFontName =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
}

/// <summary>Flat toolbar button showing a Segoe icon glyph.</summary>
sealed class IconButton : Control
{
    bool _hover;

    public IconButton(string glyph, string tooltip, ToolTip tips)
    {
        Text = glyph;
        Font = new Font(Theme.IconFontName, 10f);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        tips.SetToolTip(this, tooltip);
    }

    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Toolbar);
        if (_hover && Enabled)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Theme.Hover);
            using var path = Rounded(ClientRectangle, LogicalToDeviceUnits(6));
            e.Graphics.FillPath(b, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? Theme.Text : Theme.ShieldOff,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        r.Width -= 1; r.Height -= 1;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Shield in the toolbar: green with a blocked-request count when ad blocking is on for the site.</summary>
sealed class ShieldButton : Control
{
    bool _hover;
    bool _on = true;
    int _count;

    public ShieldButton()
    {
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void SetState(bool on, int count)
    {
        if (on == _on && count == _count) return;
        _on = on;
        _count = count;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Toolbar);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover)
        {
            using var hb = new SolidBrush(Theme.Hover);
            using var hp = IconButton.Rounded(ClientRectangle, LogicalToDeviceUnits(6));
            g.FillPath(hb, hp);
        }

        // Shield outline: flat top, curved sides meeting at a point.
        float s = LogicalToDeviceUnits(16);
        float x = LogicalToDeviceUnits(8), y = (Height - s * 1.1f) / 2;
        using var shield = new GraphicsPath();
        shield.AddLine(x, y + s * 0.15f, x + s / 2, y);
        shield.AddLine(x + s / 2, y, x + s, y + s * 0.15f);
        shield.AddBezier(x + s, y + s * 0.15f, x + s, y + s * 0.75f, x + s * 0.7f, y + s * 0.95f, x + s / 2, y + s * 1.1f);
        shield.AddBezier(x + s / 2, y + s * 1.1f, x + s * 0.3f, y + s * 0.95f, x, y + s * 0.75f, x, y + s * 0.15f);
        shield.CloseFigure();

        var color = _on ? Theme.ShieldOn : Theme.ShieldOff;
        using (var b = new SolidBrush(color)) g.FillPath(b, shield);
        if (!_on)
        {
            using var slash = new Pen(Color.White, LogicalToDeviceUnits(2));
            g.DrawLine(slash, x + s * 0.25f, y + s * 0.3f, x + s * 0.75f, y + s * 0.8f);
        }

        var label = _on ? (_count > 999 ? "999+" : _count.ToString()) : "off";
        var textRect = new Rectangle((int)(x + s + LogicalToDeviceUnits(4)), 0, Width - (int)(x + s), Height);
        TextRenderer.DrawText(g, label, Font, textRect, _on ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>One tab in the tab strip. Middle-click or the × closes it.</summary>
sealed class TabChip : Control
{
    bool _hover;
    bool _active;
    readonly Font _iconFont = new(Theme.IconFontName, 7f);

    public event Action? CloseClicked;

    public TabChip()
    {
        Font = new Font("Segoe UI", 9f);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { if (_active != value) { _active = value; Invalidate(); } }
    }

    public void SetTitle(string title)
    {
        if (Text == title) return;
        Text = title;
        Invalidate();
    }

    Rectangle CloseRect
    {
        get
        {
            int size = LogicalToDeviceUnits(20);
            return new Rectangle(Width - size - LogicalToDeviceUnits(6), (Height - size) / 2, size, size);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location)))
        {
            CloseClicked?.Invoke();
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !CloseRect.Contains(e.Location)) OnClick(e);
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Strip);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bg = _active ? Theme.Toolbar : _hover ? Theme.Hover : Theme.Strip;
        if (bg != Theme.Strip)
        {
            var r = new Rectangle(0, LogicalToDeviceUnits(4), Width, Height - LogicalToDeviceUnits(4) + 8);
            using var b = new SolidBrush(bg);
            using var p = IconButton.Rounded(r, LogicalToDeviceUnits(8));
            g.FillPath(b, p);
        }

        int pad = LogicalToDeviceUnits(12);
        var textRect = new Rectangle(pad, LogicalToDeviceUnits(4), CloseRect.Left - pad, Height - LogicalToDeviceUnits(4));
        TextRenderer.DrawText(g, Text, Font, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        if (_active || _hover)
        {
            var cr = CloseRect;
            cr.Offset(0, LogicalToDeviceUnits(2));
            TextRenderer.DrawText(g, "", _iconFont, cr, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
