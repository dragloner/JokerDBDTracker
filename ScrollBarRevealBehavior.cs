using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JokerDBDTracker;

public static class ScrollBarRevealBehavior
{
    private const int RevealHoldMilliseconds = 900;

    public static readonly DependencyProperty EnableAutoRevealProperty =
        DependencyProperty.RegisterAttached(
            "EnableAutoReveal",
            typeof(bool),
            typeof(ScrollBarRevealBehavior),
            new PropertyMetadata(false, OnEnableAutoRevealChanged));

    public static readonly DependencyProperty RevealEdgeWidthProperty =
        DependencyProperty.RegisterAttached(
            "RevealEdgeWidth",
            typeof(double),
            typeof(ScrollBarRevealBehavior),
            new PropertyMetadata(34.0));

    public static readonly DependencyProperty IsRevealActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsRevealActive",
            typeof(bool),
            typeof(ScrollBarRevealBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(RevealState),
            typeof(ScrollBarRevealBehavior),
            new PropertyMetadata(null));

    public static bool GetEnableAutoReveal(DependencyObject obj) =>
        (bool)obj.GetValue(EnableAutoRevealProperty);

    public static void SetEnableAutoReveal(DependencyObject obj, bool value) =>
        obj.SetValue(EnableAutoRevealProperty, value);

    public static double GetRevealEdgeWidth(DependencyObject obj) =>
        (double)obj.GetValue(RevealEdgeWidthProperty);

    public static void SetRevealEdgeWidth(DependencyObject obj, double value) =>
        obj.SetValue(RevealEdgeWidthProperty, value);

    public static bool GetIsRevealActive(DependencyObject obj) =>
        (bool)obj.GetValue(IsRevealActiveProperty);

    public static void SetIsRevealActive(DependencyObject obj, bool value) =>
        obj.SetValue(IsRevealActiveProperty, value);

    private static void OnEnableAutoRevealChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollBar scrollBar)
        {
            return;
        }

        scrollBar.Loaded -= ScrollBar_Loaded;
        scrollBar.Unloaded -= ScrollBar_Unloaded;

        if ((bool)e.NewValue)
        {
            scrollBar.Loaded += ScrollBar_Loaded;
            scrollBar.Unloaded += ScrollBar_Unloaded;

            if (scrollBar.IsLoaded)
            {
                Attach(scrollBar);
            }
        }
        else
        {
            Detach(scrollBar);
            SetIsRevealActive(scrollBar, false);
        }
    }

    private static void ScrollBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollBar scrollBar && GetEnableAutoReveal(scrollBar))
        {
            Attach(scrollBar);
        }
    }

    private static void ScrollBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollBar scrollBar)
        {
            Detach(scrollBar);
            SetIsRevealActive(scrollBar, false);
        }
    }

    private static void Attach(ScrollBar scrollBar)
    {
        if (scrollBar.GetValue(StateProperty) is RevealState)
        {
            return;
        }

        ScrollViewer? host = FindAncestor<ScrollViewer>(scrollBar);
        if (host is null)
        {
            scrollBar.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!scrollBar.IsLoaded || !GetEnableAutoReveal(scrollBar))
                {
                    return;
                }

                Attach(scrollBar);
            }));
            return;
        }

        var state = new RevealState(scrollBar, host);
        scrollBar.SetValue(StateProperty, state);
        state.Attach();
        UpdateReveal(scrollBar, host, state);
    }

    private static void Detach(ScrollBar scrollBar)
    {
        if (scrollBar.GetValue(StateProperty) is not RevealState state)
        {
            return;
        }

        state.Detach();
        scrollBar.ClearValue(StateProperty);
    }

    private static void UpdateReveal(ScrollBar scrollBar, ScrollViewer host, RevealState state)
    {
        if (scrollBar.Orientation != Orientation.Vertical || !scrollBar.IsVisible || !scrollBar.IsEnabled)
        {
            SetIsRevealActive(scrollBar, false);
            return;
        }

        bool keepVisible =
            scrollBar.IsMouseOver ||
            scrollBar.IsMouseCaptureWithin ||
            host.IsKeyboardFocusWithin ||
            state.IsHoldActive;

        bool nearEdge = IsPointerNearVerticalEdge(scrollBar, host);
        SetIsRevealActive(scrollBar, keepVisible || nearEdge);
    }

    private static bool IsPointerNearVerticalEdge(ScrollBar scrollBar, ScrollViewer host)
    {
        if (host.ActualWidth <= 0 || host.ActualHeight <= 0)
        {
            return false;
        }

        Point p = Mouse.GetPosition(host);
        bool insideHost =
            p.X >= -0.5 && p.Y >= -0.5 &&
            p.X <= host.ActualWidth + 0.5 &&
            p.Y <= host.ActualHeight + 0.5;

        if (!insideHost)
        {
            return false;
        }

        double threshold = Math.Max(18.0, GetRevealEdgeWidth(scrollBar));
        threshold = Math.Max(threshold, scrollBar.ActualWidth + 8.0);
        threshold = Math.Min(threshold, Math.Max(18.0, host.ActualWidth));

        bool isRtl = host.FlowDirection == FlowDirection.RightToLeft;
        return isRtl ? p.X <= threshold : p.X >= host.ActualWidth - threshold;
    }

    private static void PulseReveal(ScrollBar scrollBar)
    {
        if (scrollBar.GetValue(StateProperty) is not RevealState state)
        {
            return;
        }

        state.Pulse();
        UpdateReveal(state.ScrollBar, state.Host, state);
    }

    private static void RefreshReveal(ScrollBar scrollBar)
    {
        if (scrollBar.GetValue(StateProperty) is not RevealState state)
        {
            return;
        }

        UpdateReveal(state.ScrollBar, state.Host, state);
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        DependencyObject? node = current;

        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private sealed class RevealState
    {
        private readonly DispatcherTimer _holdTimer;
        private DateTime _holdUntilUtc;

        public RevealState(ScrollBar scrollBar, ScrollViewer host)
        {
            ScrollBar = scrollBar;
            Host = host;
            _holdTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(90),
                DispatcherPriority.Background,
                HoldTimer_Tick,
                scrollBar.Dispatcher);
        }

        public ScrollBar ScrollBar { get; }

        public ScrollViewer Host { get; }

        public bool IsHoldActive => DateTime.UtcNow < _holdUntilUtc;

        public void Attach()
        {
            Host.PreviewMouseMove += Host_PreviewMouseMove;
            Host.MouseEnter += Host_MouseEnter;
            Host.MouseLeave += Host_MouseLeave;
            Host.SizeChanged += Host_SizeChanged;
            Host.GotKeyboardFocus += Host_GotKeyboardFocus;
            Host.LostKeyboardFocus += Host_LostKeyboardFocus;
            Host.ScrollChanged += Host_ScrollChanged;
            Host.PreviewMouseWheel += Host_PreviewMouseWheel;
            Host.PreviewKeyDown += Host_PreviewKeyDown;

            ScrollBar.MouseEnter += ScrollBar_MouseEnter;
            ScrollBar.MouseLeave += ScrollBar_MouseLeave;
            ScrollBar.PreviewMouseDown += ScrollBar_PreviewMouseDown;
            ScrollBar.IsVisibleChanged += ScrollBar_IsVisibleChanged;
            ScrollBar.IsEnabledChanged += ScrollBar_IsEnabledChanged;
        }

        public void Detach()
        {
            _holdTimer.Stop();

            Host.PreviewMouseMove -= Host_PreviewMouseMove;
            Host.MouseEnter -= Host_MouseEnter;
            Host.MouseLeave -= Host_MouseLeave;
            Host.SizeChanged -= Host_SizeChanged;
            Host.GotKeyboardFocus -= Host_GotKeyboardFocus;
            Host.LostKeyboardFocus -= Host_LostKeyboardFocus;
            Host.ScrollChanged -= Host_ScrollChanged;
            Host.PreviewMouseWheel -= Host_PreviewMouseWheel;
            Host.PreviewKeyDown -= Host_PreviewKeyDown;

            ScrollBar.MouseEnter -= ScrollBar_MouseEnter;
            ScrollBar.MouseLeave -= ScrollBar_MouseLeave;
            ScrollBar.PreviewMouseDown -= ScrollBar_PreviewMouseDown;
            ScrollBar.IsVisibleChanged -= ScrollBar_IsVisibleChanged;
            ScrollBar.IsEnabledChanged -= ScrollBar_IsEnabledChanged;
        }

        public void Pulse()
        {
            _holdUntilUtc = DateTime.UtcNow.AddMilliseconds(RevealHoldMilliseconds);
            if (!_holdTimer.IsEnabled)
            {
                _holdTimer.Start();
            }
        }

        private void HoldTimer_Tick(object? sender, EventArgs e)
        {
            RefreshReveal(ScrollBar);
            if (!IsHoldActive)
            {
                _holdTimer.Stop();
            }
        }

        private void Host_PreviewMouseMove(object sender, MouseEventArgs e) => RefreshReveal(ScrollBar);

        private void Host_MouseEnter(object sender, MouseEventArgs e) => RefreshReveal(ScrollBar);

        private void Host_MouseLeave(object sender, MouseEventArgs e) => RefreshReveal(ScrollBar);

        private void Host_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshReveal(ScrollBar);

        private void Host_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshReveal(ScrollBar);

        private void Host_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshReveal(ScrollBar);

        private void Host_ScrollChanged(object sender, ScrollChangedEventArgs e) => PulseReveal(ScrollBar);

        private void Host_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => PulseReveal(ScrollBar);

        private void Host_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsScrollKey(e.Key))
            {
                PulseReveal(ScrollBar);
            }
            else
            {
                RefreshReveal(ScrollBar);
            }
        }

        private void ScrollBar_MouseEnter(object sender, MouseEventArgs e) => RefreshReveal(ScrollBar);

        private void ScrollBar_MouseLeave(object sender, MouseEventArgs e) => RefreshReveal(ScrollBar);

        private void ScrollBar_PreviewMouseDown(object sender, MouseButtonEventArgs e) => PulseReveal(ScrollBar);

        private void ScrollBar_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshReveal(ScrollBar);

        private void ScrollBar_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshReveal(ScrollBar);

        private static bool IsScrollKey(Key key) =>
            key is Key.Up or Key.Down or Key.Left or Key.Right or
            Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space;
    }
}
