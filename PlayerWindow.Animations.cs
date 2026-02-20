using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private bool ArePlayerAnimationsEnabled => _appSettings.AnimationsEnabled;

        private void AnimatePlayerWindowEntrance()
        {
            if (!ArePlayerAnimationsEnabled || MainRootGrid is null)
            {
                return;
            }

            MainRootGrid.Opacity = 0;
            MainRootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
            MainRootGrid.RenderTransform = new ScaleTransform(0.992, 0.992);

            var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            MainRootGrid.BeginAnimation(UIElement.OpacityProperty, opacity);

            if (MainRootGrid.RenderTransform is ScaleTransform scale)
            {
                var scaleAnimation = new DoubleAnimation(0.992, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            }
        }

        private void AnimateWindowStatePulse()
        {
            if (!ArePlayerAnimationsEnabled || MainRootGrid is null)
            {
                return;
            }

            MainRootGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            MainRootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = MainRootGrid.RenderTransform as ScaleTransform;
            if (scale is null)
            {
                scale = new ScaleTransform(1, 1);
                MainRootGrid.RenderTransform = scale;
            }

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.994, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.994, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private async Task AnimatePlayerMinimizeAsync()
        {
            if (!ArePlayerAnimationsEnabled || _isPlayerMinimizeAnimating)
            {
                return;
            }

            _isPlayerMinimizeAnimating = true;
            try
            {
                if (MainRootGrid is not null)
                {
                    MainRootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
                    var scale = MainRootGrid.RenderTransform as ScaleTransform;
                    if (scale is null)
                    {
                        scale = new ScaleTransform(1, 1);
                        MainRootGrid.RenderTransform = scale;
                    }

                    var scaleDuration = TimeSpan.FromMilliseconds(340);
                    var scaleEase = new CubicEase { EasingMode = EasingMode.EaseInOut };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.970, scaleDuration)
                    {
                        EasingFunction = scaleEase
                    });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.970, scaleDuration)
                    {
                        EasingFunction = scaleEase
                    });
                }

                var tcs = new TaskCompletionSource<bool>();
                var opacityAnimation = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(340))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                opacityAnimation.Completed += (_, _) => tcs.TrySetResult(true);
                BeginAnimation(OpacityProperty, opacityAnimation);
                await tcs.Task;
            }
            catch
            {
                // Animation failures should not block minimizing.
            }
            finally
            {
                Opacity = 1;
                BeginAnimation(OpacityProperty, null);
                if (MainRootGrid?.RenderTransform is ScaleTransform scale)
                {
                    scale.ScaleX = 1;
                    scale.ScaleY = 1;
                }
                _isPlayerMinimizeAnimating = false;
            }
        }
    }
}
