using System;
using System.Drawing;

namespace QuackDuck;

internal sealed partial class PetForm
{
    private float DebugSpeedFactor => Math.Max(0.1f, (float)(debugState.SpeedMultiplier));

    private void HandleDebugControls()
    {
        ApplyLiveSettingsFromState();

        if (!debugState.DebugEnabled)
        {
            return;
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
            travelActive = false;
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

        if (debugState.ConsumeCursorHuntRequest())
        {
            try
            {
                stateMachine.ForceState("CursorHunt");
                Log("Force state -> CursorHunt (debug trigger)");
            }
            catch (Exception ex)
            {
                Log($"Force CursorHunt failed: {ex.Message}");
            }
        }

        var desiredEnergy = debugState.ConsumeRequestedEnergy();
        if (desiredEnergy.HasValue)
        {
            energy.Set(desiredEnergy.Value);
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
        debugState.EnergyCurrent = energy.Current;
        debugState.EnergyMax = energy.Max;
    }

    private void LogStateChange(string from, string to)
    {
        Log($"State: {from} -> {to}");
    }
}
