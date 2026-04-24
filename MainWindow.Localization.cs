using System.Windows.Controls;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private bool IsEnglishLanguage =>
            string.Equals(_appSettings.Language, "en", StringComparison.OrdinalIgnoreCase);

        private string T(string ru, string en) => IsEnglishLanguage ? en : ru;

        private void ApplyLocalization()
        {
            if (!IsLoaded)
            {
                return;
            }

            HomeNavButton.Content = T("Главная", "Home");
            FavoritesNavButton.Content = T("Избранное", "Favorites");
            ProfileNavButton.Content = T("Профиль", "Profile");
            SettingsNavButton.Content = T("Настройки", "Settings");
            HomeOverviewHeaderText.Text = string.Empty;
            RecommendationsHeaderText.Text = T("Рекомендации", "Recommendations");

            // Stats bar labels
            StatsStreakLabel.Text = T("Серия", "Streak");
            StatsTotalHoursLabel.Text = T("Часов просмотра", "Watch hours");
            StatsStreamCountLabel.Text = T("Стримов в каталоге", "Streams in catalog");

            // Continue Watching / Queue
            ContinueWatchingHeaderText.Text = T("Продолжить просмотр", "Continue Watching");
            WatchQueueHeaderText.Text = T("Очередь", "Queue");
            PlayQueueButton.Content = T("▶ Смотреть всё", "▶ Play all");

            // Category filter chips
            FilterAllButton.Content = T("Все", "All");
            FilterFavoritesButton.Content = T("★ Избранное", "★ Favorites");
            RandomStreamButton.Content = T("🔀 Случайный", "🔀 Random");

            if (TopTabControl.Items.Count >= 6)
            {
                ((TabItem)TopTabControl.Items[0]).Header = T("Главная", "Home");
                ((TabItem)TopTabControl.Items[1]).Header = T("Избранное", "Favorites");
                ((TabItem)TopTabControl.Items[2]).Header = T("Профиль", "Profile");
                ((TabItem)TopTabControl.Items[3]).Header = T("Задания", "Quests");
                ((TabItem)TopTabControl.Items[4]).Header = T("Настройки", "Settings");
                ((TabItem)TopTabControl.Items[5]).Header = "Watch Together";
            }

            if (TwitchWatchButtonText is not null)
            {
                TwitchWatchButtonText.Text = T("Открыть Twitch", "Open Twitch");
            }

            if (SearchPlaceholderText is not null)
            {
                SearchPlaceholderText.Text = T("Поиск стримов...", "Search streams...");
            }

            if (SortWatchedOldestItem is not null)
            {
                SortWatchedOldestItem.Content = T("Сначала старые просмотры", "Oldest watched first");
            }

            if (SortWatchedRecentItem is not null)
            {
                SortWatchedRecentItem.Content = T("Сначала новые просмотры", "Newest watched first");
            }

            if (SortChannelOrderItem is not null)
            {
                SortChannelOrderItem.Content = T("По порядку канала", "Channel order");
            }

            UpdateProgramButton.Content = T("Обновить программу", "Update app");
            RestartProgramButton.Content = T("Перезапустить программу", "Restart app");
            ApplyLocalizedUpdateStatusText();

            SettingsHeaderText.Text = T("Настройки", "Settings");
            SettingsSubtitleText.Text = T("Персонализация и поведение приложения", "Personalization and app behavior");
            AutoStartLabelText.Text = T("Автозапуск", "Autostart");
            AutoStartCheckBox.Content = T("Запускать вместе с Windows", "Launch with Windows");
            if (AutoCheckUpdatesCheckBox is not null)
            {
                AutoCheckUpdatesCheckBox.Content = T("Проверять обновления при запуске", "Check for updates on startup");
            }
            LanguageLabelText.Text = T("Язык", "Language");
            UiScaleLabelText.Text = T("Масштаб UI", "UI scale");
            AnimationsEnabledCheckBox.Content = T("Плавные анимации интерфейса", "Smooth UI animations");
            LoggingEnabledCheckBox.Content = T("Включить логирование ошибок", "Enable error logging");
            LogViewerHeaderText.Text = T("Лог событий", "Event log");
            LogViewerDescText.Text = T(
                "Последние события приложения в реальном времени.",
                "Recent app events in real time.");
            LogViewerErrorsOnlyCheckBox.Content = T("Только ошибки", "Errors only");
            LogViewerCopyButton.Content = T("Копировать", "Copy");
            LogViewerClearButton.Content = T("Очистить", "Clear");

            SoundModeHeaderText.Text = T("Режим звуковых эффектов", "Sound effects mode");
            SoundModeDescText.Text = T(
                "Выкл — повтор останавливает звук. Вкл — до 8 разных эффектов; тот же эффект при повторе заменяет предыдущий.",
                "Off — repeat press stops the sound. On — up to 8 different effects; repeating the same effect replaces the previous one.");
            SoundSpamModeToggle.Content = string.Empty;
            EqForSoundFxHeaderText.Text = T("Эквалайзер для звуковых эффектов", "Equalizer for sound effects");
            EqForSoundFxDescText.Text = T(
                "Применять настройки эквалайзера плеера к звуковым эффектам.",
                "Apply player equalizer settings to sound effects.");
            ApplyEqToSoundEffectsCheckBox.Content = string.Empty;
            FullscreenBehaviorLabelText.Text = T("Поведение fullscreen", "Fullscreen behavior");
            FullscreenBehaviorDescriptionText.Text = T(
                "Auto: при fullscreen внутри YouTube окно плеера тоже становится fullscreen. Windowed: окно остается обычным.",
                "Auto: when YouTube enters fullscreen, player window follows. Windowed: keep normal window mode.");
            CacheLabelText.Text = T("Данные и кеш", "Data & cache");
            CacheDescriptionText.Text = T(
                "Очищает WebView2 профиль (YouTube данные) и временные обновления.",
                "Clears WebView2 profile (YouTube data) and temporary update files.");
            ResetCacheButton.Content = T("Сбросить кеш", "Reset cache");
            if (OpenLogFolderButton is not null)
            {
                OpenLogFolderButton.Content = T("Открыть логи", "Open logs");
            }
            BindsSectionTitleText.Text = T("Бинды плеера", "Player binds");
            BindsSectionDescriptionText.Text = T(
                "Клавиши для панели эффектов, всех 15 эффектов и саунд-эффектов.",
                "Keys for effects panel, all 15 effects, and sound effects.");
            BindCaptureHelpText.Text = T(
                "Нажмите «Назначить», затем любую клавишу. Esc - отмена.",
                "Click Assign, then press any key. Esc cancels.");
            HideEffectsBindLabelText.Text = T("Скрыть/показать эффекты", "Hide/show effects panel");
            AuraFarmBindLabelText.Text = T("Звук Aura Farm", "Aura Farm sound");
            LaughBindLabelText.Text = T("Звук Смех", "Laugh sound");
            PsiBindLabelText.Text = T("Звук Пси-излучение", "Psi radiation sound");
            RespectBindLabelText.Text = T("Звук +Respect", "+Respect sound");
            if (SadBindLabelText is not null)
            {
                SadBindLabelText.Text = T("Звук Грусти", "Sadness sound");
            }
            EffectBindsDescriptionText.Text = T("Бинды эффектов (1–15)", "Effect binds (1–15)");
            if (EffectBind1LabelText  is not null) EffectBind1LabelText.Text  = T("1. Без цвета",               "1. Grayscale");
            if (EffectBind2LabelText  is not null) EffectBind2LabelText.Text  = T("2. Сепия",                   "2. Sepia");
            if (EffectBind3LabelText  is not null) EffectBind3LabelText.Text  = T("3. Инверсия",                "3. Invert");
            if (EffectBind4LabelText  is not null) EffectBind4LabelText.Text  = T("4. Высокий контраст",        "4. High contrast");
            if (EffectBind5LabelText  is not null) EffectBind5LabelText.Text  = T("5. Затемнение",              "5. Darkness");
            if (EffectBind6LabelText  is not null) EffectBind6LabelText.Text  = T("6. Насыщенность",            "6. Saturation");
            if (EffectBind7LabelText  is not null) EffectBind7LabelText.Text  = T("7. Сдвиг оттенка",          "7. Hue shift");
            if (EffectBind8LabelText  is not null) EffectBind8LabelText.Text  = T("8. Размытие",                "8. Blur");
            if (EffectBind9LabelText  is not null) EffectBind9LabelText.Text  = T("9. Фишай",                   "9. Fisheye");
            if (EffectBind10LabelText is not null) EffectBind10LabelText.Text = T("10. VHS сканлайн",           "10. VHS scanline");
            if (EffectBind11LabelText is not null) EffectBind11LabelText.Text = T("11. Тряска кадра",           "11. Screen shake");
            if (EffectBind12LabelText is not null) EffectBind12LabelText.Text = T("12. Горизонтальное зеркало", "12. H. mirror");
            if (EffectBind13LabelText is not null) EffectBind13LabelText.Text = T("13. JPEG-помехи",            "13. JPEG damage");
            if (EffectBind14LabelText is not null) EffectBind14LabelText.Text = T("14. Холодный тон",           "14. Cold tone");
            if (EffectBind15LabelText is not null) EffectBind15LabelText.Text = T("15. Вертикальное зеркало",   "15. V. mirror");
            ResetBindsButton.Content = T("Сбросить бинды по умолчанию", "Reset binds to defaults");

            if (LanguageComboBox.Items.Count >= 2)
            {
                ((ComboBoxItem)LanguageComboBox.Items[0]).Content = T("Русский", "Russian");
                ((ComboBoxItem)LanguageComboBox.Items[1]).Content = "English";
            }

            if (FullscreenBehaviorComboBox.Items.Count >= 2)
            {
                ((ComboBoxItem)FullscreenBehaviorComboBox.Items[0]).Content = T(
                    "Авто: окно следует fullscreen плеера",
                    "Auto: window follows player fullscreen");
                ((ComboBoxItem)FullscreenBehaviorComboBox.Items[1]).Content = T(
                    "Оставаться в оконном режиме",
                    "Keep windowed mode");
            }

            ProfileHeaderText.Text = T("Профиль", "Profile");
            AchievementsHeaderText.Text = T("Достижения", "Achievements");
            OpenQuestsButton.Content = T("Открыть задания", "Open quests");
            RecentStreamsHeaderText.Text = T("Последние просмотренные", "Recently watched");

            QuestsPageHeaderText.Text = T("Задания", "Quests");
            QuestsDailyHeaderText.Text = T("Ежедневные задания", "Daily quests");
            QuestsWeeklyHeaderText.Text = T("Еженедельные задания", "Weekly quests");
            QuestsDailyResetText.Text = T("Обновление через: --:--:--", "Resets in: --:--:--");
            QuestsWeeklyResetText.Text = T("Обновление через: --:--:--", "Resets in: --:--:--");
            BackToProfileButton.Content = T("Назад в профиль", "Back to profile");

            LoadingTitleText.Text = T("Загрузка приложения", "Loading app");

            if (FavoritesClipsHeaderText is not null)
            {
                FavoritesClipsHeaderText.Text = T("Таймкоды", "Timecodes");
            }

            if (FavoritesClipsEmptyText is not null)
            {
                FavoritesClipsEmptyText.Text = T(
                    "Нет сохранённых клипов. Открой видео и нажми M.",
                    "No saved clips. Open a video and press M.");
            }

            WatchTogetherNavButton.Content = "Watch Together";
            WatchTogetherHeaderText.Text = "Watch Together";
            WatchTogetherSubtitleText.Text = T(
                "Смотрите стримы вместе с друзьями через Radmin VPN",
                "Watch streams together with friends via Radmin VPN");
            WtConnectionHeaderText.Text = T("Подключение", "Connection");
            WtConnectionDescText.Text = T(
                "Убедитесь, что оба игрока подключены к одной сети Radmin VPN.",
                "Make sure both players are connected to the same Radmin VPN network.");
            WtPortLabelText.Text = T("Порт", "Port");
            WtHostButton.Content = T("Создать комнату (Хост)", "Create room (Host)");
            WtIpLabelText.Text = T("IP адрес хоста", "Host IP address");
            WtConnectButton.Content = T("Подключиться (Гость)", "Connect (Guest)");
            WtDisconnectButton.Content = T("Отключиться", "Disconnect");
            WtStatusHeaderText.Text = T("Статус сервера", "Server status");
            WtPeersHeaderText.Text = T("Подключённые участники", "Connected participants");
            WtGuestConnectedText.Text = T("Подключено к хосту", "Connected to host");
            WtInfoHeaderText.Text = T("Как это работает", "How it works");
            WtInfoText.Text = T(
                "1. Оба игрока запускают Radmin VPN и подключаются к одной сети.\n2. Один игрок нажимает «Создать комнату» — он становится хостом.\n3. Второй игрок вводит IP хоста (из Radmin VPN) и нажимает «Подключиться».\n4. Хост открывает видео — оно автоматически откроется у гостя.\n5. Play, pause и перемотка синхронизируются автоматически.",
                "1. Both players launch Radmin VPN and join the same network.\n2. One player clicks \"Create room\" — they become the host.\n3. The other player enters the host's IP (from Radmin VPN) and clicks \"Connect\".\n4. Host opens a video — it automatically opens for the guest.\n5. Play, pause, and seek are synced automatically.");
            WtMyIpHeaderText.Text = T("Скажи другу этот IP:", "Tell your friend this IP:");
            UpdateWatchTogetherUiState();

            UpdateBindDisplayTexts();
            UpdateBindCaptureButtons();

            RefreshHomeSummary();
            RefreshProfile();
            RefreshQuestsPage();
            RefreshFavoritesSummary();
            RefreshFavoritesClipsView();
            RefreshSearchPlaceholderText();
            UpdateStreakText();
        }

        private void RefreshSearchPlaceholderText()
        {
            if (SearchPlaceholderText is null)
            {
                return;
            }

            SearchPlaceholderText.Text = IsFavoritesTabSelected()
                ? T("Поиск по избранному и таймкодам...", "Search favorites and timecodes...")
                : T("Поиск стримов...", "Search streams...");
        }

        private void ApplyLocalizedUpdateStatusText()
        {
            if (UpdateStatusText is null || _lastUpdateInfo is null)
            {
                return;
            }

            if (!_lastUpdateInfo.IsCheckSuccessful)
            {
                UpdateStatusText.Text = T("● Не удалось проверить обновления", "● Failed to check updates");
                return;
            }

            if (_lastUpdateInfo.IsUpdateAvailable)
            {
                UpdateStatusText.Text = T(
                    $"● Доступно обновление {_lastUpdateInfo.LatestVersionText}",
                    $"● Update available {_lastUpdateInfo.LatestVersionText}");
                return;
            }

            UpdateStatusText.Text = T("● Установлено последнее обновление", "● You are up to date");
        }
    }
}
