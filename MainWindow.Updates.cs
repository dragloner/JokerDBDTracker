using System.Diagnostics;
using System.IO;
using System.Windows;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async Task CheckForUpdatesDuringStartupAsync()
        {
            GitHubUpdateInfo updateInfo;
            try
            {
                updateInfo = await _updateService.CheckForUpdateAsync(
                    GitHubRepoOwner,
                    GitHubRepoName,
                    GetCurrentAppVersion());
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("CheckForUpdatesDuringStartupAsync", ex);
                updateInfo = new GitHubUpdateInfo
                {
                    IsCheckSuccessful = false
                };
            }
            _lastUpdateInfo = updateInfo;

            if (UpdateStatusText is null || UpdateProgramButton is null)
            {
                return;
            }

            if (!updateInfo.IsCheckSuccessful)
            {
                UpdateStatusText.Text = T("● Не удалось проверить обновления", "● Failed to check updates");
                UpdateStatusText.Foreground = BrushFromHex("#E4C487");
                _updateButtonOpensReleasePage = false;
                UpdateProgramButton.Visibility = Visibility.Collapsed;
                RestartProgramButton.Visibility = Visibility.Collapsed;
                return;
            }

            _latestReleaseUrl = string.IsNullOrWhiteSpace(updateInfo.ReleaseUrl)
                ? _latestReleaseUrl
                : updateInfo.ReleaseUrl;

            if (updateInfo.IsUpdateAvailable)
            {
                UpdateStatusText.Text = T(
                    $"● Доступно обновление {updateInfo.LatestVersionText}",
                    $"● Update available {updateInfo.LatestVersionText}");
                UpdateStatusText.Foreground = BrushFromHex("#F0B56E");
                var hasDirectInstaller = !string.IsNullOrWhiteSpace(updateInfo.DownloadAssetUrl) &&
                                         !string.IsNullOrWhiteSpace(updateInfo.DownloadAssetName);
                _updateButtonOpensReleasePage = !hasDirectInstaller;
                UpdateProgramButton.Content = hasDirectInstaller
                    ? T("Обновить программу", "Update app")
                    : T("Открыть релиз", "Open release");
                UpdateProgramButton.Visibility = Visibility.Visible;
                RestartProgramButton.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateStatusText.Text = T("● Установлено последнее обновление", "● You are up to date");
            UpdateStatusText.Foreground = BrushFromHex("#82D7AA");
            _updateButtonOpensReleasePage = false;
            UpdateProgramButton.Visibility = Visibility.Collapsed;
            RestartProgramButton.Visibility = Visibility.Collapsed;
        }

        private async void UpdateProgramButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingUpdate)
            {
                return;
            }

            if (_updateButtonOpensReleasePage)
            {
                OpenReleasePageInBrowser();
                return;
            }

            if (_lastUpdateInfo is null || !_lastUpdateInfo.IsUpdateAvailable)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_lastUpdateInfo.DownloadAssetUrl) ||
                string.IsNullOrWhiteSpace(_lastUpdateInfo.DownloadAssetName))
            {
                OpenReleasePageInBrowser();
                return;
            }

            _isDownloadingUpdate = true;
            try
            {
                var targetVersionText = string.IsNullOrWhiteSpace(_lastUpdateInfo.LatestVersionText)
                    ? "?"
                    : _lastUpdateInfo.LatestVersionText;
                UpdateProgramButton.IsEnabled = false;
                ShowLoadingOverlay(
                    T(
                        $"Скачивание обновления {targetVersionText}...",
                        $"Downloading update {targetVersionText}..."),
                    isIndeterminate: false);

                var updatesFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppStoragePaths.CurrentFolderName,
                    "Updates");
                Directory.CreateDirectory(updatesFolder);
                var destinationPath = BuildUpdateAssetDestinationPath(updatesFolder, _lastUpdateInfo);

                var progress = new Progress<UpdateDownloadProgress>(state =>
                {
                    if (LoadingProgressBar is null || LoadingProgressText is null || LoadingStatusText is null)
                    {
                        return;
                    }

                    var fraction = state.Fraction;
                    if (fraction.HasValue)
                    {
                        var clamped = Math.Clamp(fraction.Value, 0, 1);
                        LoadingProgressBar.IsIndeterminate = false;
                        LoadingProgressBar.Value = clamped * 100;
                        LoadingStatusText.Text = T(
                            $"Скачивание обновления {targetVersionText}: {clamped * 100:0.0}%",
                            $"Downloading update {targetVersionText}: {clamped * 100:0.0}%");
                    }
                    else
                    {
                        LoadingProgressBar.IsIndeterminate = true;
                        LoadingStatusText.Text = T(
                            $"Скачивание обновления {targetVersionText}...",
                            $"Downloading update {targetVersionText}...");
                    }

                    var speedText = FormatDownloadSpeed(state.BytesPerSecond);
                    if (state.TotalBytes.HasValue && state.TotalBytes.Value > 0)
                    {
                        LoadingProgressText.Text = T(
                            $"{FormatDataSize(state.DownloadedBytes)} из {FormatDataSize(state.TotalBytes.Value)} • {speedText}",
                            $"{FormatDataSize(state.DownloadedBytes)} of {FormatDataSize(state.TotalBytes.Value)} • {speedText}");
                    }
                    else
                    {
                        LoadingProgressText.Text = T(
                            $"{FormatDataSize(state.DownloadedBytes)} • {speedText}",
                            $"{FormatDataSize(state.DownloadedBytes)} • {speedText}");
                    }
                });

                _updateDownloadCts = new CancellationTokenSource();
                var downloadedArchivePath = await _updateService.DownloadAssetAsync(
                    _lastUpdateInfo.DownloadAssetUrl,
                    destinationPath,
                    _lastUpdateInfo.DownloadAssetSizeBytes,
                    _lastUpdateInfo.DownloadAssetSha256,
                    progress,
                    _updateDownloadCts.Token);
                if (!string.Equals(Path.GetExtension(downloadedArchivePath), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        T(
                            $"Ожидается portable zip-архив для обновления, но получен файл: {Path.GetFileName(downloadedArchivePath)}",
                            $"Expected portable ZIP update archive, but got: {Path.GetFileName(downloadedArchivePath)}"));
                }

                ShowLoadingOverlay(
                    T(
                        $"Подготовка обновления {targetVersionText}...",
                        $"Preparing update {targetVersionText}..."),
                    isIndeterminate: true);
                StartPortableSelfUpdate(downloadedArchivePath);

                HideLoadingOverlay();
                Application.Current.Shutdown();
                return;
            }
            catch (OperationCanceledException)
            {
                HideLoadingOverlay();
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                MessageBox.Show(
                    $"{T("Не удалось скачать или запустить обновление:", "Failed to download or apply update:")}{Environment.NewLine}{ex.Message}",
                    T("Обновление", "Update"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _updateDownloadCts?.Dispose();
                _updateDownloadCts = null;
                _isDownloadingUpdate = false;
                UpdateProgramButton.IsEnabled = true;
            }
        }

        private static string BuildUpdateAssetDestinationPath(string updatesFolder, GitHubUpdateInfo updateInfo)
        {
            var version = string.IsNullOrWhiteSpace(updateInfo.LatestVersionText)
                ? "unknown"
                : updateInfo.LatestVersionText.Replace('.', '_');
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var assetName = Path.GetFileName(updateInfo.DownloadAssetName);
            if (string.IsNullOrWhiteSpace(assetName))
            {
                assetName = "update.zip";
            }

            return Path.Combine(updatesFolder, $"{version}_{timestamp}_{assetName}");
        }

        private string FormatDownloadSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return T("скорость...", "speed...");
            }

            return T($"{FormatDataSize((long)bytesPerSecond)}/с", $"{FormatDataSize((long)bytesPerSecond)}/s");
        }

        private static string FormatDataSize(long bytes)
        {
            var value = Math.Max(0, bytes);
            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            var size = (double)value;
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:0} {units[unitIndex]}"
                : $"{size:0.00} {units[unitIndex]}";
        }

        private void RestartProgramButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentExePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(currentExePath))
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = currentExePath,
                    UseShellExecute = true
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{T("Не удалось перезапустить программу:", "Failed to restart app:")}{Environment.NewLine}{ex.Message}",
                    T("Перезапуск", "Restart"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenReleasePageInBrowser()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _latestReleaseUrl,
                UseShellExecute = true
            });
        }
    }
}
