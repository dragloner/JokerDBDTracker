using System.IO;
using System.Windows.Media;

namespace JokerDBDTracker.Services;

/// <summary>Isolated intro ambience and cues; never shares the player's sound/effect state.</summary>
public sealed class IntroAudioService : IDisposable
{
    private const double AmbienceVolume = .24;
    private const double CueVolume = .22;
    private readonly MediaPlayer _ambience = new();
    private readonly MediaPlayer _cue = new();
    private bool _enabled;
    private bool _disposed;

    public IntroAudioService()
    {
        _ambience.MediaEnded += AmbienceEnded;
        _ambience.MediaFailed += MediaFailed;
        _cue.MediaFailed += MediaFailed;
    }

    public void Start(bool enabled)
    {
        if (_disposed || !enabled || _enabled) return;
        _enabled = true;
        Open(_ambience, "psi-ambience.wav", AmbienceVolume);
        Open(_cue, "psi-wake.wav", CueVolume);
    }

    private static void Open(MediaPlayer player, string filename, double volume)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Intro", filename);
        if (!File.Exists(path))
        {
            DiagnosticsService.LogInfo("Intro.Audio", $"Missing intro sound: {filename}");
            return;
        }
        try
        {
            player.Volume = volume;
            player.Open(new Uri(path));
            player.Play();
        }
        catch (Exception ex) { DiagnosticsService.LogException("Intro.Audio.Open", ex); }
    }

    public void Enter()
    {
        if (!_enabled || _disposed) return;
        Open(_cue, "psi-enter.wav", CueVolume);
    }

    private void AmbienceEnded(object? sender, EventArgs args)
    {
        if (!_enabled || _disposed) return;
        _ambience.Position = TimeSpan.Zero;
        _ambience.Play();
    }
    private static void MediaFailed(object? sender, System.Windows.Media.ExceptionEventArgs args) => DiagnosticsService.LogException("Intro.Audio", args.ErrorException);
    public void Stop()
    {
        if (_disposed) return;
        _enabled = false;
        _ambience.Stop();
        _cue.Stop();
    }
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _ambience.MediaEnded -= AmbienceEnded;
        _ambience.MediaFailed -= MediaFailed;
        _cue.MediaFailed -= MediaFailed;
        _ambience.Close();
        _cue.Close();
    }
}
