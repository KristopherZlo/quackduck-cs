using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using QuackDuck.DebugUI;

namespace QuackDuck;

internal sealed partial class PetForm : Form
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
    private const float JumpImpulse = 15.75f;
    private const float HuntJumpImpulse = 20.25f;
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
    private int scale;
    private readonly Random random = new();
    private readonly AppSettings settings;
    private readonly IEnergyService energy = new EnergyMeter();
    private readonly IPetAudioPlayer soundPlayer = new SoundEffectPlayer();
    private readonly DebugState? debugState;
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
    private bool dragReleaseWasGrounded;
    private bool dragReleaseRun;
    private float targetWalkSpeed;
    private PointF? targetPoint;
    private DateTime nextCursorHuntCheck = DateTime.UtcNow + TimeSpan.FromMinutes(5);
    private DateTime nextRandomSoundCheck = DateTime.UtcNow + TimeSpan.FromMinutes(5);
    private DateTime nextProximityAttackCheck = DateTime.UtcNow;
    private bool pendingJump;
    private bool microphoneEnabled = true;
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
    private string? lastStateName;
    private bool fallAnimationStarted;
    private MicrophoneListener? microphoneListener;
    private float lastMicLevel;
    private bool lastTravelWasRunning;
    private bool travelActive;
    private int cursorHuntChancePercent;
    private int randomSoundChancePercent;
    private bool isPausedHidden;

    // Configure the transparent, borderless window and start ticking.
    public PetForm(AppSettings settings, int scale = 1)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be a positive integer.");
        }

        this.scale = scale;
        cursorHuntChancePercent = settings.CursorHuntChancePercent;
        randomSoundChancePercent = settings.RandomSoundChancePercent;
        if (settings.Debug)
        {
            debugState = new DebugState
            {
                Scale = scale,
                DebugEnabled = true
            };
            DebugHost.Start(debugState);
        }
        Text = "QuackDuck Pet";
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = BackColor;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        AllowTransparency = true;
        DoubleBuffered = true;
        TopMost = true;
        KeyPreview = true;

        horizontalVelocity = 0;
        verticalVelocity = 0;

        animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animationTimer.Tick += AnimationTick;
        energyTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        energyTimer.Tick += (_, _) => OnEnergyTick();

        Load += (_, _) =>
        {
            skin = PetSkin.Load(settings.Skin);
            animator = new PetAnimator(skin, PetAnimator.DefaultFrameDurationMs);
            scale = debugState?.Scale ?? scale;
            ClientSize = new Size(skin.FrameWidth * scale, skin.FrameHeight * scale);

            workingArea = Screen.FromControl(this).WorkingArea;
            screenX = workingArea.Left + (workingArea.Width - ClientSize.Width) / 2f;
            screenY = workingArea.Top;
            KeepPetInBoundsAndApply(workingArea);

            microphoneEnabled = true;
            if (debugState is not null)
            {
                debugState.MicrophoneEnabled = true;
                debugState.MicrophoneThreshold = 0.08;
            }
            InitializeMicrophone();

            stateMachine = BuildStateMachine();
            animationTimer.Start();
            energyTimer.Start();
            if (debugState is not null)
            {
                debugState.CurrentState = stateMachine.CurrentState;
                debugState.AppendHistory(stateMachine.CurrentState);
            }

            InitializeTrayIcon();
        };

        Resize += (_, _) =>
        {
            workingArea = Screen.FromControl(this).WorkingArea;
            KeepPetInBoundsAndApply(workingArea);
        };

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
    }

    // Drives movement each frame, applies gravity, and keeps the window within the screen bounds.
    private void AnimationTick(object? sender, EventArgs e)
    {
        workingArea = Screen.FromControl(this).WorkingArea;
        HandleDebugControls();
        if (isDragging && stateMachine.CurrentState != "Dragging")
        {
            stateMachine.ForceState("Dragging");
        }
        stateMachine.Update();
        TrackStateHistory();
        SetAnimationSpeedFromVelocity();
        animator.Update();
        KeepPetInBounds(workingArea);
        Location = new Point((int)Math.Round(screenX), (int)Math.Round(screenY));
        UpdateDebugTelemetry();
        Invalidate();
    }

    private void ReloadSkin()
    {
        try
        {
            skin?.Dispose();
        }
        catch
        {
        }

        skin = PetSkin.Load(settings.Skin);
        animator = new PetAnimator(skin, PetAnimator.DefaultFrameDurationMs);
        ClientSize = new Size(skin.FrameWidth * scale, skin.FrameHeight * scale);
        KeepPetInBoundsAndApply(workingArea);
        Log("Skin reloaded (debug)");
    }

    private void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void OpenSettings()
    {
        DebugUI.DebugHost.OpenSettings();
    }

    private void ApplySettings(PetSettingsSnapshot snapshot)
    {
        cursorHuntChancePercent = Math.Clamp(snapshot.CursorHuntChancePercent, 0, 100);
        randomSoundChancePercent = Math.Clamp(snapshot.RandomSoundChancePercent, 0, 100);
        Log($"Settings updated: cursor hunt {cursorHuntChancePercent}%, random sound {randomSoundChancePercent}%");
    }

    private void OnEnergyTick()
    {
        if (isSleeping)
        {
            energy.Restore(EnergyRegenPerSecond);
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
            microphoneListener?.Dispose();
            (soundPlayer as IDisposable)?.Dispose();
            trayIcon?.Dispose();
            trayMenu?.Dispose();
            visibleIcon?.Dispose();
            hiddenIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}
