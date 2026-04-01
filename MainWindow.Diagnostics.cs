using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private SynchronizationContext? _logViewerSyncContext;

        private void InitializeLogViewer()
        {
            _logViewerSyncContext = SynchronizationContext.Current;
            DiagnosticsService.EntryLogged += OnDiagnosticsEntryLogged;
            RefreshLogViewer();
        }

        private void UninitializeLogViewer()
        {
            DiagnosticsService.EntryLogged -= OnDiagnosticsEntryLogged;
        }

        private void OnDiagnosticsEntryLogged(LogEntry entry)
        {
            if (_logViewerSyncContext is null)
            {
                return;
            }

            _logViewerSyncContext.Post(_ => AppendLogEntry(entry), null);
        }

        private void AppendLogEntry(LogEntry entry)
        {
            var errorsOnly = LogViewerErrorsOnlyCheckBox.IsChecked == true;
            if (errorsOnly && entry.Level != LogLevel.Error)
            {
                return;
            }

            var item = BuildLogItem(entry);
            LogViewerListBox.Items.Add(item);

            // Keep display list at 200 items
            while (LogViewerListBox.Items.Count > 200)
            {
                LogViewerListBox.Items.RemoveAt(0);
            }

            // Auto-scroll to bottom
            LogViewerListBox.ScrollIntoView(item);
        }

        private void RefreshLogViewer()
        {
            LogViewerListBox.Items.Clear();
            var errorsOnly = LogViewerErrorsOnlyCheckBox.IsChecked == true;
            var entries = DiagnosticsService.GetRecentEntries();

            foreach (var entry in entries)
            {
                if (errorsOnly && entry.Level != LogLevel.Error)
                {
                    continue;
                }

                LogViewerListBox.Items.Add(BuildLogItem(entry));
            }

            if (LogViewerListBox.Items.Count > 0)
            {
                LogViewerListBox.ScrollIntoView(LogViewerListBox.Items[^1]);
            }
        }

        private static ListBoxItem BuildLogItem(LogEntry entry)
        {
            var levelTag = entry.Level == LogLevel.Error ? "ERROR" : "INFO ";
            var text = $"[{entry.Timestamp:HH:mm:ss.fff}] [{levelTag}] [{entry.Source}] {entry.Message}";

            var item = new ListBoxItem
            {
                Content = text,
                Padding = new Thickness(6, 2, 6, 2),
                Foreground = entry.Level == LogLevel.Error
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x7B, 0x72))
                    : new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xF8)),
                Background = Brushes.Transparent,
            };

            return item;
        }

        private void LogViewerFilter_Changed(object sender, RoutedEventArgs e)
        {
            RefreshLogViewer();
        }

        private void LogViewerCopyButton_Click(object sender, RoutedEventArgs e)
        {
            var entries = DiagnosticsService.GetRecentEntries();
            var errorsOnly = LogViewerErrorsOnlyCheckBox.IsChecked == true;

            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (errorsOnly && entry.Level != LogLevel.Error)
                {
                    continue;
                }

                var levelTag = entry.Level == LogLevel.Error ? "ERROR" : "INFO ";
                sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}Z] [{levelTag}] [{entry.Source}] {entry.Message}");
            }

            try
            {
                Clipboard.SetText(sb.Length > 0 ? sb.ToString() : "(no entries)");
            }
            catch
            {
                // Clipboard may be locked by another app.
            }
        }

        private void LogViewerClearButton_Click(object sender, RoutedEventArgs e)
        {
            DiagnosticsService.ClearInMemoryLog();
            LogViewerListBox.Items.Clear();
        }
    }
}
