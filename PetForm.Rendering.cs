using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuackDuck;

internal sealed partial class PetForm
{
    // Draw the visible square pet centered inside the transparent window.
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (skin is null || animator is null)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

        var source = animator.CurrentSourceFrame;
        if (source == Rectangle.Empty)
        {
            return;
        }

        var state = e.Graphics.Save();
        if (!facingRight)
        {
            e.Graphics.TranslateTransform(ClientSize.Width, 0);
            e.Graphics.ScaleTransform(-1, 1);
        }

        var destination = new Rectangle(0, 0, skin.FrameWidth * scale, skin.FrameHeight * scale);
        e.Graphics.DrawImage(skin.SpriteSheet, destination, source, GraphicsUnit.Pixel);
        e.Graphics.Restore(state);
    }
}
