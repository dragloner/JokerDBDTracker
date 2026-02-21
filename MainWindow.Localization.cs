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
            HomeOverviewHeaderText.Text = T("Обзор главной", "Home overview");
            RecommendationsHeaderText.Text = T("Рекомендации", "Recommendations");

            if (TopTabControl.Items.Count >= 5)
            {
                ((TabItem)TopTabControl.Items[0]).Header = T("Главная", "Home");
                ((TabItem)TopTabControl.Items[1]).Header = T("Избранное", "Favorites");
                ((TabItem)TopTabControl.Items[2]).Header = T("Профиль", "Profile");
                ((TabItem)TopTabControl.Items[3]).Header = T("Задания", "Quests");
                ((TabItem)TopTabControl.Items[4]).Header = T("Настройки", "Settings");
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
            LanguageLabelText.Text = T("Язык", "Language");
            UiScaleLabelText.Text = T("Масштаб UI", "UI scale");
            AnimationsEnabledCheckBox.Content = T("Плавные анимации интерфейса", "Smooth UI animations");
            LoggingEnabledCheckBox.Content = T("Включить логирование ошибок", "Enable error logging");
            FullscreenBehaviorLabelText.Text = T("Поведение fullscreen", "Fullscreen behavior");
            FullscreenBehaviorDescriptionText.Text = T(
                "Auto: при fullscreen внутри YouTube окно плеера тоже становится fullscreen. Windowed: окно остается обычным.",
                "Auto: when YouTube enters fullscreen, player window follows. Windowed: keep normal window mode.");
            CacheLabelText.Text = T("Кеш", "Cache");
            CacheDescriptionText.Text = T(
                "Очищает WebView2 профиль (YouTube данные) и локальные временные обновления.",
                "Clears WebView2 profile (YouTube data) and local temporary update files.");
            ResetCacheButton.Content = T("Сбросить кеш", "Reset cache");
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
            EffectBindsHeaderText.Text = T("Бинды эффектов", "Effect binds");
            EffectBindsDescriptionText.Text = T(
                "Каждый эффект (1-15) можно назначить на любую клавишу.",
                "Each effect (1-15) can be assigned to any key.");
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
            PrestigeButton.Content = T("Престиж", "Prestige");

            QuestsPageHeaderText.Text = T("Задания", "Quests");
            QuestsDailyHeaderText.Text = T("Ежедневные задания", "Daily quests");
            QuestsWeeklyHeaderText.Text = T("Еженедельные задания", "Weekly quests");
            QuestsDailyResetText.Text = T("Обновление через: --:--:--", "Resets in: --:--:--");
            QuestsWeeklyResetText.Text = T("Обновление через: --:--:--", "Resets in: --:--:--");
            BackToProfileButton.Content = T("Назад в профиль", "Back to profile");

            LoadingTitleText.Text = T("Загрузка приложения", "Loading app");

            UpdateBindDisplayTexts();
            UpdateBindCaptureButtons();

            RefreshHomeSummary();
            RefreshProfile();
            RefreshQuestsPage();
            UpdateStreakText();
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
