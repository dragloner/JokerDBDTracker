using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace JokerDBDTracker.Services;

/// <summary>Isolated intro ambience and cues; never shares the player's sound/effect state.</summary>
public sealed class IntroAudioService : IDisposable
{
    private const double AmbienceVolume = .16;
    private const double CueVolume = .22;
    private const int AsyncSpeechWithPurge = 3;
    private readonly MediaPlayer _ambience = new();
    private readonly MediaPlayer _cue = new();
    private object? _voice;
    private bool _voiceChecked;
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
        Speak("Пси-поле активно. Джокер на связи.");
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

    public void Speak(string phrase)
    {
        if (!_enabled || _disposed) return;
        try
        {
            if (!_voiceChecked)
            {
                _voiceChecked = true;
                var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (type is null) return;
                _voice = Activator.CreateInstance(type);
                dynamic voice = _voice!;
                object tokens = voice.GetVoices("Language=419", "");
                try
                {
                    dynamic voices = tokens;
                    if ((int)voices.Count == 0)
                    {
                        ReleaseVoice();
                        DiagnosticsService.LogInfo("Intro.Speech", "No Russian SAPI voice installed; phrases remain visible as text.");
                        return;
                    }
                    object token = voices.Item(0);
                    try { voice.Voice = token; }
                    finally { if (Marshal.IsComObject(token)) Marshal.FinalReleaseComObject(token); }
                    voice.Volume = 24;
                    voice.Rate = -1;
                }
                finally { if (Marshal.IsComObject(tokens)) Marshal.FinalReleaseComObject(tokens); }
            }
            if (_voice is not null) ((dynamic)_voice).Speak(phrase, AsyncSpeechWithPurge);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogException("Intro.Speech", ex);
            ReleaseVoice();
        }
    }

    public void Enter()
    {
        if (!_enabled || _disposed) return;
        Open(_cue, "psi-enter.wav", CueVolume);
        StopSpeech();
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
        StopSpeech();
    }
    private void StopSpeech()
    {
        if (_voice is not null)
        {
            try { ((dynamic)_voice).Speak(string.Empty, AsyncSpeechWithPurge); }
            catch (Exception ex) { DiagnosticsService.LogException("Intro.Speech.Stop", ex); }
        }
    }
    private void ReleaseVoice()
    {
        if (_voice is not null && Marshal.IsComObject(_voice)) Marshal.FinalReleaseComObject(_voice);
        _voice = null;
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
        ReleaseVoice();
    }
}
