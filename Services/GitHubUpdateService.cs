using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using System.Security.Cryptography;

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
        public string DownloadAssetSha256 { get; init; } = string.Empty;
    }

    public sealed class GitHubUpdateService
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const int MaxCheckAttempts = 3;

        static GitHubUpdateService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "JokerDBDTracker/1.2.0.1 (+https://github.com/dragloner/JokerDBDTracker)");
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

                    var (assetName, assetUrl, assetSizeBytes, checksumUrl) = SelectBestAsset(root);
                    var assetSha256 = await TryResolveSha256Async(checksumUrl, assetName, cancellationToken);
                    var isUpdateAvailable = latestVersion > currentVersion;
                    return new GitHubUpdateInfo
                    {
                        IsCheckSuccessful = true,
                        IsUpdateAvailable = isUpdateAvailable,
                        LatestVersionText = FormatVersion(latestVersion),
                        ReleaseUrl = string.IsNullOrWhiteSpace(htmlUrl)
                            ? $"https://github.com/{owner}/{repository}/releases/latest"
                            : htmlUrl,
                        DownloadAssetName = assetName,
                        DownloadAssetUrl = assetUrl,
                        DownloadAssetSizeBytes = assetSizeBytes,
                        DownloadAssetSha256 = assetSha256
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
            long expectedSizeBytes = 0,
            string expectedSha256 = "",
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

            if (expectedSizeBytes > 0)
            {
                var fileInfo = new FileInfo(destinationPath);
                if (!fileInfo.Exists || fileInfo.Length != expectedSizeBytes)
                {
                    throw new InvalidDataException($"Downloaded file size mismatch. Expected {expectedSizeBytes}, got {fileInfo.Length}.");
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actualSha256 = await ComputeFileSha256Async(destinationPath, cancellationToken);
                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Downloaded file hash mismatch.");
                }
            }
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

        private static (string assetName, string assetUrl, long sizeBytes, string checksumUrl) SelectBestAsset(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
            {
                return (string.Empty, string.Empty, 0, string.Empty);
            }

            var candidates = new List<(string name, string url, long size, int priority)>();
            var checksumAssets = new List<(string name, string url)>();
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
                if (extension is ".sha256" or ".sha" or ".txt")
                {
                    checksumAssets.Add((name, url));
                }
                var priority = GetPortableAssetPriority(name, extension);

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

            if (string.IsNullOrWhiteSpace(selected.name))
            {
                return (string.Empty, string.Empty, 0, string.Empty);
            }

            var checksumUrl = FindChecksumUrlForAssetName(checksumAssets, selected.name);
            return (selected.name, selected.url, selected.size, checksumUrl);
        }

        private static int GetPortableAssetPriority(string assetName, string extension)
        {
            if (extension != ".zip")
            {
                return 0;
            }

            var lower = assetName.ToLowerInvariant();
            var priority = 100;

            if (lower.Contains("win-x64"))
            {
                priority += 140;
            }
            if (lower.Contains("portable"))
            {
                priority += 90;
            }
            if (lower.Contains("self-contained"))
            {
                priority += 50;
            }
            if (lower.Contains("symbols") || lower.Contains("debug"))
            {
                priority -= 120;
            }

            return Math.Max(1, priority);
        }

        private static string FindChecksumUrlForAssetName(List<(string name, string url)> checksumAssets, string selectedAssetName)
        {
            var selectedLower = selectedAssetName.ToLowerInvariant();
            foreach (var checksumAsset in checksumAssets)
            {
                var checksumNameLower = checksumAsset.name.ToLowerInvariant();
                if (checksumNameLower.Contains(selectedLower) && checksumNameLower.Contains("sha"))
                {
                    return checksumAsset.url;
                }
            }

            return string.Empty;
        }

        private static async Task<string> TryResolveSha256Async(string checksumUrl, string assetName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(checksumUrl) || string.IsNullOrWhiteSpace(assetName))
            {
                return string.Empty;
            }

            try
            {
                var checksumText = await HttpClient.GetStringAsync(checksumUrl, cancellationToken);
                var lines = checksumText
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lines.Length == 0)
                {
                    return string.Empty;
                }

                var assetLower = assetName.ToLowerInvariant();
                foreach (var line in lines)
                {
                    if (!line.Contains(assetLower, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var sha = ExtractHexSha256(line);
                    if (!string.IsNullOrWhiteSpace(sha))
                    {
                        return sha;
                    }
                }

                return ExtractHexSha256(lines[0]);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractHexSha256(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = Regex.Match(text, @"\b[A-Fa-f0-9]{64}\b");
            return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
        }

        private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
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
                    LatestVersionText = FormatVersion(version),
                    ReleaseUrl = resolvedUri
                };
            }
            catch
            {
                return new GitHubUpdateInfo { IsCheckSuccessful = false };
            }
        }

        private static string FormatVersion(Version version)
        {
            if (version.Revision > 0)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }

            if (version.Build > 0)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }

            return $"{version.Major}.{version.Minor}.0";
        }
    }
}
