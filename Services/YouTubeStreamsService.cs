using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JokerDBDTracker.Models;

namespace JokerDBDTracker.Services
{
    public class YouTubeStreamsService
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        static YouTubeStreamsService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }

        public async Task<List<YouTubeVideo>> GetAllStreamsAsync(string streamsUrl, CancellationToken cancellationToken = default)
        {
            var html = await HttpClient.GetStringAsync(streamsUrl, cancellationToken);

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

            using (var initialData = JsonDocument.Parse(initialDataJson))
            {
                ExtractVideosFromJson(initialData.RootElement, videosById, ref orderCounter);
                continuationToken = ExtractContinuationToken(initialData.RootElement);
            }

            var apiKey = ExtractConfigValueFromHtml(html, "INNERTUBE_API_KEY");
            var clientVersion = ExtractConfigValueFromHtml(html, "INNERTUBE_CLIENT_VERSION");
            while (!string.IsNullOrWhiteSpace(continuationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(clientVersion))
                {
                    // YouTube layout can change and hide continuation config. Keep already parsed videos.
                    break;
                }

                using var continuationResponse = await LoadContinuationAsync(
                    apiKey,
                    clientVersion,
                    continuationToken,
                    cancellationToken);

                ExtractVideosFromJson(continuationResponse.RootElement, videosById, ref orderCounter);
                continuationToken = ExtractContinuationToken(continuationResponse.RootElement);
            }

            return videosById.Values.OrderBy(v => v.OriginalOrder).ToList();
        }

        private static async Task<JsonDocument> LoadContinuationAsync(
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

            var response = await HttpClient.PostAsJsonAsync(
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
                if (!objectElement.TryGetProperty("videoRenderer", out var videoRenderer))
                {
                    continue;
                }

                var video = ParseVideoRenderer(videoRenderer);
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

            var videoId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(videoId))
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

        private static string ReadText(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var element))
            {
                return string.Empty;
            }

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

        private static string? ExtractContinuationToken(JsonElement root)
        {
            foreach (var objectElement in EnumerateObjects(root))
            {
                if (!objectElement.TryGetProperty("continuationEndpoint", out var endpoint))
                {
                    continue;
                }

                if (!endpoint.TryGetProperty("continuationCommand", out var command))
                {
                    continue;
                }

                if (!command.TryGetProperty("token", out var tokenElement))
                {
                    continue;
                }

                var token = tokenElement.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }

            return null;
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

