using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuackDuck;

internal sealed partial class PetForm
{
    private PetStateMachine BuildStateMachine()
    {
        return PetStateMachineBuilder.Create()
            .WithTransitionObserver(LogStateChange)
            .State("Idle")
                .OnEnter(EnterIdle)
                .OnUpdate(UpdateIdle)
                .When(() => isDragging).GoTo("Dragging")
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(ShouldStartListening).GoTo("Listening")
                .When(ShouldStartCursorHunt).GoTo("CursorHunt")
                .When(() => pendingJump).GoTo("Jumping")
                .When(() => !IsOnGround()).GoTo("Falling")
                .When(ShouldStartRunning).GoTo("Running")
                .When(ShouldStartWalking).GoTo("Walking")
                .EndState()
            .State("Walking")
                .OnEnter(() => BeginTravel(isRunning: false))
                .OnUpdate(() => UpdateTravel(isRunning: false))
                .When(() => isDragging).GoTo("Dragging")
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(ShouldStartListening).GoTo("Listening")
                .When(() => pendingJump).GoTo("Jumping")
                .When(ReachedTarget).GoTo("Idle")
                .When(() => !IsOnGround()).GoTo("Falling")
                .EndState()
            .State("Running")
                .OnEnter(() => BeginTravel(isRunning: true))
                .OnUpdate(() => UpdateTravel(isRunning: true))
                .When(() => isDragging).GoTo("Dragging")
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(ShouldStartListening).GoTo("Listening")
                .When(() => pendingJump).GoTo("Jumping")
                .When(ReachedTarget).GoTo("Idle")
                .When(() => !IsOnGround()).GoTo("Falling")
                .EndState()
            .State("Jumping")
                .OnEnter(EnterJump)
                .OnUpdate(UpdateJump)
                .When(ShouldSleep).GoTo("SleepTransition")
                .When(() => verticalVelocity > 0).GoTo("Falling")
                .When(() => IsOnGround() && verticalVelocity >= 0).GoTo("Landing")
                .EndState()
            .State("Falling")
                .OnEnter(EnterFalling)
                .OnUpdate(UpdateFalling)
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
                .When(() => !isDragging && !IsOnGround() && verticalVelocity < 0).GoTo("Jumping")
                .When(() => !isDragging && !IsOnGround()).GoTo("Falling")
                .When(() => !isDragging).GoTo("Idle")
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
            .WithInitialState("Falling")
            .Build();
    }

    private void EnterIdle()
    {
        isSleeping = false;
        targetPoint = PickGroundTarget();
        SetIdleAnimationVariant();
        idleReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(random.Next(1200, 3200));
        SnapToGround();
        horizontalVelocity = 0;
        verticalVelocity = 0;
        pendingJump = false;
        fallAnimationStarted = false;
        CheckRandomSounds();
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

        targetPoint ??= PickGroundTarget();

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

        targetPoint ??= PickGroundTarget();

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
        targetWalkSpeed = (GroundSpeedBase + (float)random.NextDouble() * GroundSpeedVariance) * (isRunning ? RunSpeedMultiplier : 1f) * DebugSpeedFactor * (direction == 0 ? 1 : direction);
        lastTravelWasRunning = isRunning;
        travelActive = true;
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
        var speed = (GroundSpeedBase + (float)random.NextDouble() * GroundSpeedVariance) * (isRunning ? RunSpeedMultiplier : 1f) * DebugSpeedFactor;
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
            travelActive = false;
            targetPoint = null;
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
        var launching = pendingJump || IsOnGround();
        if (pendingJump)
        {
            energy.Spend(EnergyCostJump);
        }

        pendingJump = false;
        fallAnimationStarted = false;

        if (launching)
        {
            animator.SetAnimation("jump", restartIfSame: true);
            verticalVelocity = -JumpImpulse * DebugSpeedFactor;
            if (Math.Abs(horizontalVelocity) < GroundSpeedBase * 0.5f)
            {
                horizontalVelocity = (facingRight ? GroundSpeedBase : -GroundSpeedBase) * DebugSpeedFactor;
            }
        }
        else
        {
            fallAnimationStarted = false;
            if (verticalVelocity > 0)
            {
                animator.SetAnimation("fall", loop: false, holdOnLastFrame: true);
                fallAnimationStarted = true;
            }
            else
            {
                animator.SetAnimation("jump", restartIfSame: true);
            }
        }
    }

    private void UpdateJump()
    {
        ApplyGravity();
        screenY += verticalVelocity;
        screenX += horizontalVelocity * AirDriftFactor;
        if (verticalVelocity > 0 && !fallAnimationStarted)
        {
            animator.SetAnimation("fall", loop: false, holdOnLastFrame: true);
            fallAnimationStarted = true;
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

    private void EnterFalling()
    {
        if (!fallAnimationStarted)
        {
            animator.SetAnimation("fall", loop: false, holdOnLastFrame: true);
        }

        fallAnimationStarted = true;
        if (verticalVelocity <= -0.1f)
        {
            return;
        }

        if (verticalVelocity < 0.1f)
        {
            verticalVelocity = 0.5f * DebugSpeedFactor;
        }
    }

    private void UpdateFalling()
    {
        ApplyGravity();
        screenY += verticalVelocity;
        screenX += horizontalVelocity * AirDriftFactor;
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
        travelActive = false;
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
        animator.SetAnimation("listen", restartIfSame: true, loop: true);
        SnapToGround();
        fallAnimationStarted = false;
        listeningReleaseTime = DateTime.UtcNow;
        CompleteTravelIfPending();
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
            verticalVelocity = -HuntJumpImpulse * DebugSpeedFactor;
            horizontalVelocity = Math.Sign(targetPoint.Value.X - screenX) * GroundSpeedBase * RunSpeedMultiplier * DebugSpeedFactor;
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
            var speed = GroundSpeedBase * (distance > RunDistanceThreshold ? RunSpeedMultiplier : 1f) * DebugSpeedFactor;
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
        var chance = cursorHuntChancePercent;
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
        fallAnimationStarted = false;
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
        horizontalVelocity = direction * GroundSpeedBase * 0.6f * DebugSpeedFactor;
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
        fallAnimationStarted = false;

        var cursor = Control.MousePosition;
        var dx = cursor.X - screenX;
        facingRight = dx >= 0;
        horizontalVelocity = Math.Sign(dx) * GroundSpeedBase * RunSpeedMultiplier * DebugSpeedFactor;
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
        fallAnimationStarted = false;
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

    private void EnterSleepTransition()
    {
        animator.SetAnimation("sleep_transition", restartIfSame: true);
        sleepTransitionReleaseTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        SnapToGround();
        fallAnimationStarted = false;
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
        fallAnimationStarted = false;
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
        if (maxX - minX <= TargetReachedTolerance * 2)
        {
            return null;
        }

        const int maxAttempts = 5;
        var attempt = 0;
        while (attempt < maxAttempts)
        {
            var x = random.Next((int)minX, (int)maxX);
            if (Math.Abs(x - screenX) > TargetReachedTolerance * 2)
            {
                spentForTarget = false;
                return new PointF(x, GroundLevel);
            }

            attempt++;
        }

        var fallbackDirection = Math.Sign(random.NextDouble() - 0.5);
        var fallbackX = Math.Clamp(screenX + fallbackDirection * TargetReachedTolerance * 3, minX, maxX);
        spentForTarget = false;
        return new PointF(fallbackX, GroundLevel);
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

    private void CompleteTravelIfPending()
    {
        if (!travelActive || targetPoint is null || spentForTarget)
        {
            return;
        }

        SpendTravelEnergy(lastTravelWasRunning);
        spentForTarget = true;
        travelActive = false;
        targetPoint = null;
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
        if (random.Next(0, 100) >= randomSoundChancePercent)
        {
            return;
        }

        var sound = skin.SoundPaths[random.Next(skin.SoundPaths.Count)];
        TryPlaySound(sound);
    }

    private void TryPlaySound(string path)
    {
        _ = soundPlayer.PlayAsync(path);
    }
}
