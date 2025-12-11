using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuackDuck;

internal sealed class PetForm : Form
{
    // Physics tuning constants for the pet.
    private const float Gravity = 0.8f;
    private const float MaxFallSpeed = 30f;
    private const float GroundSpeedBase = 2.2f;
    private const float GroundSpeedVariance = 1.3f;
    private const float AirDriftFactor = 0.35f;
    private const double IdleChancePerTick = 0.0025;

    // Timer controls the animation loop on the UI thread.
    private readonly System.Windows.Forms.Timer animationTimer;
    private readonly Random random = new();
    private PetSkin skin = null!;
    private PetAnimator animator = null!;
    private PetStateMachine stateMachine = null!;
    private Rectangle workingArea;

    // The logical screen position and motion state of the transparent window.
    private float screenX;
    private float screenY;
    private float horizontalVelocity;
    private float verticalVelocity;
    private int idleTicks;
    private bool facingRight = true;

    // Configure the transparent, borderless window and start ticking.
    public PetForm()
    {
        Text = "QuackDuck Pet";
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = BackColor;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        AllowTransparency = true;
        DoubleBuffered = true;
        TopMost = true;

        horizontalVelocity = 0;
        verticalVelocity = 0;

        animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animationTimer.Tick += AnimationTick;

        Load += (_, _) =>
        {
            skin = PetSkin.Load();
            animator = new PetAnimator(skin);
            ClientSize = new Size(skin.FrameWidth, skin.FrameHeight);

            workingArea = Screen.FromControl(this).WorkingArea;
            screenX = workingArea.Left + (workingArea.Width - ClientSize.Width) / 2f;
            screenY = workingArea.Top;
            KeepPetInBoundsAndApply(workingArea);

            stateMachine = BuildStateMachine();
            animationTimer.Start();
        };

        Resize += (_, _) =>
        {
            workingArea = Screen.FromControl(this).WorkingArea;
            KeepPetInBoundsAndApply(workingArea);
        };
    }

    // Drives movement each frame, applies gravity, and keeps the window within the screen bounds.
    private void AnimationTick(object? sender, EventArgs e)
    {
        workingArea = Screen.FromControl(this).WorkingArea;
        stateMachine.Update();
        animator.Update();
        KeepPetInBounds(workingArea);
        Location = new Point((int)Math.Round(screenX), (int)Math.Round(screenY));
        Invalidate();
    }

    // Picks a random direction and speed to simulate walking after landing.
    private void ChooseNewDirection()
    {
        var direction = random.Next(0, 2) == 0 ? -1f : 1f;
        horizontalVelocity = direction * (GroundSpeedBase + (float)random.NextDouble() * GroundSpeedVariance);
        UpdateFacingFromVelocity();
    }

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

    private PetStateMachine BuildStateMachine()
    {
        return PetStateMachineBuilder.Create()
            .State("Airborne")
                .OnUpdate(UpdateAirborne)
                .When(IsOnGround).GoTo("Grounded")
                .EndState()
            .State("Grounded")
                .OnEnter(() =>
                {
                    SnapToGround();
                    ChooseNewDirection();
                })
                .OnUpdate(UpdateGrounded)
                .When(ShouldIdle).GoTo("Idle")
                .When(() => !IsOnGround()).GoTo("Airborne")
                .EndState()
            .State("Idle")
                .OnEnter(BeginIdle)
                .OnUpdate(UpdateIdle)
                .When(() => idleTicks <= 0).GoTo("Grounded")
                .When(() => !IsOnGround()).GoTo("Airborne")
                .EndState()
            .WithInitialState("Airborne")
            .Build();
    }

    private void UpdateAirborne()
    {
        ApplyGravity();
        screenY += verticalVelocity;

        if (screenY >= GroundLevel)
        {
            SnapToGround();
        }

        animator.SetAnimation(verticalVelocity < 0 ? "jump" : "fall");
        screenX += horizontalVelocity * AirDriftFactor;
        BounceHorizontally();
    }

    private void UpdateGrounded()
    {
        ApplyGravity();
        screenY += verticalVelocity;

        if (screenY >= GroundLevel)
        {
            SnapToGround();
        }

        animator.SetAnimation("walk");
        screenX += horizontalVelocity;
        BounceHorizontally();
    }

    private void BeginIdle()
    {
        animator.SetAnimation("idle", restartIfSame: true);
        idleTicks = random.Next(45, 120);
        horizontalVelocity = 0;
        SnapToGround();
    }

    private void UpdateIdle()
    {
        ApplyGravity();
        screenY += verticalVelocity;

        if (screenY >= GroundLevel)
        {
            SnapToGround();
        }

        idleTicks--;
        BounceHorizontally();
    }

    private void ApplyGravity()
    {
        verticalVelocity = Math.Min(verticalVelocity + Gravity, MaxFallSpeed);
    }

    private float GroundLevel => workingArea.Bottom - ClientSize.Height;

    private void SnapToGround()
    {
        screenY = GroundLevel;
        verticalVelocity = 0;
    }

    private bool IsOnGround() => screenY >= GroundLevel - 0.5f;

    private bool ShouldIdle() => IsOnGround() && random.NextDouble() < IdleChancePerTick;

    private void BounceHorizontally()
    {
        var minX = workingArea.Left;
        var maxX = workingArea.Right - ClientSize.Width;
        if (screenX <= minX)
        {
            screenX = minX;
            horizontalVelocity = Math.Abs(horizontalVelocity);
        }
        else if (screenX >= maxX)
        {
            screenX = maxX;
            horizontalVelocity = -Math.Abs(horizontalVelocity);
        }

        UpdateFacingFromVelocity();
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

    // Tear down animation resources to avoid leaks.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
            skin?.Dispose();
        }

        base.Dispose(disposing);
    }

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

        var destination = new Rectangle(0, 0, skin.FrameWidth, skin.FrameHeight);
        e.Graphics.DrawImage(skin.SpriteSheet, destination, source, GraphicsUnit.Pixel);
        e.Graphics.Restore(state);
    }
}
