using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JokerDBDTracker.Models;

namespace JokerDBDTracker.Services
{
    public class YouTubeStreamsService
    {
        private const int MaxContinuationRequestCount = 200;
        private const int YouTubeVideoIdLength = 11;

        private static readonly Lazy<HttpClient> SharedHttpClient = new(() => CreateHttpClient(forceFreshSockets: false));

        private static HttpClient CreateHttpClient(bool forceFreshSockets)
        {
            var cookieContainer = new CookieContainer();
            cookieContainer.Add(new Uri("https://www.youtube.com"), new Cookie("CONSENT", "YES+cb"));
            cookieContainer.Add(new Uri("https://consent.youtube.com"), new Cookie("CONSENT", "YES+cb"));
            cookieContainer.Add(new Uri("https://www.google.com"), new Cookie("CONSENT", "YES+cb"));

            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                UseCookies = true,
                CookieContainer = cookieContainer,
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(forceFreshSockets ? 8 : 6),
                PooledConnectionLifetime = forceFreshSockets ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(45),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20)
            };

            var httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(forceFreshSockets ? 12 : 8)
            };

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ru;q=0.8");
            httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            return httpClient;
        }

        private static async Task<string> DownloadHtmlAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            var html = await DownloadHtmlOnceAsync(httpClient, url, cancellationToken);
            if (!IsYouTubeConsentPage(html))
            {
                return html;
            }

            var consentHtml = await SubmitYouTubeConsentAsync(httpClient, html, cancellationToken);
            if (LooksLikeYouTubeDataPage(consentHtml))
            {
                return consentHtml;
            }

            return await DownloadHtmlOnceAsync(httpClient, AddQueryParameter(url, "cbrd", "1"), cancellationToken);
        }

        private static async Task<string> DownloadHtmlOnceAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://www.youtube.com/");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        private static async Task<string> SubmitYouTubeConsentAsync(
            HttpClient httpClient,
            string consentHtml,
            CancellationToken cancellationToken)
        {
            var form = ExtractConsentForm(consentHtml);
            if (form.Count == 0)
            {
                throw new InvalidOperationException("YouTube consent page did not contain a usable consent form.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://consent.youtube.com/save")
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Referrer = new Uri("https://consent.youtube.com/");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<List<YouTubeVideo>> GetAllStreamsAsync(string streamsUrl, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetAllStreamsCoreAsync(SharedHttpClient.Value, streamsUrl, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                DiagnosticsService.LogInfo(
                    "YouTubeStreamsService",
                    $"Shared client failed, retrying with fresh sockets. Reason: {ex.GetType().Name}");

                using var freshHttpClient = CreateHttpClient(forceFreshSockets: true);
                return await GetAllStreamsCoreAsync(freshHttpClient, streamsUrl, cancellationToken);
            }
        }

        private static async Task<List<YouTubeVideo>> GetAllStreamsCoreAsync(HttpClient httpClient, string streamsUrl, CancellationToken cancellationToken)
        {
            var candidateUrls = BuildCandidateUrls(streamsUrl);
            var html = await DownloadFirstParsableHtmlAsync(httpClient, candidateUrls, cancellationToken);
            DiagnosticsService.LogInfo("YouTubeStreamsService", $"Streams page loaded from {candidateUrls[0]}");

            var initialDataJson = ExtractJsonByMarkers(
                html,
                "var ytInitialData = ",
                "window[\"ytInitialData\"] = ",
                "window['ytInitialData'] = ");
            if (string.IsNullOrWhiteSpace(initialDataJson))
            {
                initialDataJson = ExtractJsonByRegexMarker(html, @"ytInitialData\s*=\s*");
            }
            if (string.IsNullOrWhiteSpace(initialDataJson))
            {
                throw new InvalidOperationException("Failed to parse streams page data.");
            }

            var videosById = new Dictionary<string, YouTubeVideo>(StringComparer.OrdinalIgnoreCase);
            var orderCounter = 0;
            string? continuationToken;
            var seenContinuationTokens = new HashSet<string>(StringComparer.Ordinal);

            using (var initialData = JsonDocument.Parse(initialDataJson))
            {
                ExtractVideosFromJson(initialData.RootElement, videosById, ref orderCounter);
                continuationToken = ExtractContinuationToken(initialData.RootElement, seenContinuationTokens);
            }

            var apiKey = ExtractConfigValueFromHtml(html, "INNERTUBE_API_KEY");
            var clientVersion = ExtractConfigValueFromHtml(html, "INNERTUBE_CLIENT_VERSION");
            var continuationRequestCount = 0;
            while (!string.IsNullOrWhiteSpace(continuationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!seenContinuationTokens.Add(continuationToken))
                {
                    DiagnosticsService.LogInfo(
                        "YouTubeStreamsService",
                        "Stopping stream continuation loading because YouTube returned an already processed token.");
                    break;
                }

                continuationRequestCount++;
                if (continuationRequestCount > MaxContinuationRequestCount)
                {
                    DiagnosticsService.LogInfo(
                        "YouTubeStreamsService",
                        $"Stopping stream continuation loading after {MaxContinuationRequestCount} pages.");
                    break;
                }

                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(clientVersion))
                {
                    // YouTube layout can change and hide continuation config. Keep already parsed videos.
                    break;
                }

                using var continuationResponse = await LoadContinuationAsync(
                    httpClient,
                    apiKey,
                    clientVersion,
                    continuationToken,
                    cancellationToken);

                ExtractVideosFromJson(continuationResponse.RootElement, videosById, ref orderCounter);
                continuationToken = ExtractContinuationToken(continuationResponse.RootElement, seenContinuationTokens);
            }

            return videosById.Values.OrderBy(v => v.OriginalOrder).ToList();
        }

        private static async Task<string> DownloadFirstParsableHtmlAsync(
            HttpClient httpClient,
            IReadOnlyList<string> candidateUrls,
            CancellationToken cancellationToken)
        {
            Exception? lastError = null;
            foreach (var candidateUrl in candidateUrls)
            {
                try
                {
                    var html = await DownloadHtmlAsync(httpClient, candidateUrl, cancellationToken);
                    if (LooksLikeYouTubeDataPage(html))
                    {
                        return html;
                    }

                    lastError = new InvalidOperationException($"No ytInitialData found in {candidateUrl}.");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("Failed to load streams page data.");
        }

        private static string[] BuildCandidateUrls(string streamsUrl)
        {
            var values = new List<string>();

            void Add(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    values.Add(value);
                }
            }

            Add(streamsUrl);
            if (Uri.TryCreate(streamsUrl, UriKind.Absolute, out var uri))
            {
                var path = uri.AbsolutePath.TrimEnd('/');
                if (path.EndsWith("/streams", StringComparison.OrdinalIgnoreCase))
                {
                    Add($"{uri.Scheme}://{uri.Host}{path}");
                    Add($"{uri.Scheme}://{uri.Host}{path}?view=2&flow=grid");
                    Add($"{uri.Scheme}://{uri.Host}{path[..^"/streams".Length]}/videos?view=2&live_view=501");
                    Add($"https://m.youtube.com{path}");
                    Add($"https://m.youtube.com{path}?view=2&flow=grid");
                }
            }

            return [.. values];
        }

        private static bool LooksLikeYouTubeDataPage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            return html.Contains("ytInitialData", StringComparison.Ordinal) ||
                   html.Contains("\"INNERTUBE_API_KEY\"", StringComparison.Ordinal) ||
                   html.Contains("\"videoRenderer\"", StringComparison.Ordinal);
        }

        private static bool IsYouTubeConsentPage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            return html.Contains("consent.youtube.com", StringComparison.OrdinalIgnoreCase) &&
                   html.Contains("name=\"continue\"", StringComparison.OrdinalIgnoreCase) &&
                   html.Contains("action=\"https://consent.youtube.com/save\"", StringComparison.OrdinalIgnoreCase);
        }

        private static List<KeyValuePair<string, string>> ExtractConsentForm(string html)
        {
            var formHtml = ExtractConsentFormHtml(html, preferRejectAll: true);
            if (string.IsNullOrWhiteSpace(formHtml))
            {
                formHtml = ExtractConsentFormHtml(html, preferRejectAll: false);
            }

            if (string.IsNullOrWhiteSpace(formHtml))
            {
                return [];
            }

            var values = new List<KeyValuePair<string, string>>();
            foreach (Match inputMatch in Regex.Matches(
                         formHtml,
                         "<input\\b[^>]*>",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var input = inputMatch.Value;
                var name = ExtractHtmlAttribute(input, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                values.Add(new KeyValuePair<string, string>(
                    WebUtility.HtmlDecode(name),
                    WebUtility.HtmlDecode(ExtractHtmlAttribute(input, "value"))));
            }

            return values;
        }

        private static string ExtractConsentFormHtml(string html, bool preferRejectAll)
        {
            foreach (Match formMatch in Regex.Matches(
                         html,
                         "<form\\b[^>]*action=\"https://consent\\.youtube\\.com/save\"[^>]*>.*?(?=<form\\b|</body>|</html>)",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
            {
                var formHtml = formMatch.Value;
                var hasRejectAllInput = formHtml.Contains("name=\"set_eom\" value=\"true\"", StringComparison.OrdinalIgnoreCase);
                var hasAcceptAllInput = formHtml.Contains("name=\"set_ytc\" value=\"true\"", StringComparison.OrdinalIgnoreCase) ||
                                        formHtml.Contains("name=\"set_apyt\" value=\"true\"", StringComparison.OrdinalIgnoreCase);
                if (preferRejectAll && hasRejectAllInput && !hasAcceptAllInput)
                {
                    return formHtml;
                }

                if (!preferRejectAll && (hasRejectAllInput || hasAcceptAllInput))
                {
                    return formHtml;
                }
            }

            return string.Empty;
        }

        private static string ExtractHtmlAttribute(string htmlTag, string attributeName)
        {
            var match = Regex.Match(
                htmlTag,
                $"{Regex.Escape(attributeName)}\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return string.Empty;
            }

            for (var i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                {
                    return match.Groups[i].Value;
                }
            }

            return string.Empty;
        }

        private static string AddQueryParameter(string url, string name, string value)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return url;
            }

            var query = uri.Query;
            var separator = string.IsNullOrWhiteSpace(query) ? "?" : "&";
            return $"{uri.GetLeftPart(UriPartial.Path)}{query}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
        }

        private static async Task<JsonDocument> LoadContinuationAsync(
            HttpClient httpClient,
            string apiKey,
            string clientVersion,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            var payload = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion
                    }
                },
                continuation = continuationToken
            };

            var response = await httpClient.PostAsJsonAsync(
                $"https://www.youtube.com/youtubei/v1/browse?key={apiKey}",
                payload,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }

        private static void ExtractVideosFromJson(
            JsonElement root,
            IDictionary<string, YouTubeVideo> videosById,
            ref int orderCounter)
        {
            foreach (var objectElement in EnumerateObjects(root))
            {
                YouTubeVideo? video = null;
                if (objectElement.TryGetProperty("videoRenderer", out var videoRenderer))
                {
                    video = ParseVideoRenderer(videoRenderer);
                }
                else if (objectElement.TryGetProperty("gridVideoRenderer", out var gridVideoRenderer))
                {
                    video = ParseVideoRenderer(gridVideoRenderer);
                }
                else if (objectElement.TryGetProperty("lockupViewModel", out var lockupViewModel))
                {
                    video = ParseLockupViewModel(lockupViewModel);
                }

                if (video is null || string.IsNullOrWhiteSpace(video.VideoId))
                {
                    continue;
                }

                if (!videosById.ContainsKey(video.VideoId))
                {
                    video.OriginalOrder = orderCounter++;
                    videosById[video.VideoId] = video;
                }
            }
        }

        private static YouTubeVideo? ParseVideoRenderer(JsonElement videoRenderer)
        {
            if (!videoRenderer.TryGetProperty("videoId", out var idElement))
            {
                return null;
            }

            var videoId = idElement.GetString() ?? string.Empty;
            if (!IsValidYouTubeVideoId(videoId))
            {
                return null;
            }

            var title = ReadText(videoRenderer, "title");
            var published = ReadText(videoRenderer, "publishedTimeText");
            if (string.IsNullOrWhiteSpace(published))
            {
                published = ReadText(videoRenderer, "videoInfo");
            }

            var thumbnailUrl = TryReadThumbnailUrl(videoRenderer);

            return new YouTubeVideo
            {
                VideoId = videoId,
                Title = string.IsNullOrWhiteSpace(title) ? videoId : title,
                PublishedAtText = string.IsNullOrWhiteSpace(published) ? "No date" : published,
                ThumbnailUrl = thumbnailUrl
            };
        }

        private static YouTubeVideo? ParseLockupViewModel(JsonElement lockupViewModel)
        {
            if (!lockupViewModel.TryGetProperty("contentId", out var idElement))
            {
                return null;
            }

            var videoId = idElement.GetString() ?? string.Empty;
            if (!IsValidYouTubeVideoId(videoId))
            {
                return null;
            }

            var title = ReadTextAtPath(lockupViewModel, "metadata", "lockupMetadataViewModel", "title");
            var published = ReadLockupMetadataText(lockupViewModel);
            var thumbnailUrl = TryReadLockupThumbnailUrl(lockupViewModel);

            return new YouTubeVideo
            {
                VideoId = videoId,
                Title = string.IsNullOrWhiteSpace(title) ? videoId : title,
                PublishedAtText = string.IsNullOrWhiteSpace(published) ? "No date" : published,
                ThumbnailUrl = thumbnailUrl
            };
        }

        private static bool IsValidYouTubeVideoId(string? videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId) || videoId.Length != YouTubeVideoIdLength)
            {
                return false;
            }

            return videoId.All(ch =>
                char.IsAsciiLetterOrDigit(ch) ||
                ch == '-' ||
                ch == '_');
        }

        private static string TryReadThumbnailUrl(JsonElement videoRenderer)
        {
            if (!videoRenderer.TryGetProperty("thumbnail", out var thumbnailObject))
            {
                return string.Empty;
            }

            if (!thumbnailObject.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            string? candidate = null;
            foreach (var thumb in thumbnails.EnumerateArray())
            {
                if (thumb.TryGetProperty("url", out var urlElement))
                {
                    var url = urlElement.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        candidate = url;
                    }
                }
            }

            return candidate ?? string.Empty;
        }

        private static string TryReadLockupThumbnailUrl(JsonElement lockupViewModel)
        {
            if (!lockupViewModel.TryGetProperty("contentImage", out var contentImage))
            {
                return string.Empty;
            }

            string? candidate = null;
            foreach (var objectElement in EnumerateObjects(contentImage))
            {
                if (!objectElement.TryGetProperty("sources", out var sources) ||
                    sources.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var source in sources.EnumerateArray())
                {
                    if (source.TryGetProperty("url", out var urlElement))
                    {
                        var url = urlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            candidate = url;
                        }
                    }
                }
            }

            return candidate ?? string.Empty;
        }

        private static string ReadText(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var element))
            {
                return string.Empty;
            }

            return ReadTextElement(element);
        }

        private static string ReadTextAtPath(JsonElement root, params string[] path)
        {
            var current = root;
            foreach (var propertyName in path)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(propertyName, out current))
                {
                    return string.Empty;
                }
            }

            return ReadTextElement(current);
        }

        private static string ReadTextElement(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? string.Empty;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("simpleText", out var simpleText))
                {
                    return simpleText.GetString() ?? string.Empty;
                }

                if (element.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? string.Empty;
                }

                if (element.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var run in runs.EnumerateArray())
                    {
                        if (run.TryGetProperty("text", out var textPart))
                        {
                            sb.Append(textPart.GetString());
                        }
                    }

                    return sb.ToString();
                }
            }

            return string.Empty;
        }

        private static string ReadLockupMetadataText(JsonElement lockupViewModel)
        {
            if (!lockupViewModel.TryGetProperty("metadata", out var metadataRoot))
            {
                return string.Empty;
            }

            foreach (var objectElement in EnumerateObjects(metadataRoot))
            {
                var text = ReadTextElement(objectElement);
                if (!string.IsNullOrWhiteSpace(text) &&
                    !string.Equals(text, ReadTextAtPath(lockupViewModel, "metadata", "lockupMetadataViewModel", "title"), StringComparison.Ordinal))
                {
                    return text;
                }
            }

            return string.Empty;
        }

        private static string? ExtractContinuationToken(JsonElement root, ISet<string> seenTokens)
        {
            foreach (var objectElement in EnumerateObjects(root))
            {
                if (!objectElement.TryGetProperty("continuationItemRenderer", out var continuationItemRenderer))
                {
                    continue;
                }

                var token = TryReadContinuationToken(continuationItemRenderer);
                if (!string.IsNullOrWhiteSpace(token) && !seenTokens.Contains(token))
                {
                    return token;
                }
            }

            foreach (var objectElement in EnumerateObjects(root))
            {
                var token = TryReadContinuationToken(objectElement);
                if (!string.IsNullOrWhiteSpace(token) && !seenTokens.Contains(token))
                {
                    return token;
                }
            }

            return null;
        }

        private static string? TryReadContinuationToken(JsonElement parent)
        {
            if (!parent.TryGetProperty("continuationEndpoint", out var endpoint))
            {
                return null;
            }

            if (!endpoint.TryGetProperty("continuationCommand", out var command))
            {
                return null;
            }

            if (!command.TryGetProperty("token", out var tokenElement))
            {
                return null;
            }

            var token = tokenElement.GetString();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        private static IEnumerable<JsonElement> EnumerateObjects(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                yield return root;

                foreach (var property in root.EnumerateObject())
                {
                    foreach (var child in EnumerateObjects(property.Value))
                    {
                        yield return child;
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    foreach (var child in EnumerateObjects(item))
                    {
                        yield return child;
                    }
                }
            }
        }

        private static string ExtractConfigValueFromHtml(string html, string key)
        {
            var strictPattern = $"\"{Regex.Escape(key)}\":\"([^\"]+)\"";
            var strictMatch = Regex.Match(html, strictPattern);
            if (strictMatch.Success)
            {
                return strictMatch.Groups[1].Value;
            }

            var relaxedPattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]+)\"";
            var relaxedMatch = Regex.Match(html, relaxedPattern);
            return relaxedMatch.Success ? relaxedMatch.Groups[1].Value : string.Empty;
        }

        private static string ExtractJsonByMarker(string html, string marker)
        {
            var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return string.Empty;
            }

            var jsonStart = html.IndexOf('{', markerIndex + marker.Length);
            if (jsonStart < 0)
            {
                return string.Empty;
            }

            return ExtractJsonObjectStartingAt(html, jsonStart);
        }

        private static string ExtractJsonByMarkers(string html, params string[] markers)
        {
            foreach (var marker in markers)
            {
                var json = ExtractJsonByMarker(html, marker);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    return json;
                }
            }

            return string.Empty;
        }

        private static string ExtractJsonByRegexMarker(string html, string markerPattern)
        {
            var match = Regex.Match(html, markerPattern);
            if (!match.Success)
            {
                return string.Empty;
            }

            var startIndex = match.Index + match.Length;
            if (startIndex < 0 || startIndex >= html.Length)
            {
                return string.Empty;
            }

            var braceIndex = html.IndexOf('{', startIndex);
            if (braceIndex < 0)
            {
                return string.Empty;
            }

            return ExtractJsonObjectStartingAt(html, braceIndex);
        }

        private static string ExtractJsonObjectStartingAt(string html, int jsonStart)
        {
            if (jsonStart < 0 || jsonStart >= html.Length || html[jsonStart] != '{')
            {
                return string.Empty;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var i = jsonStart; i < html.Length; i++)
            {
                var ch = html[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return html.Substring(jsonStart, i - jsonStart + 1);
                    }
                }
            }

            return string.Empty;
        }
    }
}

