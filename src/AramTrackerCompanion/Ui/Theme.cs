using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AramTrackerCompanion.Ui;

internal static class Theme
{
    public const int Margin = 24;
    public const int ContentWidth = 412;
    public const int FormWidth = ContentWidth + (2 * Margin);

    public static readonly Color Background = Color.FromArgb(11, 11, 12);
    public static readonly Color Tile = Color.FromArgb(24, 24, 27);
    public static readonly Color TileHover = Color.FromArgb(36, 36, 40);
    public static readonly Color Edge = Color.FromArgb(44, 44, 49);
    public static readonly Color Ink = Color.White;
    public static readonly Color Muted = Color.FromArgb(161, 161, 170);
    public static readonly Color Faint = Color.FromArgb(113, 113, 122);
    public static readonly Color Good = Color.FromArgb(52, 211, 153);
    public static readonly Color Bad = Color.FromArgb(248, 113, 113);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Brand = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Heading = new("Segoe UI Semibold", 14f);
    public static readonly Font Headline = new("Segoe UI", 19f, FontStyle.Bold);
    public static readonly Font Code = new("Consolas", 12f);

    public static Icon AppIcon { get; } = LoadIcon();

    public static float ScaleOf(Control control) => control.DeviceDpi / 96f;

    public static void Apply(Form form, string title, int height)
    {
        form.Text = title;
        form.Icon = AppIcon;
        form.Font = Body;
        form.BackColor = Background;
        form.ForeColor = Ink;
        form.AutoScaleMode = AutoScaleMode.None;
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.ClientSize = new Size(FormWidth, height);
        form.HandleCreated += (_, _) => UseDarkTitleBar(form.Handle);
    }

    // Call last, after every control is placed.
    public static void FitToDpi(Form form)
    {
        var factor = ScaleOf(form);
        if (factor != 1f)
        {
            var client = form.ClientSize;
            form.Scale(new SizeF(factor, factor));
            form.ClientSize = new Size((int)Math.Round(client.Width * factor), (int)Math.Round(client.Height * factor));
        }
    }

    public static Label Text(string text, Font font, Color color, int x, int y, int width, int height) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = false,
        Bounds = new Rectangle(x, y, width, height),
        UseMnemonic = false,
    };

    public static void AddBrand(Control parent)
    {
        parent.Controls.Add(new LogoMark { Bounds = new Rectangle(Margin, 22, 36, 36) });
        parent.Controls.Add(new BrandName { Bounds = new Rectangle(72, 22, 300, 20) });
        parent.Controls.Add(Text("Companion", Small, Muted, 70, 41, 300, 18));
    }

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintTile(Graphics graphics, Control control, Color fill, float radius)
    {
        graphics.Clear(control.Parent?.BackColor ?? Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0.5f, 0.5f, control.Width - 1, control.Height - 1);
        using var path = Rounded(bounds, radius * ScaleOf(control));
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(Edge);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
    }

    private static void UseDarkTitleBar(IntPtr window)
    {
        var enabled = 1;

        try
        {
            if (DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(window, 19, ref enabled, sizeof(int));
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path && Icon.ExtractAssociatedIcon(path) is { } icon)
            {
                return icon;
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
        }

        return SystemIcons.Application;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
