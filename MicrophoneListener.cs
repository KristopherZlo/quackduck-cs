using System;
using NAudio.Wave;

namespace QuackDuck;

/// <summary>
/// Lightweight RMS listener that raises callbacks when microphone input crosses a threshold.
/// </summary>
internal sealed class MicrophoneListener : IDisposable
{
    private readonly Action onSoundStarted;
    private readonly Action onSoundStopped;
    private readonly Action<float>? onLevelChanged;
    private float threshold;
    private readonly TimeSpan releaseHold;
    private readonly int bufferMilliseconds;
    private WaveInEvent? capture;
    private bool disposed;
    private DateTime lastAboveThreshold = DateTime.MinValue;
    private bool currentlyHearing;
    private float gain = 1f;

    internal MicrophoneListener(Action onSoundStarted, Action onSoundStopped, Action<float>? onLevelChanged = null, float threshold = 0.08f, int bufferMilliseconds = 120, TimeSpan? releaseHold = null)
    {
        this.onSoundStarted = onSoundStarted ?? throw new ArgumentNullException(nameof(onSoundStarted));
        this.onSoundStopped = onSoundStopped ?? throw new ArgumentNullException(nameof(onSoundStopped));
        this.onLevelChanged = onLevelChanged;
        this.threshold = threshold;
        this.bufferMilliseconds = bufferMilliseconds;
        this.releaseHold = releaseHold ?? TimeSpan.FromMilliseconds(500);
    }

    internal bool IsRunning => capture is not null;

    internal void SetGain(float value)
    {
        gain = Math.Max(0.1f, value);
    }

    internal void SetThreshold(float value)
    {
        threshold = Math.Clamp(value, 0.001f, 1f);
    }

    internal void Start()
    {
        if (disposed || capture is not null)
        {
            return;
        }

        try
        {
            capture = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1),
                BufferMilliseconds = bufferMilliseconds
            };
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += (_, _) => StopInternal();
            capture.StartRecording();
        }
        catch
        {
            StopInternal();
            throw;
        }
    }

    internal void Stop()
    {
        StopInternal();
    }

    private void StopInternal()
    {
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.StopRecording();
            capture.Dispose();
        }
        catch
        {
            // ignore teardown errors
        }

        capture = null;
        currentlyHearing = false;
        lastAboveThreshold = DateTime.MinValue;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (disposed)
        {
            return;
        }

        var max = CalculateMaxAmplitude(e.Buffer, e.BytesRecorded) * gain;
        onLevelChanged?.Invoke(max);
        var now = DateTime.UtcNow;

        if (max >= threshold)
        {
            lastAboveThreshold = now;
            if (!currentlyHearing)
            {
                currentlyHearing = true;
                onSoundStarted();
            }
            return;
        }

        if (currentlyHearing && now - lastAboveThreshold > releaseHold)
        {
            currentlyHearing = false;
            onSoundStopped();
        }
    }

    private static float CalculateMaxAmplitude(byte[] buffer, int bytesRecorded)
    {
        // 16-bit PCM expected
        var max = 0f;
        for (int index = 0; index < bytesRecorded; index += 2)
        {
            var sample = BitConverter.ToInt16(buffer, index);
            var amplitude = Math.Abs(sample / 32768f);
            if (amplitude > max)
            {
                max = amplitude;
            }
        }

        return max;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopInternal();
    }
}
