using System;
using System.Drawing;

namespace QuackDuck;

internal sealed partial class PetForm
{
    private float DebugSpeedFactor => Math.Max(0.1f, (float)(debugState?.SpeedMultiplier ?? 1d));

    private void HandleDebugControls()
    {
        if (debugState is null || !debugState.DebugEnabled)
        {
            return;
        }

        var desiredMicEnabled = debugState.MicrophoneEnabled;
        var micGain = Math.Max(0.1f, (float)debugState.MicrophoneGain);
        var micThreshold = Math.Clamp((float)debugState.MicrophoneThreshold, 0.001f, 1f);
        microphoneListener?.SetGain(micGain);
        microphoneListener?.SetThreshold(micThreshold);
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

        if (debugState.Scale != scale)
        {
            scale = Math.Max(1, debugState.Scale);
            ClientSize = new Size(skin.FrameWidth * scale, skin.FrameHeight * scale);
            KeepPetInBoundsAndApply(workingArea);
        }

        if (debugState.ConsumeFlip())
        {
            facingRight = !facingRight;
            horizontalVelocity = -horizontalVelocity;
            Log("Flip direction (debug)");
        }

        if (debugState.ConsumeReloadSkin())
        {
            ReloadSkin();
        }

        if (debugState.ConsumeReset())
        {
            targetPoint = null;
            horizontalVelocity = 0;
            verticalVelocity = 0;
            spentForTarget = false;
            wakeRequested = false;
            pendingJump = false;
            energy.Restore(energy.Max);
            stateMachine.ForceState("Idle");
            Log("Reset (debug)");
        }

        var desired = debugState.ConsumeDesiredState();
        if (!string.IsNullOrWhiteSpace(desired))
        {
            try
            {
                stateMachine.ForceState(desired);
                Log($"Force state -> {desired}");
            }
            catch (Exception ex)
            {
                Log($"Force state failed: {ex.Message}");
            }
        }
    }

    private void TrackStateHistory()
    {
        if (debugState is null)
        {
            return;
        }

        var name = stateMachine.CurrentState;
        if (!string.Equals(name, lastStateName, StringComparison.OrdinalIgnoreCase))
        {
            lastStateName = name;
            debugState.CurrentState = name;
            debugState.AppendHistory(name);
        }
    }

    private void UpdateDebugTelemetry()
    {
        if (debugState is null)
        {
            return;
        }

        debugState.CurrentX = screenX;
        debugState.CurrentY = screenY;
        debugState.MicrophoneLevel = lastMicLevel;
    }
}
