using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;

namespace JokerDBDTracker.Services
{
    public sealed class GitHubUpdateInfo
    {
        public bool IsCheckSuccessful { get; init; }
        public bool IsUpdateAvailable { get; init; }
        public string LatestVersionText { get; init; } = string.Empty;
        public string ReleaseUrl { get; init; } = string.Empty;
        public string DownloadAssetName { get; init; } = string.Empty;
        public string DownloadAssetUrl { get; init; } = string.Empty;
        public long DownloadAssetSizeBytes { get; init; }
    }

    public sealed class GitHubUpdateService
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const int MaxCheckAttempts = 3;

        static GitHubUpdateService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "JokerDBDTracker/1.1.0 (+https://github.com/dragloner/JokerDBDTracker)");
            HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            HttpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<GitHubUpdateInfo> CheckForUpdateAsync(
            string owner,
            string repository,
            Version currentVersion,
            CancellationToken cancellationToken = default)
        {
            for (var attempt = 0; attempt < MaxCheckAttempts; attempt++)
            {
                try
                {
                    var endpoint = $"https://api.github.com/repos/{owner}/{repository}/releases/latest";
                    using var response = await HttpClient.GetAsync(endpoint, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    var root = document.RootElement;

                    var tag = TryGetString(root, "tag_name");
                    var htmlUrl = TryGetString(root, "html_url");
                    var latestVersion = ParseVersion(tag);
                    if (latestVersion is null)
                    {
                        continue;
                    }

                    var (assetName, assetUrl, assetSizeBytes) = SelectBestAsset(root);
                    var isUpdateAvailable = latestVersion > currentVersion;
                    return new GitHubUpdateInfo
                    {
                        IsCheckSuccessful = true,
                        IsUpdateAvailable = isUpdateAvailable,
                        LatestVersionText = $"{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build}",
                        ReleaseUrl = string.IsNullOrWhiteSpace(htmlUrl)
                            ? $"https://github.com/{owner}/{repository}/releases/latest"
                            : htmlUrl,
                        DownloadAssetName = assetName,
                        DownloadAssetUrl = assetUrl,
                        DownloadAssetSizeBytes = assetSizeBytes
                    };
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Http timeout; retry.
                }
                catch (HttpRequestException)
                {
                    // Transient network/API failure; retry.
                }
                catch
                {
                    // Keep retries limited and fail closed.
                }

                if (attempt < MaxCheckAttempts - 1)
                {
                    await Task.Delay(350, cancellationToken);
                }
            }

            var fallback = await TryCheckViaLatestReleaseRedirectAsync(owner, repository, currentVersion, cancellationToken);
            if (fallback.IsCheckSuccessful)
            {
                return fallback;
            }

            return new GitHubUpdateInfo
            {
                IsCheckSuccessful = false
            };
        }

        public async Task DownloadAssetAsync(
            string downloadUrl,
            string destinationPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 64,
                useAsync: true);

            var buffer = new byte[1024 * 64];
            long downloadedBytes = 0;
            int read;
            while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    progress?.Report((double)downloadedBytes / totalBytes.Value);
                }
            }

            progress?.Report(1.0);
        }

        private static string TryGetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                return string.Empty;
            }

            return property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static Version? ParseVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var cleaned = Regex.Replace(value.Trim(), @"^[^\d]*", string.Empty);
            var numericPart = cleaned.Split('-', '+')[0];
            if (string.IsNullOrWhiteSpace(numericPart))
            {
                return null;
            }

            var segments = numericPart.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Take(4)
                .ToList();
            while (segments.Count < 3)
            {
                segments.Add("0");
            }

            var normalized = string.Join(".", segments);
            return Version.TryParse(normalized, out var parsed) ? parsed : null;
        }

        private static (string assetName, string assetUrl, long sizeBytes) SelectBestAsset(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
            {
                return (string.Empty, string.Empty, 0);
            }

            var candidates = new List<(string name, string url, long size, int priority)>();
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = TryGetString(asset, "name");
                var url = TryGetString(asset, "browser_download_url");
                var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var extension = Path.GetExtension(name).ToLowerInvariant();
                var priority = extension switch
                {
                    ".msi" => 300,
                    ".exe" => 200,
                    ".zip" => 100,
                    _ => 0
                };

                if (priority == 0)
                {
                    continue;
                }

                candidates.Add((name, url, size, priority));
            }

            var selected = candidates
                .OrderByDescending(c => c.priority)
                .ThenByDescending(c => c.size)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(selected.name)
                ? (string.Empty, string.Empty, 0)
                : (selected.name, selected.url, selected.size);
        }

        private static async Task<GitHubUpdateInfo> TryCheckViaLatestReleaseRedirectAsync(
            string owner,
            string repository,
            Version currentVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                var latestUrl = $"https://github.com/{owner}/{repository}/releases/latest";
                using var response = await HttpClient.GetAsync(latestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.Found and not HttpStatusCode.MovedPermanently)
                {
                    return new GitHubUpdateInfo { IsCheckSuccessful = false };
                }

                var resolvedUri = response.RequestMessage?.RequestUri?.ToString() ?? latestUrl;
                var tag = resolvedUri.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
                var version = ParseVersion(tag);
                if (version is null)
                {
                    return new GitHubUpdateInfo { IsCheckSuccessful = false };
                }

                return new GitHubUpdateInfo
                {
                    IsCheckSuccessful = true,
                    IsUpdateAvailable = version > currentVersion,
                    LatestVersionText = $"{version.Major}.{version.Minor}.{version.Build}",
                    ReleaseUrl = resolvedUri
                };
            }
            catch
            {
                return new GitHubUpdateInfo { IsCheckSuccessful = false };
            }
        }
    }
}
