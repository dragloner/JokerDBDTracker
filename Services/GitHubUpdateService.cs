using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    public sealed class UpdateDownloadProgress
    {
        public long DownloadedBytes { get; init; }
        public long? TotalBytes { get; init; }
        public double BytesPerSecond { get; init; }
        public bool IsCompleted { get; init; }

        public double? Fraction =>
            TotalBytes.HasValue && TotalBytes.Value > 0
                ? Math.Clamp((double)DownloadedBytes / TotalBytes.Value, 0, 1)
                : null;
    }

    public sealed class GitHubUpdateService
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const int MaxCheckAttempts = 3;
        private const int MaxDownloadNetworkAttempts = 2;
        private static readonly TimeSpan CheckRequestTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan DownloadAttemptTimeout = TimeSpan.FromMinutes(20);

        static GitHubUpdateService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BuildUserAgent());
            HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            HttpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        private static string BuildUserAgent()
        {
            var versionText = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion?
                .Split('+')[0];
            if (string.IsNullOrWhiteSpace(versionText))
            {
                versionText = "1.0.0";
            }

            return $"JokerDBDTracker/{versionText} (+https://github.com/dragloner/JokerDBDTracker)";
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
                    using var timeoutCts = CreateLinkedTimeoutToken(cancellationToken, CheckRequestTimeout);
                    var endpoint = $"https://api.github.com/repos/{owner}/{repository}/releases/latest";
                    using var response = await HttpClient.GetAsync(endpoint, timeoutCts.Token);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
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
                    return new GitHubUpdateInfo
                    {
                        IsCheckSuccessful = true,
                        IsUpdateAvailable = latestVersion > currentVersion,
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
                    // Request timeout; retry.
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

        public async Task<string> DownloadAssetAsync(
            string downloadUrl,
            string destinationPath,
            long expectedSizeBytes = 0,
            string expectedSha256 = "",
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidDataException("Update download URL is empty.");
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new InvalidDataException("Update destination path is empty.");
            }

            var targetDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (await TryUseExistingDownloadedAssetAsync(destinationPath, expectedSizeBytes, expectedSha256, cancellationToken))
            {
                var existingLength = new FileInfo(destinationPath).Length;
                progress?.Report(new UpdateDownloadProgress
                {
                    DownloadedBytes = existingLength,
                    TotalBytes = expectedSizeBytes > 0 ? expectedSizeBytes : existingLength,
                    BytesPerSecond = 0,
                    IsCompleted = true
                });
                return destinationPath;
            }

            var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.downloading";
            Exception? lastNetworkException = null;
            try
            {
                for (var attempt = 1; attempt <= MaxDownloadNetworkAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await DownloadToTempFileAsync(
                            downloadUrl,
                            tempPath,
                            expectedSizeBytes,
                            progress,
                            cancellationToken);
                        lastNetworkException = null;
                        break;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxDownloadNetworkAttempts)
                    {
                        lastNetworkException = new TimeoutException("Update download timed out.");
                    }
                    catch (HttpRequestException ex) when (attempt < MaxDownloadNetworkAttempts)
                    {
                        lastNetworkException = ex;
                    }

                    TryDeleteFile(tempPath);
                    DiagnosticsService.LogInfo(
                        nameof(GitHubUpdateService),
                        $"Retrying update download. Attempt {attempt + 1}/{MaxDownloadNetworkAttempts}. Url={downloadUrl}");
                    await Task.Delay(650, cancellationToken);
                }

                if (lastNetworkException is not null)
                {
                    throw lastNetworkException;
                }

                await EnsureDownloadedAssetValidAsync(tempPath, expectedSizeBytes, expectedSha256, cancellationToken);
                var finalPath = await MoveDownloadedFileWithFallbackAsync(tempPath, destinationPath, cancellationToken);
                var finalLength = new FileInfo(finalPath).Length;
                progress?.Report(new UpdateDownloadProgress
                {
                    DownloadedBytes = finalLength,
                    TotalBytes = expectedSizeBytes > 0 ? expectedSizeBytes : finalLength,
                    BytesPerSecond = 0,
                    IsCompleted = true
                });
                return finalPath;
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        private static async Task DownloadToTempFileAsync(
            string downloadUrl,
            string tempPath,
            long expectedSizeBytes,
            IProgress<UpdateDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            TryDeleteFile(tempPath);
            using var timeoutCts = CreateLinkedTimeoutToken(cancellationToken, DownloadAttemptTimeout);
            using var response = await HttpClient.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Update server returned unexpected content type: {mediaType}");
            }

            var totalBytes = response.Content.Headers.ContentLength;
            if ((!totalBytes.HasValue || totalBytes.Value <= 0) && expectedSizeBytes > 0)
            {
                totalBytes = expectedSizeBytes;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            await using var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 64,
                useAsync: true);

            var buffer = new byte[1024 * 64];
            long downloadedBytes = 0;
            var watch = Stopwatch.StartNew();
            var lastSampleSeconds = 0.0;
            var lastSampleBytes = 0L;
            var latestSpeed = 0.0;

            progress?.Report(new UpdateDownloadProgress
            {
                DownloadedBytes = 0,
                TotalBytes = totalBytes,
                BytesPerSecond = 0,
                IsCompleted = false
            });

            int read;
            while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);
                downloadedBytes += read;

                var elapsedSeconds = watch.Elapsed.TotalSeconds;
                var shouldReport =
                    elapsedSeconds - lastSampleSeconds >= 0.25 ||
                    (totalBytes.HasValue && downloadedBytes >= totalBytes.Value);
                if (!shouldReport)
                {
                    continue;
                }

                var deltaBytes = downloadedBytes - lastSampleBytes;
                var deltaSeconds = Math.Max(0.001, elapsedSeconds - lastSampleSeconds);
                latestSpeed = deltaBytes / deltaSeconds;
                lastSampleBytes = downloadedBytes;
                lastSampleSeconds = elapsedSeconds;

                progress?.Report(new UpdateDownloadProgress
                {
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = totalBytes,
                    BytesPerSecond = latestSpeed,
                    IsCompleted = false
                });
            }

            await fileStream.FlushAsync(timeoutCts.Token);
            progress?.Report(new UpdateDownloadProgress
            {
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes ?? (downloadedBytes > 0 ? downloadedBytes : null),
                BytesPerSecond = latestSpeed,
                IsCompleted = true
            });
        }

        private static async Task<bool> TryUseExistingDownloadedAssetAsync(
            string path,
            long expectedSizeBytes,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                await EnsureDownloadedAssetValidAsync(path, expectedSizeBytes, expectedSha256, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task EnsureDownloadedAssetValidAsync(
            string path,
            long expectedSizeBytes,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                throw new InvalidDataException("Downloaded update archive is empty.");
            }

            if (expectedSizeBytes > 0 && fileInfo.Length != expectedSizeBytes)
            {
                DiagnosticsService.LogInfo(
                    nameof(GitHubUpdateService),
                    $"Downloaded file size mismatch ignored. Expected {expectedSizeBytes}, got {fileInfo.Length}.");
            }

            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                return;
            }

            var actualSha256 = await ComputeFileSha256Async(path, cancellationToken);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Downloaded file hash mismatch.");
            }
        }

        private static async Task<string> MoveDownloadedFileWithFallbackAsync(
            string tempPath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(tempPath, destinationPath, overwrite: true);
                    return destinationPath;
                }
                catch (IOException) when (attempt < 6)
                {
                    await Task.Delay(220, cancellationToken);
                }
                catch (UnauthorizedAccessException) when (attempt < 6)
                {
                    await Task.Delay(220, cancellationToken);
                }
            }

            // Destination might be locked by antivirus/indexer/previous updater run.
            // Keep downloaded archive under unique name so update can still continue.
            var fallbackPath = BuildAlternativeDestinationPath(destinationPath);
            File.Move(tempPath, fallbackPath, overwrite: false);
            return fallbackPath;
        }

        private static string BuildAlternativeDestinationPath(string destinationPath)
        {
            var directory = Path.GetDirectoryName(destinationPath) ?? Path.GetTempPath();
            var name = Path.GetFileNameWithoutExtension(destinationPath);
            var extension = Path.GetExtension(destinationPath);
            return Path.Combine(
                directory,
                $"{name}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static CancellationTokenSource CreateLinkedTimeoutToken(
            CancellationToken cancellationToken,
            TimeSpan timeout)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            return linked;
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
                using var timeoutCts = CreateLinkedTimeoutToken(cancellationToken, TimeSpan.FromSeconds(12));
                var checksumText = await HttpClient.GetStringAsync(checksumUrl, timeoutCts.Token);
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
                using var timeoutCts = CreateLinkedTimeoutToken(cancellationToken, CheckRequestTimeout);
                var latestUrl = $"https://github.com/{owner}/{repository}/releases/latest";
                using var response = await HttpClient.GetAsync(latestUrl, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
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
