using System.Globalization;

namespace JokerDBDTracker;

public partial class PlayerWindow
{
    private async Task SyncChatReplayAsync(double position, bool paused)
    {
        if (!_isCommunityChatOpen || CommunityChatBrowser?.CoreWebView2 is null || _isPlayerClosing) return;
        var seconds = Math.Max(0, position).ToString("F3", CultureInfo.InvariantCulture);
        // YouTube's chat iframe consumes playback progress separately from the hidden video.
        var script = $$"""
            (() => {
                const frame = document.querySelector('ytd-live-chat-frame iframe, iframe#chatframe');
                if (!frame?.contentWindow) return false;
                const target = 'https://www.youtube.com';
                frame.contentWindow.postMessage({'yt-iframed-parent-ready': true}, target);
                frame.contentWindow.postMessage({'yt-player-state-change': {{(paused ? 2 : 1)}}}, target);
                frame.contentWindow.postMessage({'yt-player-video-progress': {{seconds}}}, target);
                return true;
            })();
            """;
        await ExecuteCommunityBrowserScriptWithTimeoutAsync(CommunityChatBrowser, script, 500, "ChatReplay.Sync");
    }
}
