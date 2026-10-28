using System.Drawing.Drawing2D;

namespace AIHub;

internal sealed class ProviderButton : Button
{
    private readonly Bitmap icon;
    private readonly string label;

    internal ProviderButton(AppEntry entry, Action click)
    {
        label = entry.Name;
        icon = UsageStyle.Icon(entry.Key, entry.Executable);
        AccessibleName = "Open " + entry.Name;
        AccessibleDescription = entry.Description;
        Size = new Size(72, 64);
        Margin = new Padding(0, 0, 0, 8);
        Font = new Font("Segoe UI Semibold", 9);
        FlatStyle = FlatStyle.Flat;
        BackColor = UsageStyle.Surface;
        ForeColor = entry.Accent;
        Cursor = Cursors.Hand;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 44, 61);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        Click += (_, _) => click();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Keep the original artwork sharp when Windows display scaling changes.
        int size = LogicalToDeviceUnits(28), gap = LogicalToDeviceUnits(5);
        int top = Math.Max(0, (ClientSize.Height - size - gap - Font.Height) / 2);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(icon, new Rectangle((ClientSize.Width - size) / 2, top, size, size));
        TextRenderer.DrawText(e.Graphics, label, Font,
            new Rectangle(0, top + size + gap, ClientSize.Width, Font.Height), ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) icon.Dispose();
        base.Dispose(disposing);
    }
}
