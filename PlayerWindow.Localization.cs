using System.Windows.Controls;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private bool IsEnglishPlayerLanguage =>
            string.Equals(_appSettings.Language, "en", StringComparison.OrdinalIgnoreCase);

        private string PT(string ru, string en) => IsEnglishPlayerLanguage ? en : ru;

        private void ApplyPlayerLocalization()
        {
            BackToListButton.Content = PT("Назад к списку", "Back to list");
            ToggleEffectsPanelButton.Content = _effectsPanelExpanded
                ? PT("Скрыть эффекты", "Hide effects")
                : PT("Показать эффекты", "Show effects");
            PlayerLoadingHeaderText.Text = PT("Загрузка плеера", "Loading player");
            EffectsHeaderText.Text = PT("Эффекты плеера", "Player effects");
            EffectsHintText.Text = PT(
                "Включайте эффекты и настраивайте силу прямо под каждым эффектом.",
                "Enable effects and tune each strength slider.");
            ResetEffectsButton.Content = PT("Сбросить все эффекты", "Reset all effects");

            Fx1.Content = PT("1. Без цвета", "1. Grayscale");
            Fx2.Content = PT("2. Сепия", "2. Sepia");
            Fx3.Content = PT("3. Инверсия", "3. Invert");
            Fx4.Content = PT("4. Высокий контраст", "4. High contrast");
            Fx5.Content = PT("5. Затемнение", "5. Darkness");
            Fx6.Content = PT("6. Насыщенность", "6. Saturation");
            Fx7.Content = PT("7. Сдвиг оттенка", "7. Hue shift");
            Fx8.Content = PT("8. Размытие", "8. Blur");
            Fx9.Content = PT("9. Фишай", "9. Fisheye");
            Fx10.Content = PT("10. VHS сканлайн", "10. VHS scanline");
            Fx11.Content = PT("11. Тряска кадра", "11. Screen shake");
            Fx12.Content = PT("12. Горизонтальное зеркало", "12. Horizontal mirror");
            Fx13.Content = PT("13. JPEG-помехи", "13. JPEG damage");
            Fx14.Content = PT("14. Холодный тон", "14. Cold tone");
            Fx15.Content = PT("15. Вертикальное зеркало", "15. Vertical mirror");

            if (FisheyeCenterPadTitleText is not null)
            {
                FisheyeCenterPadTitleText.Text = PT("Центр фишая", "Fisheye center");
            }

            if (AudioFxHeaderText is not null)
            {
                AudioFxHeaderText.Text = PT("Аудио FX", "Audio FX");
            }

            if (AudioFxHintText is not null)
            {
                AudioFxHintText.Text = PT(
                    "Эквалайзер, питч, реверб и базовые звуковые эффекты для текущего видео.",
                    "EQ, pitch, reverb and basic sound effects for the current video.");
            }

            if (ResetAudioFxButton is not null)
            {
                ResetAudioFxButton.Content = PT("Сбросить Audio FX", "Reset Audio FX");
            }

            if (AudioFxVolumeLabelText is not null)
            {
                AudioFxVolumeLabelText.Text = PT("Усиление громкости", "Volume boost");
            }

            if (AudioFxEqLowLabelText is not null)
            {
                AudioFxEqLowLabelText.Text = PT("EQ Низ (дБ)", "EQ Low (dB)");
            }

            if (AudioFxEqMidLabelText is not null)
            {
                AudioFxEqMidLabelText.Text = PT("EQ Середина (дБ)", "EQ Mid (dB)");
            }

            if (AudioFxEqHighLabelText is not null)
            {
                AudioFxEqHighLabelText.Text = PT("EQ Верх (дБ)", "EQ High (dB)");
            }

            if (AudioFxPitchLabelText is not null)
            {
                AudioFxPitchLabelText.Text = PT("Питч (полутоны)", "Pitch (semitones)");
            }

            if (AudioFxReverbLabelText is not null)
            {
                AudioFxReverbLabelText.Text = PT("Реверб", "Reverb");
            }

            if (AudioFxEchoLabelText is not null)
            {
                AudioFxEchoLabelText.Text = PT("Эхо", "Echo");
            }

            if (AudioFxDistortionLabelText is not null)
            {
                AudioFxDistortionLabelText.Text = PT("Дисторшн", "Distortion");
            }

            if (AudioFxWobbleLabelText is not null)
            {
                AudioFxWobbleLabelText.Text = PT("Wobble", "Wobble");
            }

            if (TimecodesPanelHintText is not null)
            {
                TimecodesPanelHintText.Text = PT(
                    "Нажми M или кнопку + Таймкод, чтобы сохранить текущий момент видео.",
                    "Press M or + Timecode to save the current moment.");
            }

            if (TimecodesPanelEmptyText is not null)
            {
                TimecodesPanelEmptyText.Text = PT(
                    "Нет сохранённых таймкодов. Нажми M или + Таймкод.",
                    "No saved timecodes yet. Press M or + Timecode.");
            }

            if (TimecodeAddFromPanelButton is not null)
            {
                TimecodeAddFromPanelButton.Content = PT("+ Таймкод", "+ Timecode");
            }

            if (TimecodesPanelSummaryText is not null)
            {
                TimecodesPanelSummaryText.Text = PT(
                    "Сначала показываются таймкоды этого видео, затем остальные.",
                    "Current video timecodes are shown first, then the rest.");
            }

            if (TimecodesSearchPlaceholderText is not null)
            {
                TimecodesSearchPlaceholderText.Text = PT(
                    "Поиск по названию, времени или видео...",
                    "Search by title, time or video...");
            }

            if (TimecodePopupHintText is not null)
            {
                TimecodePopupHintText.Text = PT(
                    "Сохрани важный момент и дай ему понятное название.",
                    "Save the current moment with a clear title.");
            }

            if (TimecodePopupLabelCaptionText is not null)
            {
                TimecodePopupLabelCaptionText.Text = PT("Название таймкода", "Timecode title");
            }

            if (TimecodePopupSaveButton is not null)
            {
                TimecodePopupSaveButton.Content = PT("Сохранить таймкод", "Save timecode");
            }

            if (TimecodePopupCancelButton is not null)
            {
                TimecodePopupCancelButton.Content = PT("Отмена", "Cancel");
            }

            if (BlockedNavToastText is not null)
            {
                BlockedNavToastText.Text = PT("🔒 Только видео YouTube", "🔒 YouTube videos only");
            }

            if (NonJokerBannerText is not null)
            {
                NonJokerBannerText.Text = PT(
                    "⚠ Стороннее видео — XP и задания не начисляются",
                    "⚠ Non-JokerDBD video — no XP or quest progress");
            }

            if (PlayerSfxSpamHeaderText is not null)
            {
                PlayerSfxSpamHeaderText.Text = PT("Режим спама звуков", "Sound spam mode");
            }

            if (PlayerSfxSpamDescText is not null)
            {
                PlayerSfxSpamDescText.Text = PT(
                    "Вкл: до 8 разных эффектов; повтор того же заменяет предыдущий (не залипает). Выкл: повтор — стоп.",
                    "On: up to 8 different effects; repeating the same one replaces the previous (no unstoppable stack). Off: repeat press stops.");
            }
            ApplyCommunityLocalization();
        }
    }
}
