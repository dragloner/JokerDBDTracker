using System.Text.Json;

namespace JokerDBDTracker;

public partial class PlayerWindow
{
    // Shared by the early CSS bootstrap and the native startup sequence. Consent and
    // account pages must retain their own layout, input handling and scrolling.
    private static string BuildPlayerDocumentPredicate(string videoId) => $$"""
        (() => {
            if (window !== window.top || location.protocol !== 'https:' ||
                location.hostname !== 'www.youtube.com' || location.pathname !== '/watch' ||
                new URLSearchParams(location.search).get('v') !== {{JsonSerializer.Serialize(videoId)}} ||
                document.readyState !== 'complete') return false;

            const consentSelectors = [
                'ytd-consent-bump-v2-lightbox', 'yt-consent-bump-v2-renderer',
                'form[action*="consent.youtube.com"]', 'form[action*="consent.google.com"]',
                'iframe[src*="consent.youtube.com"]', 'iframe[src*="consent.google.com"]'
            ];
            return !Array.from(document.querySelectorAll(consentSelectors.join(','))).some(node => {
                const style = getComputedStyle(node);
                return !node.hidden && style.display !== 'none' && style.visibility !== 'hidden' &&
                    node.getClientRects().length > 0;
            });
        })()
        """;

    private async Task<bool> WaitForPlayerDocumentAsync(int navigationVersion)
    {
        const int documentProbeIntervalMs = 250;
        SetPlayerSurfaceVisible(true);
        SetPlayerLoadingOverlay(false);
        var predicate = BuildPlayerDocumentPredicate(_video.VideoId);
        while (!_isPlayerClosing && navigationVersion == _playerNavigationVersion)
        {
            var result = await ExecuteWebScriptWithTimeoutAsync(predicate, 1500, "Player.DocumentPolicy");
            if (_isPlayerClosing || navigationVersion != _playerNavigationVersion) return false;
            if (result == "true") return true;
            await Task.Delay(documentProbeIntervalMs);
        }
        return false;
    }
}
