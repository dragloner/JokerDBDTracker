using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using JokerDBDTracker.Models;
using System.Windows.Media.Animation;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private const int MainMinimizeAnimationDurationMs = 520;
        private const double MainMinimizeScaleTo = 0.968;
        private bool AreAnimationsEnabled => _appSettings.AnimationsEnabled && SystemParameters.ClientAreaAnimation;

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
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            animation.Completed += (_, _) => tcs.TrySetResult(true);
            BeginAnimation(OpacityProperty, animation);
            return tcs.Task;
        }

        private async Task AnimateCoverToPlayerAsync(YouTubeVideo video)
        {
            if (!AreAnimationsEnabled || !Uri.TryCreate(video.ThumbnailUrl, UriKind.Absolute, out var cover) ||
                cover.Scheme != Uri.UriSchemeHttps) return;

            FrameworkElement? source = null;
            foreach (var list in new[] { VideoList, ContinueWatchingList, RecommendationsList, RecentStreamsList })
            {
                if (list.IsVisible && list.ItemContainerGenerator.ContainerFromItem(video) is ListBoxItem card &&
                    card.IsVisible && card.ActualWidth > 0)
                {
                    source = card;
                    break;
                }
            }
            if (source is null && HeroCover.IsVisible) source = HeroCover;
            if (source is null || source.ActualWidth <= 0 || source.ActualHeight <= 0) return;

            var origin = source.TranslatePoint(new Point(), MainRootBorder);
            if (origin.Y < 0 || origin.Y >= MainRootBorder.ActualHeight) return;
            var scale = new ScaleTransform();
            var shift = new TranslateTransform();
            var transform = new TransformGroup();
            transform.Children.Add(scale);
            transform.Children.Add(shift);
            var coverPlate = (Border)PlayerTransition.Children[0];
            coverPlate.RenderTransform = transform;
            PlayerTransitionCover.Source = new BitmapImage(cover);
            PlayerTransition.Visibility = Visibility.Visible;
            var duration = TimeSpan.FromMilliseconds(250);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            DoubleAnimation Move(double from, double to) => new(from, to, duration) { EasingFunction = ease };
            var vertical = Move(origin.Y, 0);
            vertical.Completed += (_, _) => completed.TrySetResult();
            try
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, Move(Math.Clamp(source.ActualWidth / MainRootBorder.ActualWidth, .1, 1), 1));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Move(Math.Clamp(source.ActualHeight / MainRootBorder.ActualHeight, .08, 1), 1));
                shift.BeginAnimation(TranslateTransform.XProperty, Move(origin.X, 0));
                shift.BeginAnimation(TranslateTransform.YProperty, vertical);
                await Task.WhenAny(completed.Task, Task.Delay(350));
            }
            finally
            {
                PlayerTransition.Visibility = Visibility.Collapsed;
                PlayerTransitionCover.Source = null;
            }
        }

        private void AnimatePanelEntrance(FrameworkElement element)
        {
            if (!AreAnimationsEnabled || element.Visibility != Visibility.Visible)
            {
                element.Opacity = 1;
                return;
            }

            var translation = element.RenderTransform as TranslateTransform;
            if (translation is null)
            {
                translation = new TranslateTransform();
                element.RenderTransform = translation;
            }
            translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(195))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            element.Opacity = 0;
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void AnimateMainStatePulse()
        {
            if (!AreAnimationsEnabled || MainRootBorder is null)
            {
                return;
            }

            MainRootBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            MainRootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = MainRootBorder.RenderTransform as ScaleTransform;
            if (scale is null)
            {
                scale = new ScaleTransform(1, 1);
                MainRootBorder.RenderTransform = scale;
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

                    var scaleDuration = TimeSpan.FromMilliseconds(MainMinimizeAnimationDurationMs);
                    var scaleEase = new CubicEase { EasingMode = EasingMode.EaseInOut };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, MainMinimizeScaleTo, scaleDuration)
                    {
                        EasingFunction = scaleEase
                    });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, MainMinimizeScaleTo, scaleDuration)
                    {
                        EasingFunction = scaleEase
                    });
                }

                await AnimateWindowOpacityAsync(0.0, MainMinimizeAnimationDurationMs);
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
