using JokerDBDTracker.Services;
using Microsoft.Web.WebView2.Core;

namespace JokerDBDTracker;

public partial class PlayerWindow
{
    private static string BuildMediaDiagnosticsScript() => """
        (() => {
            if (window !== window.top || location.hostname !== 'www.youtube.com' ||
                location.pathname !== '/watch' || window.__jdbdMediaDiagnosticsInstalled) return;
            window.__jdbdMediaDiagnosticsInstalled = true;
            for (const kind of ['waiting', 'stalled', 'error', 'playing', 'pause', 'ended', 'seeking', 'seeked']) {
                document.addEventListener(kind, event => {
                    const video = event.target;
                    if (!(video instanceof HTMLVideoElement)) return;
                    window.chrome?.webview?.postMessage('jdbd:media:' + JSON.stringify({
                        event: kind, current: video.currentTime, ready: video.readyState,
                        network: video.networkState, paused: video.paused, seeking: video.seeking,
                        error: video.error?.code || 0,
                        buffered: Array.from({ length: Math.min(video.buffered.length, 3) }, (_, i) =>
                            [video.buffered.start(i), video.buffered.end(i)])
                    }));
                }, true);
            }
        })();
        """;

    private void CoreWebView2_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (e.Response.StatusCode < 400 || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host == "www.youtube.com" && uri.AbsolutePath.StartsWith("/youtubei/v1/player", StringComparison.Ordinal))
        {
            // Never record signed media URLs, cookies or authentication headers.
            DiagnosticsService.LogInfo("Player.MediaNetwork", $"HTTP {e.Response.StatusCode}, host={uri.Host}");
        }
    }
}
