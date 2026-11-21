using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using JokerDBDTracker.Services;

namespace JokerDBDTracker.Views;

public partial class PsiIntroView : UserControl, IDisposable
{
    private const double FrameIntervalSeconds = 1.0 / 30;
    private const int PhraseIntervalSeconds = 8;
    private const int ExitDurationMs = 680;
    private static readonly string[] Phrases = ["Контроллер уже рядом.", "Не доверяй тишине.", "Сигнал проходит сквозь стены.", "Ты на частоте Джокера.", "За пределами обычного эфира."];
    private readonly Stopwatch _clock = new();
    private readonly IntroAudioService _audio = new();
    private double _lastFrame;
    private int _lastPhrase;
    private bool _running;
    private bool _exiting;
    private bool _disposed;
    private Window? _hostWindow;
    private Point _pointer;
    private double? _exitAt;
    public bool SoundEnabled { get; private set; }
    public event EventHandler? Entered;
    public event EventHandler? SoundChanged;
    public event EventHandler? RetryRequested;
    public event EventHandler? CloseRequested;

    public PsiIntroView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_hostWindow is not null) return;
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow is not null) _hostWindow.StateChanged += HostStateChanged;
        };
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (!_running) return;
            if (!IsVisible) _audio.Stop();
            else _audio.Start(SoundEnabled);
        };
    }

    public void Start(bool soundEnabled, bool animationsEnabled)
    {
        if (_disposed || _running) return;
        _running = true;
        SoundEnabled = soundEnabled;
        UpdateSoundLabel();
        Atmosphere.Animate = animationsEnabled;
        EnterButton.IsEnabled = true;
        _clock.Restart();
        _audio.Start(soundEnabled);
        if (animationsEnabled)
        {
            SceneImage.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(1400)));
            IntroContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1100)));
            System.Windows.Media.CompositionTarget.Rendering += RenderFrame;
        }
        Atmosphere.InvalidateVisual();
    }

    public void SetCatalogState(bool hasVideos, bool loading)
    {
        ReadyText.Text = loading ? "Библиотека загружается в фоне. Можно войти." : hasVideos
            ? "Библиотека готова. Вход открыт." : "Каталог недоступен. Можно войти или повторить загрузку.";
        RetryButton.Visibility = !loading && !hasVideos ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderFrame(object? sender, EventArgs args)
    {
        var time = _clock.Elapsed.TotalSeconds;
        if (!IsVisible || _hostWindow?.WindowState == WindowState.Minimized || time - _lastFrame < FrameIntervalSeconds) return;
        _lastFrame = time;
        Atmosphere.Time = time;
        if (_exitAt.HasValue) Atmosphere.ExitProgress = Math.Clamp((time - _exitAt.Value) / (ExitDurationMs / 1000.0), 0, 1);
        Atmosphere.InvalidateVisual();
        SceneTranslation.X += (_pointer.X * -8 - SceneTranslation.X) * .06;
        SceneTranslation.Y += (_pointer.Y * -5 - SceneTranslation.Y) * .06;
        RadiationMark.Opacity = .7 + .3 * Math.Pow(Math.Sin(time * .6), 2);
        RadiationRotation.Angle = Math.Sin(time * .23) * 8;
        var phrase = (int)(time / PhraseIntervalSeconds) % Phrases.Length;
        if (!_exiting && phrase != _lastPhrase)
        {
            _lastPhrase = phrase;
            PhraseText.Text = Phrases[phrase];
            PhraseText.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(800)));
            _audio.Speak(Phrases[phrase]);
        }
    }

    private void Scene_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Atmosphere.Animate) return;
        var point = e.GetPosition(SceneRoot);
        _pointer = new Point(point.X / Math.Max(1, ActualWidth) * 2 - 1, point.Y / Math.Max(1, ActualHeight) * 2 - 1);
    }
    private void Scene_MouseLeave(object sender, MouseEventArgs e) => _pointer = new Point();

    private void Enter_Click(object sender, RoutedEventArgs e)
    {
        if (_exiting || !_running) return;
        _exiting = true;
        EnterButton.IsEnabled = false;
        SoundToggle.IsEnabled = false;
        RetryButton.IsEnabled = false;
        _audio.Enter();
        if (!Atmosphere.Animate) { FinishEntry(); return; }
        _exitAt = _clock.Elapsed.TotalSeconds;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ExitDurationMs)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => FinishEntry();
        BeginAnimation(OpacityProperty, fade);
    }

    private void FinishEntry()
    {
        if (_disposed) return;
        Stop();
        Entered?.Invoke(this, EventArgs.Empty);
    }

    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        SoundEnabled = !SoundEnabled;
        if (SoundEnabled) _audio.Start(true); else _audio.Stop();
        UpdateSoundLabel();
        SoundChanged?.Invoke(this, EventArgs.Empty);
    }
    private void UpdateSoundLabel() => SoundToggle.Content = SoundEnabled ? "Звук: вкл" : "Звук: выкл";
    private void HostStateChanged(object? sender, EventArgs e)
    {
        if (!_running || _exiting) return;
        if (_hostWindow?.WindowState == WindowState.Minimized) _audio.Stop();
        else _audio.Start(SoundEnabled);
    }
    private void Retry_Click(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Stop()
    {
        _running = false;
        _clock.Stop();
        System.Windows.Media.CompositionTarget.Rendering -= RenderFrame;
        SceneImage.BeginAnimation(OpacityProperty, null);
        IntroContent.BeginAnimation(OpacityProperty, null);
        PhraseText.BeginAnimation(OpacityProperty, null);
        _audio.Stop();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        BeginAnimation(OpacityProperty, null);
        _audio.Dispose();
        SceneBrush.ImageSource = null;
        if (_hostWindow is not null) _hostWindow.StateChanged -= HostStateChanged;
        _hostWindow = null;
    }
}
