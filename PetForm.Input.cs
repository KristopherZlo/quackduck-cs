using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuackDuck;

internal sealed partial class PetForm
{
    private void UpdateDragging()
    {
        if (!isDragging)
        {
            return;
        }

        screenX = lastDragScreenPos.X - dragOffset.X;
        screenY = lastDragScreenPos.Y - dragOffset.Y;
        KeepPetInBounds(workingArea);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        wakeRequested = true;
        if (e.Button == MouseButtons.Right)
        {
            pendingJump = true;
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        isDragging = true;
        dragOffset = e.Location;
        var pos = Control.MousePosition;
        lastDragScreenPos = pos;
        prevDragScreenPos = pos;
        lastDragTime = DateTime.UtcNow;
        prevDragTime = lastDragTime;
        horizontalVelocity = 0;
        verticalVelocity = 0;
        Capture = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!isDragging)
        {
            return;
        }

        prevDragScreenPos = lastDragScreenPos;
        prevDragTime = lastDragTime;
        lastDragScreenPos = Control.MousePosition;
        lastDragTime = DateTime.UtcNow;

        screenX = lastDragScreenPos.X - dragOffset.X;
        screenY = lastDragScreenPos.Y - dragOffset.Y;
        KeepPetInBounds(workingArea);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!isDragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        isDragging = false;
        var elapsedMs = Math.Max(1, (lastDragTime - prevDragTime).TotalMilliseconds);
        var deltaX = lastDragScreenPos.X - prevDragScreenPos.X;
        var deltaY = lastDragScreenPos.Y - prevDragScreenPos.Y;

        // Convert pixels/ms into pixels per tick to stay consistent with the physics loop.
        horizontalVelocity = (float)(deltaX / elapsedMs * animationTimer.Interval);
        verticalVelocity = (float)(deltaY / elapsedMs * animationTimer.Interval);

        screenX = lastDragScreenPos.X - dragOffset.X;
        screenY = lastDragScreenPos.Y - dragOffset.Y - 1f; // nudge up so we start airborne
        workingArea = Screen.FromControl(this).WorkingArea;
        KeepPetInBounds(workingArea);
        BounceHorizontally();
        Capture = false;
        UpdateFacingFromVelocity();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1)
        {
            OpenSettings();
        }
    }
}
