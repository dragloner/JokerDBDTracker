using System.Text.Json;

namespace JokerDBDTracker;

public partial class PlayerWindow
{
    private const string VisibleConsentExpression = """
        (() => Array.from(document.querySelectorAll([
            'ytd-consent-bump-v2-lightbox', 'yt-consent-bump-v2-renderer',
            'form[action*="consent.youtube.com"]', 'form[action*="consent.google.com"]',
            'iframe[src*="consent.youtube.com"]', 'iframe[src*="consent.google.com"]'
        ].join(','))).some(node => {
            const style = getComputedStyle(node);
            return !node.hidden && style.display !== 'none' && style.visibility !== 'hidden' && node.getClientRects().length > 0;
        }))()
        """;

    private static string BuildPlayerPageStateScript(string videoId) => $$"""
        (() => {
            if ({{BuildPlayerDocumentPredicate(videoId)}}) return 'ready';
            const interactiveHost = ['consent.youtube.com', 'consent.google.com', 'accounts.google.com'].includes(location.hostname);
            const modal = {{VisibleConsentExpression}};
            return interactiveHost || modal || location.pathname === '/signin' ? 'interactive' : 'pending';
        })()
        """;

    private static string BuildPlayerLayoutProbeScript() => """
        (() => {
            const player = document.getElementById('movie_player');
            const container = player?.querySelector('.html5-video-container');
            const video = container?.querySelector('video');
            if (!document.getElementById('jdbd-player-layout') || !player || !container || !video) return false;
            const width = document.documentElement.clientWidth;
            const height = document.documentElement.clientHeight;
            if (width < 120 || height < 68) return false;
            const fits = node => {
                const rect = node.getBoundingClientRect();
                return Math.abs(rect.left) <= 1 && Math.abs(rect.top) <= 1 &&
                    Math.abs(rect.width - width) <= 1 && Math.abs(rect.height - height) <= 1;
            };
            return fits(player) && fits(container) && fits(video) && getComputedStyle(video).objectFit === 'contain';
        })()
        """;

    private async Task<bool> WaitForPlayerLayoutAsync(int navigationVersion)
    {
        const int maximumProbes = 60;
        const int probeIntervalMs = 100;
        var probe = BuildPlayerLayoutProbeScript();
        for (var attempt = 0; attempt < maximumProbes; attempt++)
        {
            if (_isPlayerClosing || navigationVersion != _playerNavigationVersion) return false;
            if (await ExecuteWebScriptWithTimeoutAsync(probe, 1500, "Player.Layout") == "true") return true;
            await Task.Delay(probeIntervalMs);
        }
        return false;
    }

    // Shared by the early CSS bootstrap and the native startup sequence. Consent and
    // account pages must retain their own layout, input handling and scrolling.
    private static string BuildPlayerDocumentPredicate(string videoId) => $$"""
        (() => {
            if (window !== window.top || location.protocol !== 'https:' ||
                location.hostname !== 'www.youtube.com' || location.pathname !== '/watch' ||
                new URLSearchParams(location.search).get('v') !== {{JsonSerializer.Serialize(videoId)}} ||
                document.readyState !== 'complete') return false;

            return !{{VisibleConsentExpression}};
        })()
        """;

    private async Task<bool> WaitForPlayerDocumentAsync(int navigationVersion)
    {
        const int documentProbeIntervalMs = 250;
        var predicate = BuildPlayerPageStateScript(_video.VideoId);
        while (!_isPlayerClosing && navigationVersion == _playerNavigationVersion)
        {
            var result = await ExecuteWebScriptWithTimeoutAsync(predicate, 1500, "Player.DocumentPolicy");
            if (_isPlayerClosing || navigationVersion != _playerNavigationVersion) return false;
            if (result == "\"ready\"")
            {
                SetPlayerSurfaceVisible(false);
                SetPlayerLoadingOverlay(true);
                return true;
            }
            var needsInteraction = result == "\"interactive\"";
            SetPlayerSurfaceVisible(needsInteraction);
            SetPlayerLoadingOverlay(!needsInteraction);
            await Task.Delay(documentProbeIntervalMs);
        }
        return false;
    }
}
