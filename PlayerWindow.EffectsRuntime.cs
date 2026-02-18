using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow : Window
    {
        private void PlayerWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateWindowSizeButtonState();
            RequestApplyEffects(immediate: true);
            TriggerEffectsReapplyBurst();
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (!_isPlayerElementFullScreen)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (_isPlayerElementFullScreen)
                {
                    ApplyFullMonitorBounds();
                    RequestApplyEffects(immediate: false);
                }
            }, DispatcherPriority.Background);
        }

        private void PlayerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || _isPlayerElementFullScreen)
            {
                return;
            }

            _isResizeInteractionInProgress = true;
            _resizeSettleDebounceTimer.Stop();
            _resizeSettleDebounceTimer.Start();
        }

        private void ResizeSettleDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _resizeSettleDebounceTimer.Stop();
            _isResizeInteractionInProgress = false;
            RequestApplyEffects(immediate: false);
        }

        private void TriggerEffectsReapplyBurst()
        {
            _ = ApplyEffectsSafelyAsync(force: true);
            if (_isEffectsBurstReapplyRunning)
            {
                return;
            }

            _ = ReapplyEffectsBurstAsync();
        }

        private async Task ReapplyEffectsBurstAsync()
        {
            _isEffectsBurstReapplyRunning = true;
            try
            {
                await Task.Delay(120);
                await ApplyEffectsSafelyAsync(force: true);
                await Task.Delay(280);
                await ApplyEffectsSafelyAsync(force: true);
                await Task.Delay(500);
                await ApplyEffectsSafelyAsync(force: true);
            }
            finally
            {
                _isEffectsBurstReapplyRunning = false;
            }
        }
    }
}
