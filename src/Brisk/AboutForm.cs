namespace Brisk;

/// <summary>A small, centred About panel in the style of a Mac app's About window.</summary>
sealed class AboutForm : Form
{
    readonly Bitmap? _icon;

    public AboutForm(Icon? appIcon)
    {
        var t = Theme.Current;
        Text = "About Brisk";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        BackColor = t.Toolbar;
        ClientSize = new Size(LogicalToDeviceUnits(320), LogicalToDeviceUnits(330));
        DoubleBuffered = true;
        if (appIcon is not null)
        {
            Icon = appIcon;
            int s = LogicalToDeviceUnits(112);
            using var big = new Icon(appIcon, 256, 256);
            using var bmp = big.ToBitmap();
            _icon = new Bitmap(bmp, s, s);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, Theme.Current.Dark);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Escape or Keys.Enter) Close();
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = Theme.Current;
        Shapes.Smooth(g);
        int y = LogicalToDeviceUnits(28);
        if (_icon is not null)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_icon, (ClientSize.Width - _icon.Width) / 2, y);
            y += _icon.Height + LogicalToDeviceUnits(14);
        }

        void Line(string text, Font font, Color color, int after)
        {
            var size = TextRenderer.MeasureText(g, text, font, new Size(ClientSize.Width - LogicalToDeviceUnits(40), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, font,
                new Rectangle(LogicalToDeviceUnits(20), y, ClientSize.Width - LogicalToDeviceUnits(40), size.Height),
                color, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            y += size.Height + after;
        }

        using var title = new Font(Theme.DisplayFontName, 15f, FontStyle.Bold);
        using var body = Theme.Ui(9f);
        using var small = Theme.Ui(8f);
        Line("Brisk", title, t.Text, LogicalToDeviceUnits(4));
        Line("Version " + Application.ProductVersion.Split('+')[0], body, t.Secondary, LogicalToDeviceUnits(14));
        Line("A small, fast, private browser for Windows.", body, t.Text, LogicalToDeviceUnits(10));
        Line("Ad blocking by Brave's adblock-rust with EasyList, EasyPrivacy and Peter Lowe's list, backed by AdGuard DNS.",
            small, t.Secondary, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon?.Dispose();
        base.Dispose(disposing);
    }
}
