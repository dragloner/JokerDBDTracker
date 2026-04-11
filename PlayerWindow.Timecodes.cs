using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JokerDBDTracker.Models;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private bool _timecodePopupVisible;
        private int _pendingTimecodeSeconds;

        private async Task LoadVideoTimecodesAsync()
        {
            try
            {
                var all = await _playerTimecodeService.LoadAsync();
                ApplyLoadedTimecodes(all);
            }
            catch
            {
                _videoTimecodes = [];
                _allTimecodes = [];
                RefreshTimecodesPanelList();
            }
        }

        private void ApplyLoadedTimecodes(List<Timecode> all)
        {
            foreach (var timecode in all)
            {
                timecode.IsCurrentVideo = string.Equals(
                    timecode.VideoId,
                    _video.VideoId,
                    StringComparison.OrdinalIgnoreCase);
            }

            _videoTimecodes = all
                .Where(t => t.IsCurrentVideo)
                .OrderBy(t => t.Seconds)
                .ToList();

            _allTimecodes =
            [
                .. _videoTimecodes,
                .. all
                    .Where(t => !t.IsCurrentVideo)
                    .OrderByDescending(t => t.SavedAtUtc)
            ];

            RefreshTimecodesPanelList();
        }

        private void RefreshTimecodesPanelList()
        {
            if (TimecodesPanelList is null)
            {
                return;
            }

            var filtered = BuildFilteredTimecodes();
            TimecodesPanelList.ItemsSource = null;
            TimecodesPanelList.ItemsSource = filtered;

            if (TimecodesPanelEmptyText is not null)
            {
                TimecodesPanelEmptyText.Visibility = filtered.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                TimecodesPanelEmptyText.Text = string.IsNullOrWhiteSpace(_timecodeSearchQuery)
                    ? PT("Нет сохранённых таймкодов. Нажми M или кнопку + Таймкод.", "No saved timecodes yet. Press M or + Timecode.")
                    : PT("По этому запросу таймкоды не найдены.", "No timecodes found for this search.");
            }

            if (TimecodesPanelHeaderText is not null)
            {
                var total = _allTimecodes.Count;
                var current = _videoTimecodes.Count;
                TimecodesPanelHeaderText.Text = total == 0
                    ? PT("Таймкоды", "Timecodes")
                    : PT($"Таймкоды ({current} здесь / {total} всего)", $"Timecodes ({current} here / {total} total)");
            }

            if (TimecodesPanelSummaryText is not null)
            {
                var filteredCurrent = filtered.Count(t => t.IsCurrentVideo);
                var filteredOther = filtered.Count - filteredCurrent;
                TimecodesPanelSummaryText.Text = string.IsNullOrWhiteSpace(_timecodeSearchQuery)
                    ? PT(
                        $"Сначала показываются таймкоды этого видео, затем остальные. Всего: {filtered.Count}.",
                        $"Current video timecodes are shown first, then the rest. Total: {filtered.Count}.")
                    : PT(
                        $"Найдено: {filtered.Count}. Здесь: {filteredCurrent}. В других видео: {filteredOther}.",
                        $"Found: {filtered.Count}. Here: {filteredCurrent}. In other videos: {filteredOther}.");
            }
        }

        private List<Timecode> BuildFilteredTimecodes()
        {
            if (string.IsNullOrWhiteSpace(_timecodeSearchQuery))
            {
                return [.. _allTimecodes];
            }

            var query = _timecodeSearchQuery.Trim();
            return _allTimecodes
                .Where(t => MatchesTimecodeSearch(t, query))
                .ToList();
        }

        private static bool MatchesTimecodeSearch(Timecode timecode, string query)
        {
            var comparison = StringComparison.OrdinalIgnoreCase;
            return (!string.IsNullOrWhiteSpace(timecode.Label) && timecode.Label.Contains(query, comparison)) ||
                   (!string.IsNullOrWhiteSpace(timecode.VideoTitle) && timecode.VideoTitle.Contains(query, comparison)) ||
                   timecode.TimeFormatted.Contains(query, comparison) ||
                   timecode.SavedAtText.Contains(query, comparison);
        }

        private void TimecodesSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _timecodeSearchQuery = TimecodesSearchBox?.Text?.Trim() ?? string.Empty;

            if (TimecodesSearchPlaceholderText is not null)
            {
                TimecodesSearchPlaceholderText.Visibility =
                    string.IsNullOrWhiteSpace(_timecodeSearchQuery) &&
                    (TimecodesSearchBox?.IsKeyboardFocused != true)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            RefreshTimecodesPanelList();
        }

        private void TimecodesSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TimecodesSearchPlaceholderText is not null)
            {
                TimecodesSearchPlaceholderText.Visibility = Visibility.Collapsed;
            }
        }

        private void TimecodesSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (TimecodesSearchPlaceholderText is not null)
            {
                TimecodesSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(TimecodesSearchBox?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void ToggleTimecodePopup()
        {
            if (TimecodePopup is null)
            {
                return;
            }

            if (_timecodePopupVisible)
            {
                HideTimecodePopup();
            }
            else
            {
                _ = ShowTimecodePopupAsync();
            }
        }

        private void RequestTimecodeCapture()
        {
            _ = RequestTimecodeCaptureAsync();
        }

        private async Task RequestTimecodeCaptureAsync()
        {
            if (TimecodePopup is null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastTimecodeTriggerUtc).TotalMilliseconds <= 180)
            {
                return;
            }

            _lastTimecodeTriggerUtc = now;
            if (_isOpeningTimecodePopup)
            {
                return;
            }

            _isOpeningTimecodePopup = true;
            try
            {
                if (_timecodePopupVisible)
                {
                    TimecodePopupLabelBox?.Focus();
                    TimecodePopupLabelBox?.SelectAll();
                    return;
                }

                await ShowTimecodePopupAsync();
            }
            finally
            {
                _isOpeningTimecodePopup = false;
            }
        }

        private async Task<int> GetBestCurrentPlaybackSecondsAsync()
        {
            var fallback = (int)Math.Round(_lastMeasuredTime > 0 ? _lastMeasuredTime : 0);
            if (Player?.CoreWebView2 is null)
            {
                return fallback;
            }

            try
            {
                var result = await Player.CoreWebView2.ExecuteScriptAsync(
                    "(() => { const v = document.querySelector('video'); return v ? Math.floor(v.currentTime || 0) : -1; })()");
                if (int.TryParse(result?.Trim('"'), out var parsed) && parsed >= 0)
                {
                    _lastMeasuredTime = parsed;
                    return parsed;
                }
            }
            catch
            {
                // Fall back to sampled value.
            }

            return fallback;
        }

        private async Task ShowTimecodePopupAsync()
        {
            _editingTimecodeId = null;
            var seconds = await GetBestCurrentPlaybackSecondsAsync();
            OpenTimecodePopup(seconds, PT($"Таймкод {TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss}", $"Timecode {TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss}"), isEditing: false);
        }

        private void OpenTimecodePopup(int seconds, string initialLabel, bool isEditing)
        {
            if (TimecodePopup is not Popup popup)
            {
                return;
            }

            _pendingTimecodeSeconds = Math.Max(0, seconds);
            var formatted = TimeSpan.FromSeconds(_pendingTimecodeSeconds).ToString(@"hh\:mm\:ss");

            if (TimecodePopupHeaderText is not null)
            {
                TimecodePopupHeaderText.Text = isEditing
                    ? PT("Редактировать таймкод", "Edit timecode")
                    : PT("Сохранить таймкод", "Save timecode");
            }

            if (TimecodePopupHintText is not null)
            {
                TimecodePopupHintText.Text = isEditing
                    ? PT("Измени название и сохрани обновлённый таймкод.", "Update the title and save the edited timecode.")
                    : PT("Сохрани важный момент и дай ему понятное название.", "Save the current moment with a clear title.");
            }

            if (TimecodePopupTimeText is not null)
            {
                TimecodePopupTimeText.Text = PT($"Позиция в видео: {formatted}", $"Video position: {formatted}");
            }

            if (TimecodePopupLabelCaptionText is not null)
            {
                TimecodePopupLabelCaptionText.Text = PT("Название таймкода", "Timecode title");
            }

            if (TimecodePopupLabelBox is not null)
            {
                TimecodePopupLabelBox.Text = initialLabel;
                TimecodePopupLabelBox.Tag = PT("Например: смешной момент, крик, баг, лучший килл", "Example: funny moment, scream, bug, best play");
            }

            if (TimecodePopupSaveButton is not null)
            {
                TimecodePopupSaveButton.Content = isEditing
                    ? PT("Сохранить изменения", "Save changes")
                    : PT("Сохранить таймкод", "Save timecode");
            }

            if (TimecodePopupCancelButton is not null)
            {
                TimecodePopupCancelButton.Content = PT("Отмена", "Cancel");
            }

            popup.IsOpen = true;
            _timecodePopupVisible = true;
            UnregisterGlobalHotkeys();

            _ = Dispatcher.BeginInvoke(() =>
            {
                TimecodePopupLabelBox?.Focus();
                TimecodePopupLabelBox?.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void HideTimecodePopup()
        {
            if (TimecodePopup is Popup popup)
            {
                popup.IsOpen = false;
            }

            _timecodePopupVisible = false;
            _editingTimecodeId = null;
            Dispatcher.BeginInvoke(UpdateGlobalHotkeysForTypingFocusState, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TimecodePopupSaveButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SaveTimecodeFromPopupAsync();
        }

        private void TimecodePopupLabelBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SaveTimecodeFromPopupAsync();
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideTimecodePopup();
            }
        }

        private async Task SaveTimecodeFromPopupAsync()
        {
            var seconds = Math.Max(0, _pendingTimecodeSeconds);
            var label = TimecodePopupLabelBox?.Text.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = PT($"Таймкод {TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss}", $"Timecode {TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss}");
            }

            var editingId = _editingTimecodeId;
            HideTimecodePopup();

            try
            {
                var all = await _playerTimecodeService.LoadAsync();

                if (!string.IsNullOrWhiteSpace(editingId))
                {
                    var existing = all.FirstOrDefault(t => string.Equals(t.Id, editingId, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                    {
                        existing.Label = label;
                    }
                }
                else
                {
                    all.Add(new Timecode
                    {
                        VideoId = _video.VideoId,
                        VideoTitle = _video.Title,
                        ThumbnailUrl = _video.ThumbnailUrl,
                        Seconds = seconds,
                        Label = label,
                        SavedAtUtc = DateTime.UtcNow
                    });
                }

                await _playerTimecodeService.SaveAsync(all);
                ApplyLoadedTimecodes(all);
            }
            catch
            {
                // Best-effort persistence.
            }
        }

        private void TimecodePopupCancelButton_Click(object sender, RoutedEventArgs e)
        {
            HideTimecodePopup();
        }

        private void TimecodeAddFromPanelButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowTimecodePopupAsync();
        }

        private void TimecodeItem_SeekClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
            {
                return;
            }

            if (!int.TryParse(btn.Tag?.ToString(), out var seconds))
            {
                return;
            }

            _ = SeekToTimecodeAsync(seconds);
        }

        private void TimecodeItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el)
            {
                return;
            }

            if (el.DataContext is not Timecode tc || !tc.IsCurrentVideo)
            {
                return;
            }

            _ = SeekToTimecodeAsync(tc.Seconds);
        }

        private async Task SeekToTimecodeAsync(int seconds)
        {
            if (Player?.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                var script = $"try {{ document.querySelector('video').currentTime = {seconds}; }} catch {{}}";
                await Player.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Best-effort seek.
            }
        }

        private void TimecodeItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || el.DataContext is not Timecode tc)
            {
                return;
            }

            ShowTimecodeContextMenu(tc);
            e.Handled = true;
        }

        private void ShowTimecodeContextMenu(Timecode tc)
        {
            var cm = new ContextMenu
            {
                Background = System.Windows.Media.Brushes.Transparent,
                HasDropShadow = true
            };

            var labelText = string.IsNullOrWhiteSpace(tc.Label) ? tc.TimeFormatted : tc.Label;
            var copyTimecode = new MenuItem
            {
                Header = $"📋  {PT("Копировать таймкод", "Copy timecode")}  {tc.TimeFormatted}",
                Foreground = System.Windows.Media.Brushes.White,
                Background = System.Windows.Media.Brushes.Transparent
            };
            copyTimecode.Click += (_, _) =>
            {
                try { Clipboard.SetText(tc.TimeFormatted); } catch { }
            };

            var copyLabel = new MenuItem
            {
                Header = $"📝  {PT("Копировать название", "Copy label")}  {labelText}",
                Foreground = System.Windows.Media.Brushes.White,
                Background = System.Windows.Media.Brushes.Transparent
            };
            copyLabel.Click += (_, _) =>
            {
                try { Clipboard.SetText(labelText); } catch { }
            };

            var rename = new MenuItem
            {
                Header = $"✏  {PT("Переименовать", "Rename")}",
                Foreground = System.Windows.Media.Brushes.White,
                Background = System.Windows.Media.Brushes.Transparent
            };
            rename.Click += (_, _) =>
            {
                _editingTimecodeId = tc.Id;
                OpenTimecodePopup(
                    tc.Seconds,
                    string.IsNullOrWhiteSpace(tc.Label) ? PT($"Таймкод {tc.TimeFormatted}", $"Timecode {tc.TimeFormatted}") : tc.Label,
                    isEditing: true);
            };

            cm.Items.Add(copyTimecode);
            cm.Items.Add(copyLabel);
            cm.Items.Add(rename);
            cm.IsOpen = true;
        }

        private void TimecodeDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
            {
                return;
            }

            var id = btn.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            _ = DeleteTimecodeAsync(id);
        }

        private async Task DeleteTimecodeAsync(string id)
        {
            try
            {
                var all = await _playerTimecodeService.LoadAsync();
                all.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                await _playerTimecodeService.SaveAsync(all);
                ApplyLoadedTimecodes(all);
            }
            catch
            {
                // Best-effort persistence.
            }
        }
    }
}
