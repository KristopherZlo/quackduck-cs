using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
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
    private readonly DebugState debugState;
    private PetSkin skin = null!;
    private PetAnimator animator = null!;
    private PetStateMachine stateMachine = null!;
    private NameOverlayForm? nameOverlay;
    private CancellationTokenSource? cursorKnockbackCts;
    private Task? cursorKnockbackTask;
    private Rectangle workingArea;
    private string currentSkinName = "default";
    private string? customSkinRoot;

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
    private bool soundEffectsEnabled = true;
    private double soundEffectsVolume = 1.0;
    private bool autostartApplied;
    private string appliedLanguage = "English";
    private int selectedMicDeviceIndex = -1;
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
        debugState = new DebugState
        {
            Scale = scale,
            DebugEnabled = settings.Debug,
            PetName = "Quack",
            SelectedSkin = settings.Skin
        };
        SettingsStorage.LoadInto(debugState);
        this.scale = debugState.Scale;
        debugState.MicrophoneThreshold = debugState.ActivationThresholdPercent / 100.0;
        debugState.SelectedPetSize = string.IsNullOrWhiteSpace(debugState.SelectedPetSize)
            ? this.scale switch
            {
                1 => "Small",
                2 => "Medium",
                _ => "Big"
            }
            : debugState.SelectedPetSize;
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
            currentSkinName = string.IsNullOrWhiteSpace(debugState.SelectedSkin) ? settings.Skin : debugState.SelectedSkin;
            debugState.SelectedSkin = currentSkinName;
            customSkinRoot = debugState.SkinsFolderPath;
            skin = PetSkin.Load(currentSkinName, customSkinRoot);
            animator = new PetAnimator(skin, PetAnimator.DefaultFrameDurationMs);
            scale = debugState.Scale;
            ClientSize = new Size(skin.FrameWidth * scale, skin.FrameHeight * scale);
            soundEffectsEnabled = debugState.SoundEffectsEnabled;
            soundEffectsVolume = Math.Clamp(debugState.EffectsVolumePercent / 100d, 0d, 1d);
            soundPlayer.Enabled = soundEffectsEnabled;
            soundPlayer.Volume = soundEffectsVolume;
            appliedLanguage = debugState.SelectedLanguage;
            ApplyLanguage(appliedLanguage);

            workingArea = Screen.FromControl(this).WorkingArea;
            screenX = workingArea.Left + (workingArea.Width - ClientSize.Width) / 2f;
            screenY = workingArea.Top;
            KeepPetInBoundsAndApply(workingArea);

            PopulateInputDevices();
            selectedMicDeviceIndex = ResolveMicDeviceIndex(debugState.SelectedInputDevice);
            microphoneEnabled = debugState.MicrophoneEnabled;
            debugState.MicrophoneThreshold = Math.Clamp(debugState.MicrophoneThreshold, 0.001, 1.0);
            InitializeMicrophone();

            stateMachine = BuildStateMachine();
            animationTimer.Start();
            energyTimer.Start();
            if (debugState is not null)
            {
                debugState.CurrentState = stateMachine.CurrentState;
                debugState.AppendHistory(stateMachine.CurrentState);
            }

            nameOverlay = new NameOverlayForm();
            UpdateNameOverlay();

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
        UpdateNameOverlay();
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

        ApplySkin(debugState.SelectedSkin);
        Log("Skin reloaded (debug)");
    }

    private void ApplySkin(string? skinName)
    {
        var desired = string.IsNullOrWhiteSpace(skinName) ? "default" : skinName;
        try
        {
            skin?.Dispose();
        }
        catch
        {
        }

        try
        {
            skin = PetSkin.Load(desired, customSkinRoot);
            currentSkinName = desired;
        }
        catch (Exception ex)
        {
            Log($"Skin '{desired}' failed to load: {ex.Message}. Falling back to default.");
            try
            {
                desired = "default";
                skin = PetSkin.Load(desired, customSkinRoot);
                currentSkinName = desired;
            }
            catch (Exception fallbackEx)
            {
                Log($"Default skin failed to load: {fallbackEx.Message}");
                throw;
            }
        }

        animator = new PetAnimator(skin, PetAnimator.DefaultFrameDurationMs);
        debugState.SelectedSkin = currentSkinName;
        ClientSize = new Size(skin.FrameWidth * scale, skin.FrameHeight * scale);
        KeepPetInBoundsAndApply(workingArea);
    }

    private void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void ApplyLanguage(string language)
    {
        var culture = string.Equals(language, "Russian", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("ru-RU")
            : new CultureInfo("en-US");

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch
        {
        }
    }

    private void UpdateNameOverlay()
    {
        if (nameOverlay is null || debugState is null)
        {
            return;
        }

        if (isPausedHidden || !debugState.ShowName)
        {
            if (nameOverlay.Visible)
            {
                nameOverlay.Hide();
            }
            return;
        }

        var name = string.IsNullOrWhiteSpace(debugState.PetName) ? "Quack" : debugState.PetName;
        var fontSize = (float)Math.Clamp(debugState.FontSize, 8, 64);
        nameOverlay.UpdateContent(name, fontSize);

        var petLocation = new Point((int)Math.Round(screenX), (int)Math.Round(screenY));
        nameOverlay.UpdatePosition(petLocation, ClientSize.Width, debugState.NameOffset);
        if (!nameOverlay.Visible)
        {
            nameOverlay.Show();
        }
    }

    private void ApplyLiveSettingsFromState()
    {
        debugState.PetSpeed = Math.Max(0.1, debugState.PetSpeed);
        debugState.SpeedMultiplier = debugState.PetSpeed;

        var desiredScale = MapSizeToScale(debugState.SelectedPetSize);
        if (desiredScale != scale)
        {
            scale = desiredScale;
            ClientSize = new Size(skin.FrameWidth * scale, skin.FrameHeight * scale);
            KeepPetInBoundsAndApply(workingArea);
        }

        var desiredMicEnabled = debugState.MicrophoneEnabled;
        var micGain = Math.Max(0.1f, (float)debugState.MicrophoneGain);
        var micThreshold = Math.Clamp((float)debugState.MicrophoneThreshold, 0.001f, 1f);
        microphoneListener?.SetGain(micGain);
        microphoneListener?.SetThreshold(micThreshold);
        var newDeviceIndex = ResolveMicDeviceIndex(debugState.SelectedInputDevice);
        if (newDeviceIndex != selectedMicDeviceIndex)
        {
            selectedMicDeviceIndex = newDeviceIndex;
            microphoneListener?.SetDeviceIndex(selectedMicDeviceIndex);
        }
        if (desiredMicEnabled != microphoneEnabled)
        {
            microphoneEnabled = desiredMicEnabled;
            if (microphoneEnabled)
            {
                TryStartMicrophone();
            }
            else
            {
                StopMicrophone();
                isHearingSound = false;
            }
        }

        var desiredSoundEnabled = debugState.SoundEffectsEnabled;
        if (desiredSoundEnabled != soundEffectsEnabled)
        {
            soundEffectsEnabled = desiredSoundEnabled;
            soundPlayer.Enabled = soundEffectsEnabled;
        }

        var desiredVolume = Math.Clamp(debugState.EffectsVolumePercent / 100d, 0d, 1d);
        if (Math.Abs(desiredVolume - soundEffectsVolume) > 0.001d)
        {
            soundEffectsVolume = desiredVolume;
            soundPlayer.Volume = soundEffectsVolume;
        }

        if (debugState.Autostart != autostartApplied)
        {
            try
            {
                AutostartManager.SetEnabled("QuackDuck", Application.ExecutablePath, debugState.Autostart);
                autostartApplied = debugState.Autostart;
            }
            catch
            {
            }
        }

        if (!string.Equals(appliedLanguage, debugState.SelectedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            appliedLanguage = debugState.SelectedLanguage;
            ApplyLanguage(appliedLanguage);
        }

        var desiredSkinRoot = debugState.SkinsFolderPath;
        var desiredSkin = debugState.SelectedSkin;
        if (!string.Equals(customSkinRoot ?? string.Empty, desiredSkinRoot ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentSkinName, desiredSkin, StringComparison.OrdinalIgnoreCase))
        {
            customSkinRoot = desiredSkinRoot;
            ApplySkin(desiredSkin);
        }
    }

    private int MapSizeToScale(string size)
    {
        return size?.ToLowerInvariant() switch
        {
            "small" => 1,
            "big" => 3,
            _ => 2
        };
    }

    private void PopulateInputDevices()
    {
        try
        {
            var devices = new List<string> { "Default microphone" };
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var info = WaveInEvent.GetCapabilities(i);
                var name = string.IsNullOrWhiteSpace(info.ProductName) ? $"Device {i}" : info.ProductName;
                devices.Add(name);
            }

            debugState.SetInputDevices(devices);
        }
        catch
        {
        }
    }

    private int ResolveMicDeviceIndex(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Default microphone", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var info = WaveInEvent.GetCapabilities(i);
                var deviceName = string.IsNullOrWhiteSpace(info.ProductName) ? $"Device {i}" : info.ProductName;
                if (string.Equals(deviceName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }
        catch
        {
        }

        return -1;
    }

    private void OpenSettings()
    {
        SettingsApp.SharedState = debugState;
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
            SettingsStorage.Save(debugState);
            skin?.Dispose();
            microphoneListener?.Dispose();
            (soundPlayer as IDisposable)?.Dispose();
            trayIcon?.Dispose();
            trayMenu?.Dispose();
            visibleIcon?.Dispose();
            hiddenIcon?.Dispose();
            nameOverlay?.Dispose();
            cursorKnockbackCts?.Cancel();
            cursorKnockbackCts?.Dispose();
        }

        base.Dispose(disposing);
    }
}
