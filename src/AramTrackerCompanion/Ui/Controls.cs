using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AramTrackerCompanion.Ui;

internal sealed class LogoMark : Control
{
    private static readonly PointF BallCentre = new(30.5f, 17.5f);
    private const float BallRadius = 12f;

    public LogoMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var unit = Math.Min(Width, Height) / 48f;
        graphics.ScaleTransform(unit, unit);

        DrawTail(graphics);
        DrawBall(graphics);
        DrawGlint(graphics, new PointF(43, 37), 4.6f, 0.74f, Color.FromArgb(191, 230, 255));
        DrawGlint(graphics, new PointF(42.5f, 5.5f), 2.4f, 0.38f, Color.FromArgb(230, 246, 255));
    }

    private static void DrawTail(Graphics graphics)
    {
        using var tail = new GraphicsPath();
        tail.AddBezier(41.2f, 24.6f, 33, 32, 20, 40, 4.5f, 44.5f);
        tail.AddBezier(4.5f, 44.5f, 9, 29, 17, 15.5f, 23.5f, 7.2f);
        tail.CloseFigure();

        using var fade = new LinearGradientBrush(new PointF(36, 13), new PointF(5, 44), Color.Black, Color.Black)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(255, 58, 184, 246), Color.FromArgb(230, 67, 105, 240), Color.FromArgb(0, 106, 60, 240)],
                Positions = [0f, 0.5f, 1f],
            },
            WrapMode = WrapMode.TileFlipXY,
        };
        graphics.FillPath(fade, tail);

        Dot(graphics, 17, 33, 1.3f, Color.FromArgb(217, 255, 255, 255));
        Dot(graphics, 11.4f, 37.6f, 0.9f, Color.FromArgb(153, 255, 255, 255));
        Dot(graphics, 22, 25.6f, 0.9f, Color.FromArgb(179, 255, 255, 255));
    }

    private static void DrawBall(Graphics graphics)
    {
        var ball = new RectangleF(BallCentre.X - BallRadius, BallCentre.Y - BallRadius, BallRadius * 2, BallRadius * 2);

        var light = new PointF(ball.X + (0.36f * ball.Width), ball.Y + (0.3f * ball.Height));
        var reach = 0.8f * ball.Width;

        using var glow = new GraphicsPath();
        glow.AddEllipse(light.X - reach, light.Y - reach, reach * 2, reach * 2);

        using var shade = new PathGradientBrush(glow)
        {
            CenterPoint = light,
            InterpolationColors = new ColorBlend
            {
                // Path gradients run from the edge to the centre.
                Colors = [Color.FromArgb(31, 102, 204), Color.FromArgb(47, 141, 230), Color.FromArgb(134, 211, 250), Color.FromArgb(227, 245, 255), Color.White],
                Positions = [0f, 0.07f, 0.3f, 0.65f, 1f],
            },
        };

        using var sphere = new GraphicsPath();
        sphere.AddEllipse(ball);

        var state = graphics.Save();
        graphics.SetClip(sphere);
        graphics.FillEllipse(shade, ball);
        graphics.Restore(state);

        using var rim = new Pen(Color.FromArgb(140, 29, 95, 196), 0.9f);
        graphics.DrawEllipse(rim, BallCentre.X - 11.55f, BallCentre.Y - 11.55f, 23.1f, 23.1f);

        using var highlight = new Pen(Color.White, 2.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(highlight, 31.29f - 8.88f, 17.58f - 8.88f, 17.76f, 17.76f, 202f, 73f);

        var texture = Color.FromArgb(230, 159, 216, 247);
        Dot(graphics, 34.82f, 22.3f, 1.06f, texture);
        Dot(graphics, 37.58f, 18.1f, 0.77f, texture);
        Dot(graphics, 31.7f, 25.3f, 0.67f, texture);
    }

    private static void DrawGlint(Graphics graphics, PointF centre, float radius, float pinch, Color colour)
    {
        var (x, y) = (centre.X, centre.Y);

        using var star = new GraphicsPath();
        star.AddBezier(x, y - radius, x + pinch, y - pinch, x + pinch, y - pinch, x + radius, y);
        star.AddBezier(x + radius, y, x + pinch, y + pinch, x + pinch, y + pinch, x, y + radius);
        star.AddBezier(x, y + radius, x - pinch, y + pinch, x - pinch, y + pinch, x - radius, y);
        star.AddBezier(x - radius, y, x - pinch, y - pinch, x - pinch, y - pinch, x, y - radius);
        star.CloseFigure();

        using var brush = new SolidBrush(colour);
        graphics.FillPath(brush, star);
    }

    private static void Dot(Graphics graphics, float x, float y, float radius, Color colour)
    {
        using var brush = new SolidBrush(colour);
        graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
    }
}

internal sealed class BrandName : Control
{
    private static readonly Color IceFrom = Color.FromArgb(125, 211, 252);
    private static readonly Color IceTo = Color.FromArgb(96, 165, 250);

    public BrandName()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        Font = Theme.Brand;
        Text = "ARAM Tracker";
        AccessibleName = "ARAM Tracker";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // TextRenderer can't draw with a gradient.
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

        const string first = "ARAM ";
        var left = graphics.MeasureString(first, Font, PointF.Empty, format).Width;
        var second = graphics.MeasureString("Tracker", Font, PointF.Empty, format);

        using var ink = new SolidBrush(Theme.Ink);
        graphics.DrawString(first, Font, ink, 0, 0, format);

        using var ice = new LinearGradientBrush(new RectangleF(left, 0, Math.Max(1, second.Width), Math.Max(1, second.Height)), IceFrom, IceTo, LinearGradientMode.Horizontal);
        graphics.DrawString("Tracker", Font, ice, left, 0, format);
    }
}

internal sealed class PillButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private bool _primary;

    public PillButton(string text, bool primary = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Font = Theme.Strong;
        Cursor = Cursors.Hand;
        Height = 38;
        _primary = primary;
        FitWidth();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary
    {
        get => _primary;
        set
        {
            _primary = value;
            Invalidate();
        }
    }

    public void FitWidth() => Width = TextRenderer.MeasureText(Text, Font).Width + 34;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var graphics = pevent.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var active = Enabled && (_hovered || _pressed);
        var fill = _primary
            ? !Enabled ? Theme.Edge : active ? Color.FromArgb(212, 212, 216) : Theme.Ink
            : active ? Theme.TileHover : Theme.Tile;
        var text = _primary
            ? Enabled ? Theme.Background : Theme.Faint
            : Enabled ? Theme.Ink : Theme.Faint;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = Theme.Rounded(bounds, Height / 2f);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);

        if (!_primary || (Focused && ShowFocusCues))
        {
            using var pen = new Pen(Focused && ShowFocusCues ? Theme.Muted : Theme.Edge);
            graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var graphics = pevent.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = Theme.Rounded(track, Height / 2f);
        using var trackBrush = new SolidBrush(Checked ? Theme.Ink : Theme.Edge);
        graphics.FillPath(trackBrush, path);

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Theme.Muted);
            graphics.DrawPath(pen, path);
        }

        var inset = Height * 0.17f;
        var size = Height - (2 * inset);
        var x = Checked ? Width - inset - size : inset;
        using var knob = new SolidBrush(Checked ? Theme.Background : Theme.Muted);
        graphics.FillEllipse(knob, x, inset, size, size);
    }
}

internal sealed class TilePanel : Panel
{
    public TilePanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Tile;
    }

    public static TilePanel Setting(string text, bool on, Action<bool> changed, int x, int y)
    {
        var panel = new TilePanel { Bounds = new Rectangle(x, y, Theme.ContentWidth, 50) };
        var toggle = new ToggleSwitch { Checked = on, Location = new Point(Theme.ContentWidth - 16 - 44, 13), BackColor = Theme.Tile, AccessibleName = text };
        toggle.Click += (_, _) => changed(toggle.Checked);

        panel.Controls.Add(Theme.Text(text, Theme.Body, Theme.Ink, 16, 15, Theme.ContentWidth - 90, 20));
        panel.Controls.Add(toggle);
        return panel;
    }

    public ToggleSwitch? Toggle => Controls.OfType<ToggleSwitch>().FirstOrDefault();

    protected override void OnPaint(PaintEventArgs e) => Theme.PaintTile(e.Graphics, this, Theme.Tile, 14);
}

internal sealed class InfoTile : Control
{
    private string _value = "";
    private Color? _dot;

    public InfoTile(string caption, int x, int y, int width)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Caption = caption;
        Bounds = new Rectangle(x, y, width, 66);
        AccessibleRole = AccessibleRole.StaticText;
    }

    public string Caption { get; }

    public void Display(string value, Color? dot = null)
    {
        if (_value == value && _dot == dot)
        {
            return;
        }

        _value = value;
        _dot = dot;
        AccessibleName = $"{Caption}: {value}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var scale = Theme.ScaleOf(this);
        Theme.PaintTile(graphics, this, Theme.Tile, 14);

        var left = (int)(16 * scale);
        var right = Width - left;

        if (_dot is { } dot)
        {
            var size = 8 * scale;
            using var brush = new SolidBrush(dot);
            graphics.FillEllipse(brush, right - size, 17 * scale, size, size);
            right -= (int)(size + (8 * scale));
        }

        const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        TextRenderer.DrawText(graphics, Caption, Theme.Small, new Rectangle(left, (int)(12 * scale), right - left, (int)(18 * scale)), Theme.Muted, Theme.Tile, Flags);
        TextRenderer.DrawText(graphics, _value, Theme.Strong, new Rectangle(left, (int)(33 * scale), Width - (2 * left), (int)(22 * scale)), Theme.Ink, Theme.Tile, Flags);
    }
}
