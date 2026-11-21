using System.Text.Json;
using System.Windows.Input;

namespace JokerDBDTracker;

public partial class PlayerWindow
{
    // Layout belongs to CSS. Do not call YouTube's private resize methods, click
    // its controls, rewrite its state classes or emit synthetic lifecycle events.
    private const string PlayerLayoutCss = """
        html, body { margin: 0 !important; padding: 0 !important;
            background: #000 !important; overflow: hidden !important; }
        #movie_player { position: fixed !important; inset: 0 !important;
            width: 100vw !important; height: 100vh !important;
            max-width: none !important; max-height: none !important; z-index: 9000 !important; }
        #movie_player .html5-video-container { position: absolute !important; inset: 0 !important;
            width: 100% !important; height: 100% !important; margin: 0 !important; padding: 0 !important; }
        #movie_player video.html5-main-video { width: 100% !important; height: 100% !important;
            max-width: none !important; max-height: none !important; position: absolute !important;
            left: 0 !important; top: 0 !important; margin: 0 !important; object-fit: contain !important; }
        ytd-masthead, #masthead-container, #secondary, #related,
        ytd-watch-metadata, #comments, ytd-comments, ytd-live-chat-frame,
        ytd-engagement-panel-section-list-renderer { display: none !important; }
        #movie_player .ytp-title, #movie_player .ytp-cards-teaser,
        #movie_player .ytp-comments-header-renderer,
        #movie_player .ytp-comments-trigger { display: none !important; }
        """;

    private static string BuildEarlyKioskBootstrapScript(string videoId) => $$"""
        (() => {
            if (window !== window.top || window.__jdbdLayoutInstalled) return;
            window.__jdbdLayoutInstalled = true;
            const apply = () => {
                if (!{{BuildPlayerDocumentPredicate(videoId)}}) return false;
                if (!document.getElementById('jdbd-player-layout')) {
                    const style = document.createElement('style');
                    style.id = 'jdbd-player-layout';
                    style.textContent = {{JsonSerializer.Serialize(PlayerLayoutCss)}};
                    (document.head || document.documentElement).appendChild(style);
                }
                return true;
            };
            if (!apply()) {
                const timer = setInterval(() => { if (apply()) clearInterval(timer); }, 250);
            }
        })();
        """;

    private static string BuildKioskModeScript(string videoId, Key panelToggleKey, IReadOnlyCollection<Key> reservedAppKeys)
    {
        var (panelKeys, panelCodes) = BuildKeyboardEventVariants(panelToggleKey);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in reservedAppKeys)
        {
            var (variants, codeVariants) = BuildKeyboardEventVariants(key);
            keys.UnionWith(variants);
            codes.UnionWith(codeVariants);
        }
        return $$"""
            (() => {
                if (!{{BuildPlayerDocumentPredicate(videoId)}}) return false;
                {{BuildEarlyKioskBootstrapScript(videoId)}}
                if (window.__jdbdPlayerKeysInstalled) return true;
                window.__jdbdPlayerKeysInstalled = true;
                const panelKeys = new Set({{JsonSerializer.Serialize(panelKeys)}});
                const panelCodes = new Set({{JsonSerializer.Serialize(panelCodes)}});
                const reservedKeys = new Set({{JsonSerializer.Serialize(keys)}});
                const reservedCodes = new Set({{JsonSerializer.Serialize(codes)}});
                const handleKey = event => {
                    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey ||
                        event.target?.closest?.('input, textarea, select, [contenteditable], [role="textbox"]')) return;
                    const key = (event.key || '').toUpperCase();
                    const panel = panelKeys.has(key) || panelCodes.has(event.code);
                    const timecode = key === 'M' || event.code === 'KeyM';
                    if (!panel && !timecode && !reservedKeys.has(key) && !reservedCodes.has(event.code)) return;
                    event.preventDefault();
                    event.stopImmediatePropagation();
                    if (event.type === 'keydown' && !event.repeat && (panel || timecode)) {
                        window.chrome?.webview?.postMessage(panel ? 'jdbd:toggle-effects-panel' : 'jdbd:add-timecode');
                    }
                };
                document.addEventListener('keydown', handleKey, true);
                document.addEventListener('keyup', handleKey, true);
                return true;
            })();
            """;
    }
}
