namespace Brisk;

/// <summary>
/// Ctrl+Shift+A: a small pop-up for finding an open tab by title or address. Typing filters the
/// list, the arrow keys move, Enter switches to the tab and Escape (or clicking elsewhere) closes it.
/// </summary>
sealed class TabSearch : Form
{
    public sealed record Entry(object Tab, string Title, string Url, Image? Icon, bool Active);

    readonly Panel _field = new();
    readonly TextBox _box = new() { BorderStyle = BorderStyle.None, PlaceholderText = "Search tabs", AccessibleName = "Search tabs" };
    readonly Font _titleFont = Theme.Ui(9.5f), _urlFont = Theme.Ui(8.25f);
    readonly List<Entry> _all;
    List<Entry> _shown = new();
    int _selected, _hover = -1, _first;

    const int MaxRows = 10;
    public event Action<object>? Chosen;

    public TabSearch(List<Entry> tabs)
    {
        _all = tabs;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
        var t = Theme.Current;
        BackColor = t.MenuBack;
        _field.BackColor = t.FieldEditing;
        _box.BackColor = t.FieldEditing;
        _box.ForeColor = t.Text;
        _box.Font = Theme.Ui(10.5f);
        _box.TextChanged += (_, _) => Filter();
        _field.Paint += (_, e) =>
        {
            Shapes.Smooth(e.Graphics);
            float s = LogicalToDeviceUnits(14);
            Glyphs.Search(e.Graphics, new RectangleF(LogicalToDeviceUnits(10), (_field.Height - s) / 2, s, s), Theme.Current.Secondary, LogicalToDeviceUnits(2) * 0.8f);
        };
        _field.Controls.Add(_box);
        Controls.Add(_field);
        Filter();
    }

    int D(int v) => LogicalToDeviceUnits(v);
    int RowH => D(44);
    int ListTop => D(8 + 34 + 8);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, Theme.Current.Dark);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    /// <summary>Opens centred under the owner's toolbar.</summary>
    public void ShowUnder(Form owner, int top)
    {
        int w = D(520);
        var area = owner.RectangleToScreen(owner.ClientRectangle);
        Location = new Point(area.X + (area.Width - w) / 2, area.Y + top);
        Width = w;
        Resize();
        Show(owner);
        Activate();
        _box.Focus();
    }

    void Filter()
    {
        var q = _box.Text.Trim();
        _shown = _all.Where(e => q.Length == 0 ||
                                 e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                 e.Url.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _selected = _shown.FindIndex(e => e.Active && q.Length == 0);
        if (_selected < 0) _selected = 0;
        _first = 0;
        Scroll(0);
        Resize();
        Invalidate();
    }

    new void Resize()
    {
        int rows = Math.Max(1, Math.Min(MaxRows, _shown.Count));
        Height = ListTop + rows * RowH + D(8);
        _field.SetBounds(D(10), D(8), Width - D(20), D(34));
        _box.SetBounds(D(32), (D(34) - _box.PreferredHeight) / 2 + D(1), _field.Width - D(42), _box.PreferredHeight);
        using var round = Shapes.Rounded(new RectangleF(0, 0, _field.Width, _field.Height), D(8));
        _field.Region?.Dispose();
        _field.Region = new Region(round);
    }

    new void Scroll(int delta)
    {
        if (_shown.Count == 0) return;
        _selected = Math.Clamp(_selected + delta, 0, _shown.Count - 1);
        if (_selected < _first) _first = _selected;
        if (_selected >= _first + MaxRows) _first = _selected - MaxRows + 1;
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Down: Scroll(1); return true;
            case Keys.Up: Scroll(-1); return true;
            case Keys.PageDown: Scroll(MaxRows); return true;
            case Keys.PageUp: Scroll(-MaxRows); return true;
            case Keys.Enter: Choose(_selected); return true;
            case Keys.Escape: Close(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void Choose(int i)
    {
        if (i < 0 || i >= _shown.Count) return;
        var tab = _shown[i].Tab;
        Close();
        Chosen?.Invoke(tab);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(Close);
    }

    int RowAt(Point p)
    {
        if (p.Y < ListTop) return -1;
        int i = _first + (p.Y - ListTop) / RowH;
        return i < _shown.Count && i < _first + MaxRows ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = RowAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int step = e.Delta > 0 ? -1 : 1;
        int maxFirst = Math.Max(0, _shown.Count - MaxRows);
        _first = Math.Clamp(_first + step, 0, maxFirst);
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Choose(RowAt(e.Location));
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = Theme.Current;
        g.Clear(t.MenuBack);
        Shapes.Smooth(g);
        if (_shown.Count == 0)
        {
            TextRenderer.DrawText(g, "No matching tabs", _urlFont, new Rectangle(0, ListTop, Width, RowH), t.Secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        int pad = D(8);
        for (int i = _first; i < Math.Min(_shown.Count, _first + MaxRows); i++)
        {
            var entry = _shown[i];
            var r = new Rectangle(pad, ListTop + (i - _first) * RowH, Width - pad * 2, RowH);
            bool sel = i == _selected;
            if (sel || i == _hover)
            {
                using var path = Shapes.Rounded(r, D(6));
                using var b = new SolidBrush(sel ? t.Accent : t.Hover);
                g.FillPath(b, path);
            }
            int icon = D(16), ix = r.X + D(10), iy = r.Y + (r.Height - icon) / 2;
            if (entry.Icon is not null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(entry.Icon, ix, iy, icon, icon);
            }
            else
            {
                using var b = new SolidBrush(sel ? Color.FromArgb(90, 255, 255, 255) : t.Field);
                using var path = Shapes.Rounded(new RectangleF(ix, iy, icon, icon), D(4));
                g.FillPath(b, path);
            }
            int tx = ix + icon + D(12), tw = r.Right - tx - D(12);
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, entry.Title, _titleFont, new Rectangle(tx, r.Y + D(5), tw, D(19)), sel ? Color.White : t.Text, flags);
            TextRenderer.DrawText(g, entry.Url, _urlFont, new Rectangle(tx, r.Y + D(23), tw, D(16)),
                sel ? Color.FromArgb(220, 234, 255) : t.Secondary, flags);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _titleFont.Dispose(); _urlFont.Dispose(); }
        base.Dispose(disposing);
    }
}
