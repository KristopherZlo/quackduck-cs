using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace QuackDuck;

/// <summary>
/// Minimal sound effect player that supports wav/mp3 files via NAudio.
/// Playback runs on a background task so UI threads remain responsive.
/// </summary>
internal sealed class SoundEffectPlayer : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private bool disposed;

    internal Task PlayAsync(string path)
    {
        if (disposed || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => PlayInternal(path, cancellation.Token), cancellation.Token);
    }

    private static void PlayInternal(string path, CancellationToken token)
    {
        try
        {
            using var audioFile = new AudioFileReader(path);
            using var output = new WaveOutEvent();
            output.Init(audioFile);
            output.Play();

            while (output.PlaybackState == PlaybackState.Playing && !token.IsCancellationRequested)
            {
                Thread.Sleep(50);
            }
        }
        catch
        {
            // Swallow playback errors so the pet keeps running.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
