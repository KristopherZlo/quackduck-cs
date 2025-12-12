using System;

namespace QuackDuck;

internal sealed partial class PetForm
{
    private void InitializeMicrophone()
    {
        microphoneListener?.Dispose();
        microphoneListener = new MicrophoneListener(NotifySoundStarted, NotifySoundStopped, level => lastMicLevel = level);
        microphoneListener.SetGain((float)(debugState?.MicrophoneGain ?? 1.0));
        microphoneListener.SetThreshold((float)(debugState?.MicrophoneThreshold ?? 0.08));
        microphoneListener.SetDeviceIndex(selectedMicDeviceIndex);
        TryStartMicrophone();
    }

    private void TryStartMicrophone()
    {
        if (!microphoneEnabled || microphoneListener is null || microphoneListener.IsRunning)
        {
            return;
        }

        try
        {
            microphoneListener.Start();
            Log("Microphone listening started");
        }
        catch (Exception ex)
        {
            microphoneEnabled = false;
            if (debugState is not null)
            {
                debugState.MicrophoneEnabled = false;
            }
            Log($"Microphone start failed: {ex.Message}");
        }
    }

    private void StopMicrophone()
    {
        try
        {
            microphoneListener?.Stop();
            Log("Microphone listening stopped");
        }
        catch
        {
            // ignore stop failures
        }

        lastMicLevel = 0;
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
}
