using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UsageViewer;

public partial class MainWindow : Window
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DisplayTickInterval = TimeSpan.FromSeconds(1);
    private const double ScreenEdgeOffset = 16;
    private readonly DispatcherTimer _pollTimer = new() { Interval = PollInterval };
    private readonly DispatcherTimer _displayTimer = new() { Interval = DisplayTickInterval };
    private readonly TrayIconService _tray;
    private readonly UsageViewerController _controller;
    private readonly RotateTransform _refreshGlyphRotation;
    private readonly object _pendingViewStateLock = new();
    private UsageViewerViewState? _pendingViewState;
    private bool _viewStateUpdateQueued;
    private UsageSnapshotRenderer _snapshotRenderer = null!;
    private long _renderedSnapshotVersion = -1;
    private bool _refreshSpinnerRunning;
    private bool _isExiting;
    private bool _initialPlacementApplied;

    public MainWindow(UsageViewerController controller)
    {
        _controller = controller;
        InitializeComponent();
        _refreshGlyphRotation = (RotateTransform)RefreshGlyph.RenderTransform;
        _snapshotRenderer = new UsageSnapshotRenderer(this, BucketPanel, UpdatedText);
        Topmost = true;
        _tray = new TrayIconService();
        _tray.ShowRequested += ShowFromTray;
        _tray.HideRequested += HideToTray;
        _tray.RefreshRequested += () => _ = _controller.RefreshAsync();
        _tray.ExitRequested += ExitApplication;
        _pollTimer.Tick += (_, _) => _ = _controller.RefreshAsync();
        _displayTimer.Tick += (_, _) => UpdateFreshnessDisplay();
        Closing += OnClosing;
        Loaded += OnLoaded;
        _controller.ViewStateChanged += OnViewStateChanged;
        ApplyViewState(_controller.ViewState);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_initialPlacementApplied)
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Right - Width - ScreenEdgeOffset);
            Top = Math.Max(workArea.Top, workArea.Bottom - Height - ScreenEdgeOffset);
            _initialPlacementApplied = true;
        }

    }

    private void OnViewStateChanged(UsageViewerViewState state)
    {
        lock (_pendingViewStateLock)
        {
            _pendingViewState = state;
            if (_viewStateUpdateQueued)
                return;
            _viewStateUpdateQueued = true;
        }

        Dispatcher.BeginInvoke(new Action(ApplyPendingViewState));
    }

    private void ApplyPendingViewState()
    {
        UsageViewerViewState? state;
        lock (_pendingViewStateLock)
        {
            state = _pendingViewState;
            _pendingViewState = null;
            _viewStateUpdateQueued = false;
        }

        if (state is not null)
            ApplyViewState(state);
    }

    private void ApplyViewState(UsageViewerViewState state)
    {
        RefreshButton.IsEnabled = state.RefreshEnabled;
        var isRefreshing = state.Status.Kind == ViewerStatusKind.Refreshing;
        UpdateRefreshSpinner(isRefreshing);
        SignInButton.Visibility = state.SignInVisible ? Visibility.Visible : Visibility.Collapsed;
        SignInButton.IsEnabled = state.SignInEnabled;
        if (_renderedSnapshotVersion != state.SnapshotVersion)
        {
            _renderedSnapshotVersion = state.SnapshotVersion;
            _snapshotRenderer.Render(state.Snapshot);
            if (state.Snapshot is { Buckets.Count: > 0 })
                UpdateFreshnessDisplay();
        }
        SetStatus(state.Status.Text, state.Status.IsVisible && !isRefreshing);
    }

    private void UpdateRefreshSpinner(bool isRefreshing)
    {
        if (_refreshSpinnerRunning == isRefreshing)
            return;

        _refreshSpinnerRunning = isRefreshing;
        var animation = isRefreshing
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever }
            : null;
        _refreshGlyphRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void SignInButton_Click(object sender, RoutedEventArgs e) => _ = _controller.SignInAsync();

    private void UpdateFreshnessDisplay()
    {
        _controller.UpdateFreshness();
        _snapshotRenderer.UpdateResetCountdowns();
    }

    private void SetStatus(string value, bool isVisible)
    {
        StatusText.Text = value;
        StatusText.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        StatusText.ToolTip = value;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = _controller.RefreshAsync();

    private void HideButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || IsSourceInsideButton(e.OriginalSource as DependencyObject))
            return;
        DragMove();
        e.Handled = true;
    }

    private static bool IsSourceInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private async void ShowFromTray()
    {
        Topmost = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        try
        {
            await _controller.StartAsync();
        }
        catch (Exception)
        {
            // Controller reports operational failures through its view state.
        }
        _pollTimer.Start();
        _displayTimer.Start();
        UpdateFreshnessDisplay();
    }

    private async void HideToTray()
    {
        _pollTimer.Stop();
        _displayTimer.Stop();
        Hide();
        await _controller.DisconnectAsync();
        ProcessMemoryTrimmer.TrimCurrentProcessWorkingSet();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _tray.Dispose();
        _controller.ViewStateChanged -= OnViewStateChanged;
    }

}
