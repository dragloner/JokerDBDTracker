using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private bool AreAnimationsEnabled => _appSettings.AnimationsEnabled;

        private Task AnimateWindowOpacityAsync(double toOpacity, int durationMs)
        {
            if (!AreAnimationsEnabled)
            {
                Opacity = toOpacity;
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            var animation = new DoubleAnimation
            {
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (_, _) => tcs.TrySetResult(true);
            BeginAnimation(OpacityProperty, animation);
            return tcs.Task;
        }

        private void AnimatePanelEntrance(FrameworkElement element)
        {
            if (!AreAnimationsEnabled || element.Visibility != Visibility.Visible)
            {
                element.Opacity = 1;
                return;
            }

            element.Opacity = 0;
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void AnimateMainStatePulse()
        {
            if (!AreAnimationsEnabled || MainRootBorder is null)
            {
                return;
            }

            MainRootBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

            MainRootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = MainRootBorder.RenderTransform as ScaleTransform;
            if (scale is null)
            {
                scale = new ScaleTransform(1, 1);
                MainRootBorder.RenderTransform = scale;
            }

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.988, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.988, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private async Task AnimateMainMinimizeAsync()
        {
            if (!AreAnimationsEnabled || _isMainMinimizeAnimating)
            {
                return;
            }

            _isMainMinimizeAnimating = true;
            try
            {
                if (MainRootBorder is not null)
                {
                    MainRootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
                    var scale = MainRootBorder.RenderTransform as ScaleTransform;
                    if (scale is null)
                    {
                        scale = new ScaleTransform(1, 1);
                        MainRootBorder.RenderTransform = scale;
                    }

                    var scaleDuration = TimeSpan.FromMilliseconds(190);
                    var scaleEase = new CubicEase { EasingMode = EasingMode.EaseIn };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.958, scaleDuration)
                    {
                        EasingFunction = scaleEase
                    });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.958, scaleDuration)
                    {
                        EasingFunction = scaleEase
                    });
                }

                await AnimateWindowOpacityAsync(0.0, 190);
            }
            catch
            {
                // Animation failures should not block minimizing.
            }
            finally
            {
                Opacity = 1;
                BeginAnimation(OpacityProperty, null);
                if (MainRootBorder?.RenderTransform is ScaleTransform scale)
                {
                    scale.ScaleX = 1;
                    scale.ScaleY = 1;
                }
                _isMainMinimizeAnimating = false;
            }
        }
    }
}
