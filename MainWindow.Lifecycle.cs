namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _searchDebounceTimer.Stop();
                _networkTimeSyncTimer.Stop();
                _questRolloverTimer.Stop();
                _questUiRefreshTimer.Stop();
                _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
                _networkTimeSyncTimer.Tick -= NetworkTimeSyncTimer_Tick;
                _questRolloverTimer.Tick -= QuestRolloverTimer_Tick;
                _questUiRefreshTimer.Tick -= QuestUiRefreshTimer_Tick;
                StateChanged -= MainWindow_StateChanged;
                Loaded -= MainWindow_Loaded;
                PreviewKeyDown -= MainWindow_PreviewKeyDown;
                PreviewMouseMove -= MainWindow_PreviewMouseMove;
                PreviewMouseLeftButtonUp -= MainWindow_PreviewMouseLeftButtonUp;
                _updateDownloadCts?.Cancel();
                _updateDownloadCts?.Dispose();
                _updateDownloadCts = null;
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}