using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
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
            var browser = new WebView2();
            var window = new Window { Content = browser, Width = 320, Height = 160, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.GetFullPath("artifacts/web-audio-check-data"));
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.IsMuted = true;
                var loaded = new TaskCompletionSource();
                browser.CoreWebView2.NavigationCompleted += (_, _) => loaded.TrySetResult();
                browser.NavigateToString("<!doctype html><html><body>Local audio integration test</body></html>");
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var parametersType = typeof(PlayerWindow).GetNestedType("SoundEffectAudioParams", BindingFlags.NonPublic)!;
                var parameters = Activator.CreateInstance(parametersType, true)!;
                parametersType.GetProperty("PitchSemitones")!.SetValue(parameters, 12d);
                parametersType.GetProperty("Reverb")!.SetValue(parameters, 0.5d);
                parametersType.GetProperty("EqLowDb")!.SetValue(parameters, 3d);
                var builder = typeof(PlayerWindow).GetMethod("BuildSoundEffectScript", BindingFlags.Static | BindingFlags.NonPublic)!;
                var file = Directory.GetFiles("Assets/Sounds", "*.mp3").First(path => Path.GetFileName(path).Contains("aura", StringComparison.OrdinalIgnoreCase));
                var data = Convert.ToBase64String(await File.ReadAllBytesAsync(file));
                async Task Evaluate(string script)
                {
                    var result = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                        JsonSerializer.Serialize(new { expression = script, awaitPromise = true, userGesture = true, returnByValue = true }))
                        .WaitAsync(TimeSpan.FromSeconds(20));
                    using var json = JsonDocument.Parse(result);
                    if (json.RootElement.TryGetProperty("exceptionDetails", out _)) throw new Exception(result);
                }
                await Evaluate((string)builder.Invoke(null, new object[] { "AuraFarm", data, false, parameters })!);
                var active = await browser.CoreWebView2.ExecuteScriptAsync("Object.values(window.__jdbdSfx.playing).map(e => ({rate:e.source.playbackRate.value,state:e.ctx.state}))");
                if (!active.Contains("\"rate\":2") || !active.Contains("running")) throw new Exception("Sound did not start: " + active);
                var sync = typeof(PlayerWindow).GetMethod("BuildSoundParamsScript", BindingFlags.Static | BindingFlags.NonPublic)!;
                await Evaluate((string)sync.Invoke(null, new object[] { "{}" })!);
                var reset = await browser.CoreWebView2.ExecuteScriptAsync("Object.values(window.__jdbdSfx.playing).map(e => e.source.playbackRate.value)");
                if (reset != "[1]") throw new Exception("Reset failed: " + reset);
                Console.WriteLine("PASS real WebView2: MP3 decode, native activation, running Web Audio, live pitch reset; output muted.");
                var stopAll = (string)typeof(PlayerWindow).GetMethod("BuildStopAllSoundEffectsScript", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                await Evaluate(stopAll);
                var dissBytes = await File.ReadAllBytesAsync("Assets/Sounds/diss.mp3");
                using (var embedded = Application.GetResourceStream(new Uri("pack://application:,,,/JokerDBDTracker;component/Assets/Sounds/diss.mp3"))!.Stream)
                {
                    var embeddedHash = System.Security.Cryptography.SHA256.HashData(embedded);
                    if (!embeddedHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(dissBytes))) throw new Exception("Embedded diss differs from the supplied final file");
                }
                var diss = Convert.ToBase64String(dissBytes);
                await Evaluate((string)builder.Invoke(null, new object[] { "Diss", diss, false, parameters })!);
                var dissActive = await browser.CoreWebView2.ExecuteScriptAsync("Object.values(window.__jdbdSfx.playing).some(e=>e.kind==='Diss' && e.ctx.state==='running')");
                if (dissActive != "true") throw new Exception("Diss did not decode or start");
                await Evaluate(stopAll);
                var stopped = await browser.CoreWebView2.ExecuteScriptAsync("Object.keys(window.__jdbdSfx.playing).length===0 && window.__jdbdSfx.count===0 && window.__jdbdSfx.startGen.Diss>0");
                if (stopped != "true") throw new Exception("Diss was not stopped by the shared reset");
                Console.WriteLine("PASS supplied diss MP3: decode, start and shared reset/cancellation; output muted.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { browser.Dispose(); window.Close(); app.Shutdown(); }
        };
        app.Run();
    }
}
