using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace QuackDuck;

internal sealed class NameOverlayForm : Form
{
    private const int PaddingX = 6;
    private const int PaddingY = 4;
    private const int VerticalSpacing = 4;
    private Font? currentFont;
    private string text = string.Empty;

    public NameOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = BackColor;
        TopMost = true;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int WS_EX_NOACTIVATE = 0x08000000;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void UpdateContent(string name, float fontSize)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "Quack" : name;
        var clampedSize = Math.Clamp(fontSize, 8f, 64f);
        var needsFont = currentFont is null || Math.Abs(currentFont.Size - clampedSize) > 0.1f;
        if (needsFont)
        {
            currentFont?.Dispose();
            currentFont = new Font(FontFamily.GenericSansSerif, clampedSize, FontStyle.Bold);
        }

        if (!string.Equals(text, normalized, StringComparison.Ordinal) || needsFont)
        {
            text = normalized;
            RecalculateSize();
            Invalidate();
        }
    }

    public void UpdatePosition(Point petLocation, int petWidth, double offset)
    {
        var x = petLocation.X + (petWidth - ClientSize.Width) / 2;
        var y = petLocation.Y - ClientSize.Height - VerticalSpacing + (int)Math.Round(offset);
        Location = new Point(x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (currentFont is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var rect = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        using var background = new SolidBrush(Color.FromArgb(170, 16, 16, 16));
        using var textBrush = new SolidBrush(Color.White);
        e.Graphics.FillRectangle(background, rect);

        var textSize = TextRenderer.MeasureText(e.Graphics, text, currentFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var x = (ClientSize.Width - textSize.Width) / 2f;
        var y = (ClientSize.Height - textSize.Height) / 2f;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        e.Graphics.DrawString(text, currentFont, textBrush, new PointF(x, y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            currentFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RecalculateSize()
    {
        if (currentFont is null)
        {
            return;
        }

        using var g = CreateGraphics();
        var textSize = TextRenderer.MeasureText(g, text, currentFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var width = Math.Max(1, textSize.Width + PaddingX * 2);
        var height = Math.Max(1, textSize.Height + PaddingY * 2);
        ClientSize = new Size(width, height);
    }
}
