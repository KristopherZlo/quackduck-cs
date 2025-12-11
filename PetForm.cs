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
    private const float GroundFriction = 0.08f;
    private const float EdgeBounceDamping = 0.78f;
    private const float CeilingBounceDamping = 0.85f;
    private const float RunSpeedMultiplier = 1.8f;
    private const float JumpImpulse = 10.5f;
    private const float HuntJumpImpulse = 13.5f;
    private const float TargetReachedTolerance = 4f;
    private const float RunDistanceThreshold = 600f;
    private const int EnergyRegenPerSecond = 1;
    private const int EnergyCostWalkGoal = 1;
    private const int EnergyCostRunGoal = 2;
    private const int EnergyCostJump = 2;
    private const int EnergyCostAttack = 1;
    private const int EnergyCostCrouch = 1;
    private const int EnergyCostWallgrab = 2;
    private const int EnergyCostCursorHuntSuccess = 5;

    // Timer controls the animation loop on the UI thread.
    private readonly System.Windows.Forms.Timer animationTimer;
    private readonly System.Windows.Forms.Timer energyTimer;
    private readonly int scale;
    private readonly Random random = new();
    private readonly AppSettings settings;
    private readonly EnergyMeter energy = new();
    private PetSkin skin = null!;
    private PetAnimator animator = null!;
    private PetStateMachine stateMachine = null!;
    private Rectangle workingArea;

    // The logical screen position and motion state of the transparent window.
    private float screenX;
    private float screenY;
    private float horizontalVelocity;
    private float verticalVelocity;
    private bool facingRight = true;
    private bool isDragging;
    private Point dragOffset;
    private Point lastDragScreenPos;
    private Point prevDragScreenPos;
    private DateTime lastDragTime;
    private DateTime prevDragTime;
    private float targetWalkSpeed;
    private PointF? targetPoint;
    private DateTime nextCursorHuntCheck = DateTime.UtcNow + TimeSpan.FromMinutes(5);
    private DateTime nextRandomSoundCheck = DateTime.UtcNow + TimeSpan.FromMinutes(5);
    private bool pendingJump;
    private bool wakeRequested;
    private bool heardSound;
    private bool spentForTarget;
    private DateTime landingReleaseTime;
    private DateTime listeningReleaseTime;
    private DateTime wallgrabReleaseTime;
    private DateTime attackReleaseTime;
    private DateTime sleepTransitionReleaseTime;
    private bool isSleeping;
    private bool isHearingSound;
    private DateTime soundStoppedAt = DateTime.MinValue;
    private DateTime idleReleaseTime;

    // Configure the transparent, borderless window and start ticking.
    public PetForm(AppSettings settings, int scale = 1)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be a positive integer.");
        }

        this.scale = scale;
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
        energyTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        energyTimer.Tick += (_, _) => OnEnergyTick();

        Load += (_, _) =>
        {
            skin = PetSkin.Load(settings.Skin);
            animator = new PetAnimator(skin, 50d);
            ClientSize = new Size(skin.FrameWidth * scale, skin.FrameHeight * scale);

            workingArea = Screen.FromControl(this).WorkingArea;
            screenX = workingArea.Left + (workingArea.Width - ClientSize.Width) / 2f;
            screenY = workingArea.Top;
            KeepPetInBoundsAndApply(workingArea);

            stateMachine = BuildStateMachine();
            animationTimer.Start();
            energyTimer.Start();
        };

        Resize += (_, _) =>
        {
            workingArea = Screen.FromControl(this).WorkingArea;
            KeepPetInBoundsAndApply(workingArea);
        };

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
    }

    // Drives movement each frame, applies gravity, and keeps the window within the screen bounds.
    private void AnimationTick(object? sender, EventArgs e)
    {
        workingArea = Screen.FromControl(this).WorkingArea;
        if (isDragging)
        {
            UpdateDragging();
            animator.SetAnimation("idle");
            animator.Update();
            KeepPetInBounds(workingArea);
            Location = new Point((int)Math.Round(screenX), (int)Math.Round(screenY));
            Invalidate();
            return;
        }

        stateMachine.Update();
        SetAnimationSpeedFromVelocity();
        animator.Update();
        KeepPetInBounds(workingArea);
        Location = new Point((int)Math.Round(screenX), (int)Math.Round(screenY));
        Invalidate();
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
            .State("Idle")
                .OnEnter(EnterIdle)
                .OnUpdate(UpdateIdle)
                .When(() => isDragging).GoTo("Dragging")
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(ShouldStartListening).GoTo("Listening")
                .When(ShouldStartCursorHunt).GoTo("CursorHunt")
                .When(() => pendingJump).GoTo("Jumping")
                .When(ShouldStartRunning).GoTo("Running")
                .When(ShouldStartWalking).GoTo("Walking")
                .EndState()
            .State("Walking")
                .OnEnter(() => BeginTravel(isRunning: false))
                .OnUpdate(() => UpdateTravel(isRunning: false))
                .When(() => isDragging).GoTo("Dragging")
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(() => pendingJump).GoTo("Jumping")
                .When(ReachedTarget).GoTo("Landing")
                .When(() => !IsOnGround()).GoTo("Jumping")
                .EndState()
            .State("Running")
                .OnEnter(() => BeginTravel(isRunning: true))
                .OnUpdate(() => UpdateTravel(isRunning: true))
                .When(() => isDragging).GoTo("Dragging")
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(() => pendingJump).GoTo("Jumping")
                .When(ReachedTarget).GoTo("Landing")
                .When(() => !IsOnGround()).GoTo("Jumping")
                .EndState()
            .State("Jumping")
                .OnEnter(EnterJump)
                .OnUpdate(UpdateJump)
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(() => IsOnGround() && verticalVelocity >= 0).GoTo("Landing")
                .EndState()
            .State("Landing")
                .OnEnter(EnterLanding)
                .OnUpdate(UpdateLanding)
                .When(() => DateTime.UtcNow >= landingReleaseTime).GoTo("Idle")
                .EndState()
            .State("Listening")
                .OnEnter(EnterListening)
                .OnUpdate(UpdateListening)
                .When(ShouldExitListening).GoTo("Idle")
                .EndState()
            .State("CursorHunt")
                .OnEnter(EnterCursorHunt)
                .OnUpdate(UpdateCursorHunt)
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(() => IsCursorVeryClose()).GoTo("Attacking")
                .When(() => ShouldCrouchDuringHunt()).GoTo("Crouching")
                .When(() => !IsOnGround() && HitWall()).GoTo("Wallgrab")
                .EndState()
            .State("Crouching")
                .OnEnter(EnterCrouch)
                .OnUpdate(UpdateCrouch)
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(() => IsCursorVeryClose()).GoTo("Attacking")
                .When(ReachedTarget).GoTo("Attacking")
                .EndState()
            .State("Attacking")
                .OnEnter(EnterAttack)
                .OnUpdate(UpdateAttack)
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(() => DateTime.UtcNow >= attackReleaseTime).GoTo("Idle")
                .EndState()
            .State("Wallgrab")
                .OnEnter(EnterWallgrab)
                .OnUpdate(UpdateWallgrab)
                .When(() => DateTime.UtcNow >= wallgrabReleaseTime).GoTo("Jumping")
                .EndState()
            .State("Dragging")
                .OnEnter(() => animator.SetAnimation("idle"))
                .OnUpdate(UpdateDragging)
                .When(() => !isDragging).GoTo("Jumping")
                .EndState()
            .State("SleepTransition")
                .OnEnter(EnterSleepTransition)
                .OnUpdate(UpdateSleepTransition)
                .When(() => DateTime.UtcNow >= sleepTransitionReleaseTime).GoTo("Sleeping")
                .EndState()
            .State("Sleeping")
                .OnEnter(EnterSleeping)
                .OnUpdate(UpdateSleeping)
                .When(ShouldWake).GoTo("Idle")
                .EndState()
            .WithInitialState("Idle")
            .Build();
    }

    private void EnterIdle()
    {
        isSleeping = false;
        targetPoint ??= PickGroundTarget();
        SetIdleAnimationVariant();
        idleReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(random.Next(1200, 3200));
        SnapToGround();
        horizontalVelocity = 0;
        verticalVelocity = 0;
        pendingJump = false;
        CheckRandomSounds();
        Log("Enter Idle");
    }

    private void UpdateIdle()
    {
        ApplyGravityAndFall();
        BounceHorizontally();
        UpdateFacingFromVelocity();
    }

    private bool ShouldSleep() => energy.IsDepleted;

    private bool ShouldStartWalking()
    {
        if (DateTime.UtcNow < idleReleaseTime)
        {
            return false;
        }

        if (targetPoint is null)
        {
            targetPoint = PickGroundTarget();
        }

        if (targetPoint is null)
        {
            return false;
        }

        var distance = Math.Abs(targetPoint.Value.X - screenX);
        return distance > TargetReachedTolerance;
    }

    private bool ShouldStartRunning()
    {
        if (DateTime.UtcNow < idleReleaseTime)
        {
            return false;
        }

        if (targetPoint is null)
        {
            targetPoint = PickGroundTarget();
        }

        if (targetPoint is null)
        {
            return false;
        }

        var distance = Math.Abs(targetPoint.Value.X - screenX);
        return distance > RunDistanceThreshold && random.NextDouble() < 0.4;
    }

    private void BeginTravel(bool isRunning)
    {
        targetPoint ??= PickGroundTarget();
        SnapToGround();
        var animation = isRunning ? "running" : "walk";
        animator.SetAnimation(animation, restartIfSame: true);
        var direction = Math.Sign((targetPoint?.X ?? screenX) - screenX);
        targetWalkSpeed = (GroundSpeedBase + (float)random.NextDouble() * GroundSpeedVariance) * (isRunning ? RunSpeedMultiplier : 1f) * (direction == 0 ? 1 : direction);
    }

    private void UpdateTravel(bool isRunning)
    {
        ApplyGravityAndFall();
        if (targetPoint is null)
        {
            targetPoint = PickGroundTarget();
        }

        if (targetPoint is null)
        {
            return;
        }

        var direction = Math.Sign(targetPoint.Value.X - screenX);
        var speed = (GroundSpeedBase + (float)random.NextDouble() * GroundSpeedVariance) * (isRunning ? RunSpeedMultiplier : 1f);
        horizontalVelocity = direction * speed;
        screenX += horizontalVelocity;
        BounceHorizontally();
        UpdateFacingFromVelocity();
        if (ReachedTarget())
        {
            if (!spentForTarget)
            {
                SpendTravelEnergy(isRunning);
                spentForTarget = true;
            }
        }
    }

    private bool ReachedTarget()
    {
        if (targetPoint is null)
        {
            return false;
        }

        return Math.Abs(targetPoint.Value.X - screenX) <= TargetReachedTolerance;
    }

    private void EnterJump()
    {
        if (pendingJump)
        {
            energy.Spend(EnergyCostJump);
        }

        pendingJump = false;
        animator.SetAnimation("jump", restartIfSame: true);
        verticalVelocity = -JumpImpulse;
        if (Math.Abs(horizontalVelocity) < GroundSpeedBase * 0.5f)
        {
            horizontalVelocity = facingRight ? GroundSpeedBase : -GroundSpeedBase;
        }
    }

    private void UpdateJump()
    {
        ApplyGravity();
        screenY += verticalVelocity;
        screenX += horizontalVelocity * AirDriftFactor;
        if (verticalVelocity > 0)
        {
            animator.SetAnimation("fall", loop: false, holdOnLastFrame: true);
        }
        if (screenY >= GroundLevel)
        {
            SnapToGround();
            verticalVelocity = 0;
        }
        BounceVertically();
        BounceHorizontally();
        UpdateFacingFromVelocity();
    }

    private void EnterLanding()
    {
        targetPoint = null;
        spentForTarget = false;
        SnapToGround();
        verticalVelocity = 0;
        horizontalVelocity = 0;
        animator.SetAnimation("land", restartIfSame: true, loop: false, holdOnLastFrame: true);
        landingReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(220);
        Log("Enter Landing");
    }

    private void UpdateLanding()
    {
        ApplyGravityAndFall();
        BounceHorizontally();
    }

    private void EnterListening()
    {
        Log("Enter Listening");
        animator.SetAnimation("listen", restartIfSame: true, loop: true);
        SnapToGround();
        listeningReleaseTime = DateTime.UtcNow;
    }

    private void UpdateListening()
    {
        ApplyGravityAndFall();
        BounceHorizontally();
    }

    private bool ShouldStartListening()
    {
        if (!isHearingSound && !heardSound)
        {
            return false;
        }

        heardSound = false;
        Log("Enter Listening from sound");
        return true;
    }

    private bool ShouldExitListening()
    {
        if (isHearingSound)
        {
            listeningReleaseTime = DateTime.UtcNow;
            return false;
        }

        if (DateTime.UtcNow - listeningReleaseTime < TimeSpan.FromSeconds(1))
        {
            return false;
        }

        Log("Exit Listening -> Idle");
        targetPoint = null;
        idleReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(random.Next(800, 1800));
        return true;
    }

    private void EnterCursorHunt()
    {
        nextCursorHuntCheck = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        var cursor = Control.MousePosition;
        targetPoint = new PointF(cursor.X, cursor.Y);
        spentForTarget = false;
        if (targetPoint.Value.Y >= workingArea.Bottom - 10)
        {
            var run = random.Next(0, 2) == 0;
            BeginTravel(isRunning: run);
        }
        else
        {
            animator.SetAnimation("running", restartIfSame: true);
            verticalVelocity = -HuntJumpImpulse;
            horizontalVelocity = Math.Sign(targetPoint.Value.X - screenX) * GroundSpeedBase * RunSpeedMultiplier;
        }
        Log("Enter CursorHunt");
    }

    private void UpdateCursorHunt()
    {
        if (targetPoint is null)
        {
            targetPoint = Control.MousePosition;
        }

        var distance = DistanceToCursor();
        if (distance > 0 && distance < 300f && IsOnGround())
        {
            if (random.Next(0, 3) == 0)
            {
                pendingJump = true;
            }
        }

        if (IsOnGround())
        {
            var speed = GroundSpeedBase * (distance > RunDistanceThreshold ? RunSpeedMultiplier : 1f);
            var direction = Math.Sign((targetPoint?.X ?? screenX) - screenX);
            horizontalVelocity = direction * speed;
            screenX += horizontalVelocity;
            BounceHorizontally();
        }
        else
        {
            UpdateJump();
        }
    }

    private bool ShouldStartCursorHunt()
    {
        if (DateTime.UtcNow < nextCursorHuntCheck)
        {
            return false;
        }

        nextCursorHuntCheck = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        var chance = settings.CursorHuntChancePercent;
        var roll = random.Next(0, 100);
        if (roll < chance)
        {
            var cursor = Control.MousePosition;
            targetPoint = new PointF(cursor.X, cursor.Y);
            Log("Enter CursorHunt");
            return true;
        }

        return false;
    }

    private void EnterCrouch()
    {
        animator.SetAnimation("idle-2", restartIfSame: true);
        var cursor = Control.MousePosition;
        targetPoint = new PointF(cursor.X, GroundLevel);
        spentForTarget = false;
        energy.Spend(EnergyCostCrouch);
        Log("Enter Crouching");
    }

    private void UpdateCrouch()
    {
        ApplyGravityAndFall();
        if (targetPoint is null)
        {
            return;
        }

        var direction = Math.Sign(targetPoint.Value.X - screenX);
        horizontalVelocity = direction * GroundSpeedBase * 0.6f;
        screenX += horizontalVelocity;
        BounceHorizontally();
        UpdateFacingFromVelocity();
    }

    private bool ShouldCrouchDuringHunt()
    {
        return IsOnGround() && DistanceToCursor() < 300f && random.Next(0, 2) == 0;
    }

    private void EnterAttack()
    {
        energy.Spend(EnergyCostAttack);
        if (DistanceToCursor() <= 20f)
        {
            energy.Spend(EnergyCostCursorHuntSuccess);
        }

        attackReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(450);
        animator.SetAnimation("attack", restartIfSame: true);

        var cursor = Control.MousePosition;
        var dx = cursor.X - screenX;
        facingRight = dx >= 0;
        horizontalVelocity = Math.Sign(dx) * GroundSpeedBase * RunSpeedMultiplier;
        if (IsOnGround())
        {
            SnapToGround();
        }
        Log("Enter Attack");
    }

    private void UpdateAttack()
    {
        ApplyGravityAndFall();
        screenX += horizontalVelocity;
        BounceHorizontally();
    }

    private bool IsCursorVeryClose() => DistanceToCursor() <= 20f;

    private float DistanceToCursor()
    {
        var cursor = Control.MousePosition;
        var dx = cursor.X - screenX;
        var dy = cursor.Y - screenY;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private void EnterWallgrab()
    {
        animator.SetAnimation("idle-1", restartIfSame: true);
        wallgrabReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        horizontalVelocity = 0;
        verticalVelocity = 0;
        energy.Spend(EnergyCostWallgrab);
        Log("Enter Wallgrab");
    }

    private void UpdateWallgrab()
    {
        screenY += 0.5f;
        BounceVertically();
    }

    private bool HitWall()
    {
        var minX = workingArea.Left;
        var maxX = workingArea.Right - ClientSize.Width;
        return screenX <= minX + 0.5f || screenX >= maxX - 0.5f;
    }

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

    private void EnterSleepTransition()
    {
        animator.SetAnimation("sleep_transition", restartIfSame: true);
        sleepTransitionReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        SnapToGround();
        isSleeping = false;
        Log("Enter SleepTransition");
    }

    private void UpdateSleepTransition()
    {
        SnapToGround();
    }

    private void EnterSleeping()
    {
        animator.SetAnimation("sleep", restartIfSame: true);
        SnapToGround();
        isSleeping = true;
        wakeRequested = false;
        Log("Enter Sleeping");
    }

    private void UpdateSleeping()
    {
        SnapToGround();
        BounceHorizontally();
    }

    private bool ShouldWake()
    {
        if (!isSleeping)
        {
            return false;
        }

        if (wakeRequested || heardSound)
        {
            wakeRequested = false;
            heardSound = false;
            return energy.Percent > 25 || energy.Current >= energy.Max / 4;
        }

        return false;
    }

    private PointF? PickGroundTarget()
    {
        var minX = workingArea.Left;
        var maxX = workingArea.Right - ClientSize.Width;
        var x = random.Next((int)minX, (int)maxX);
        spentForTarget = false;
        return new PointF(x, GroundLevel);
    }

    private void SetIdleAnimationVariant()
    {
        var roll = random.Next(0, 3);
        var name = roll switch
        {
            1 => "idle-1",
            2 => "idle-2",
            _ => "idle"
        };

        animator.SetAnimation(name, restartIfSame: true);
    }

    private void SpendTravelEnergy(bool isRunning)
    {
        if (isRunning)
        {
            energy.Spend(EnergyCostRunGoal);
        }
        else
        {
            energy.Spend(EnergyCostWalkGoal);
        }
    }

    private void CheckRandomSounds()
    {
        if (DateTime.UtcNow < nextRandomSoundCheck || skin.SoundPaths.Count == 0)
        {
            return;
        }

        nextRandomSoundCheck = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        if (random.Next(0, 100) >= settings.RandomSoundChancePercent)
        {
            return;
        }

        var sound = skin.SoundPaths[random.Next(skin.SoundPaths.Count)];
        TryPlaySound(sound);
    }

    private void TryPlaySound(string path)
    {
        try
        {
            if (System.IO.Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                using var player = new System.Media.SoundPlayer(path);
                player.Play();
            }
        }
        catch
        {
            // ignore playback issues
        }
    }

    // External hooks for microphone events (call from audio listener)
    internal void NotifySoundStarted()
    {
        isHearingSound = true;
        heardSound = true;
        Log("Sound detected");
    }

    internal void NotifySoundStopped()
    {
        isHearingSound = false;
        soundStoppedAt = DateTime.UtcNow;
        Log("Sound stopped");
    }

    private void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void OnEnergyTick()
    {
        if (isSleeping)
        {
            energy.Restore(EnergyRegenPerSecond);
        }
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

    private float GroundLevel => workingArea.Bottom - ClientSize.Height;

    private void SnapToGround()
    {
        screenY = GroundLevel;
        verticalVelocity = 0;
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

    // Tear down animation resources to avoid leaks.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
            energyTimer.Stop();
            energyTimer.Dispose();
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

        var destination = new Rectangle(0, 0, skin.FrameWidth * scale, skin.FrameHeight * scale);
        e.Graphics.DrawImage(skin.SpriteSheet, destination, source, GraphicsUnit.Pixel);
        e.Graphics.Restore(state);
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

    private void SetAnimationSpeedFromVelocity()
    {
        if (animator is null)
        {
            return;
        }

        animator.SetSpeedMultiplier(1.0);
    }
}
