using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using JokerDBDTracker;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var browser = new WebView2 { Visibility = Visibility.Hidden };
            var window = new Window { Content = browser, Width = 960, Height = 600, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                foreach (var (filename, expectedSeconds) in new[] { ("psi-ambience.wav", 24d), ("psi-wake.wav", 2d), ("psi-enter.wav", .8d) })
                {
                    var audio = new System.Windows.Media.MediaPlayer { IsMuted = true };
                    var opened = new TaskCompletionSource();
                    audio.MediaOpened += (_, _) => opened.TrySetResult();
                    audio.MediaFailed += (_, args) => opened.TrySetException(args.ErrorException);
                    try
                    {
                        audio.Open(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Intro", filename)));
                        await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        if (!audio.HasAudio || !audio.NaturalDuration.HasTimeSpan || Math.Abs(audio.NaturalDuration.TimeSpan.TotalSeconds - expectedSeconds) > .05)
                            throw new Exception($"Invalid intro audio: {filename}");
                        audio.Play();
                        await Task.Delay(100);
                        audio.Stop();
                        Console.WriteLine($"PASS Windows MediaPlayer opens and plays muted {filename}");
                    }
                    finally { audio.Close(); }
                }
                window.Show();
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.GetFullPath("artifacts/player-layout-integration"));
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.IsMuted = true;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                var loaded = new TaskCompletionSource();
                browser.CoreWebView2.NavigationCompleted += (_, _) => loaded.TrySetResult();
                browser.NavigateToString("""
                    <!doctype html><html><body style="margin:40px"><header>Page chrome</header>
                    <div id="movie_player" style="width:320px;height:200px;position:relative">
                    <div class="html5-video-container" style="left:60px;top:30px;width:320px;height:180px">
                    <video class="html5-main-video" style="width:320px;height:180px;left:60px;top:30px"></video>
                    </div></div></body></html>
                    """);
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(12));
                var css = (string)typeof(PlayerWindow).GetField("PlayerLayoutCss", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
                var probe = (string)typeof(PlayerWindow).GetMethod("BuildPlayerLayoutProbeScript", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
                await browser.CoreWebView2.ExecuteScriptAsync($"const s=document.createElement('style');s.id='jdbd-player-layout';s.textContent={JsonSerializer.Serialize(css)};document.head.appendChild(s)");
                async Task CheckLayout(string scenario)
                {
                    for (var i = 0; i < 30; i++)
                    {
                        if (await browser.CoreWebView2.ExecuteScriptAsync(probe) == "true")
                        {
                            Console.WriteLine("PASS " + scenario);
                            return;
                        }
                        await Task.Delay(100);
                    }
                    var state = await browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({width:innerWidth,height:innerHeight,video:document.querySelector('video').getBoundingClientRect().toJSON()})");
                    throw new Exception(scenario + ": " + state);
                }
                await CheckLayout("Hidden WebView has the final viewport and contain aspect ratio");
                await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').style.cssText='width:300px;height:140px;left:70px;top:80px';document.querySelector('.html5-video-container').style.cssText='width:300px;height:140px;left:70px;top:80px'");
                await CheckLayout("Delayed site inline sizing cannot reintroduce offsets or bars");
                window.Width = 1220;
                window.Height = 720;
                window.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await CheckLayout("Resize while hidden keeps player, container and video aligned");
                browser.Visibility = Visibility.Visible;
                await CheckLayout("Revealed video retains the same geometry without fullscreen clicks");
                browser.Visibility = Visibility.Hidden;
                var intro = new JokerDBDTracker.Views.PsiIntroView();
                var entered = new TaskCompletionSource();
                var entryCount = 0;
                intro.Entered += (_, _) => { entryCount++; intro.Dispose(); entered.TrySetResult(); };
                window.Content = intro;
                intro.Start(false, true);
                intro.SetCatalogState(true, false);
                await Task.Delay(250);
                var enterButton = (System.Windows.Controls.Button)intro.FindName("EnterButton");
                enterButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                enterButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                if (entryCount != 1) throw new Exception("Repeated intro entry was not guarded");
                var brush = (System.Windows.Media.ImageBrush)intro.FindName("SceneBrush");
                if (brush.ImageSource is not null) throw new Exception("Intro artwork retained after disposal");
                Console.WriteLine("PASS animated intro entry is single-shot and releases its artwork/resources");
                Environment.ExitCode = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL " + ex); Environment.ExitCode = 1; }
            finally { browser.Dispose(); window.Close(); app.Shutdown(); }
        };
        app.Run();
    }
}
