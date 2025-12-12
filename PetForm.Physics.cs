using System;
using System.Drawing;

namespace QuackDuck;

internal sealed partial class PetForm
{
    // Ensures the window stays inside the visible screen area after movement.
    private void KeepPetInBounds(Rectangle bounds)
    {
        screenX = Math.Clamp(screenX, bounds.Left, bounds.Right - ClientSize.Width);
        screenY = Math.Clamp(screenY, bounds.Top, bounds.Bottom - ClientSize.Height);
    }

    // Clamp and immediately push the form to the corrected location.
    private void KeepPetInBoundsAndApply(Rectangle bounds)
    {
        KeepPetInBounds(bounds);
        Location = new Point((int)Math.Round(screenX), (int)Math.Round(screenY));
    }

    private void ApplyGravity()
    {
        verticalVelocity = Math.Min(verticalVelocity + Gravity, MaxFallSpeed);
    }

    private void ApplyGravityAndFall()
    {
        ApplyGravity();
        screenY += verticalVelocity;
        BounceVertically();

        if (screenY >= GroundLevel)
        {
            SnapToGround();
            verticalVelocity = 0;
        }
    }

    private float GroundLevel => workingArea.Bottom - ClientSize.Height + (debugState?.GroundOffset ?? 0f);

    private void SnapToGround()
    {
        screenY = GroundLevel;
        verticalVelocity = 0;
        fallAnimationStarted = false;
    }

    private bool IsOnGround() => screenY >= GroundLevel - 0.05f;

    private void BounceHorizontally()
    {
        var minX = workingArea.Left;
        var maxX = workingArea.Right - ClientSize.Width;
        var bounced = false;
        if (screenX <= minX)
        {
            screenX = minX;
            var speed = Math.Max(Math.Abs(horizontalVelocity), GroundSpeedBase);
            speed = Math.Max(speed * EdgeBounceDamping, GroundSpeedBase * 0.6f);
            horizontalVelocity = Math.Abs(speed);
            bounced = true;
        }
        else if (screenX >= maxX)
        {
            screenX = maxX;
            var speed = Math.Max(Math.Abs(horizontalVelocity), GroundSpeedBase);
            speed = Math.Max(speed * EdgeBounceDamping, GroundSpeedBase * 0.6f);
            horizontalVelocity = -Math.Abs(speed);
            bounced = true;
        }

        if (bounced)
        {
            AlignTargetWalkSpeedToVelocity();
        }

        UpdateFacingFromVelocity();
    }

    private void AlignTargetWalkSpeedToVelocity()
    {
        if (Math.Abs(horizontalVelocity) < 0.05f)
        {
            return;
        }

        var direction = Math.Sign(horizontalVelocity);
        var desired = GroundSpeedBase + GroundSpeedVariance * 0.5f;
        targetWalkSpeed = desired * direction;
    }

    private void UpdateFacingFromVelocity()
    {
        const float epsilon = 0.01f;
        if (horizontalVelocity > epsilon)
        {
            facingRight = true;
        }
        else if (horizontalVelocity < -epsilon)
        {
            facingRight = false;
        }
    }

    private void BounceVertically()
    {
        var top = workingArea.Top;
        if (screenY <= top)
        {
            screenY = top;
            verticalVelocity = Math.Abs(verticalVelocity) * CeilingBounceDamping;
            if (verticalVelocity < 1f)
            {
                verticalVelocity = 1f;
            }
        }
    }

    private void SetAnimationSpeedFromVelocity()
    {
        if (animator is null)
        {
            return;
        }

        var speed = Math.Abs(horizontalVelocity);
        var normalized = speed / (GroundSpeedBase * RunSpeedMultiplier);
        var multiplier = Math.Clamp(normalized, 0.5f, 2.5f);

        if (speed < 0.1f && Math.Abs(verticalVelocity) > 0.1f)
        {
            multiplier = Math.Clamp(Math.Abs(verticalVelocity) / JumpImpulse, 0.5f, 2.0f);
        }

        multiplier *= DebugSpeedFactor;
        animator.SetSpeedMultiplier(multiplier);
    }
}
