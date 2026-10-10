using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UsageViewer;

internal sealed class UsageSnapshotRenderer
{
    private const int SingleBucketCount = 1;
    private const double MinimumUsagePercent = 0;
    private const double MaximumUsagePercent = 100;

    private readonly FrameworkElement _resourceOwner;
    private readonly Panel _bucketPanel;
    private readonly TextBlock _updatedText;
    private readonly List<(TextBlock Text, long ResetAt, long? WindowDurationMinutes)> _resetCountdowns = new();

    public UsageSnapshotRenderer(FrameworkElement resourceOwner, Panel bucketPanel, TextBlock updatedText)
    {
        _resourceOwner = resourceOwner;
        _bucketPanel = bucketPanel;
        _updatedText = updatedText;
    }

    public void Render(UsageSnapshot? snapshot)
    {
        _bucketPanel.Children.Clear();
        _resetCountdowns.Clear();

        if (snapshot is null || snapshot.Buckets.Count == 0)
        {
            ShowEmptyState();
            if (snapshot is not null)
                _updatedText.Text = $"{ViewerText.LastCheckedPrefix}{snapshot.FetchedAt.ToLocalTime():t}";
            return;
        }

        var singleBucket = snapshot.Buckets.Count == SingleBucketCount;
        foreach (var bucket in snapshot.Buckets)
        {
            var content = new StackPanel();
            if (singleBucket)
            {
                content.Margin = new Thickness(0, 1, 0, 0);
            }
            else
            {
                var card = new Border
                {
                    Background = PaletteBrush(ViewerBrushKeys.SurfaceBrush),
                    BorderBrush = PaletteBrush(ViewerBrushKeys.BorderBrush),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 5),
                    Child = content
                };
                content.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(bucket.Label) ? bucket.Id : bucket.Label,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = PaletteBrush(ViewerBrushKeys.PrimaryTextBrush)
                });
                _bucketPanel.Children.Add(card);
            }

            foreach (var window in bucket.Windows)
                content.Children.Add(CreateWindowView(window));

            if (bucket.Windows.Count == 0)
                content.Children.Add(new TextBlock
                {
                    Text = ViewerText.NoUsageWindowsReturned,
                    Margin = new Thickness(0, 9, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = PaletteBrush(ViewerBrushKeys.SecondaryTextBrush)
                });
            var bucketDetails = new List<string>();
            if (bucket.ResetCreditsAvailable is int credits)
                bucketDetails.Add($"{ViewerText.ResetCreditsPrefix}{credits}");
            if (!string.IsNullOrWhiteSpace(bucket.RateLimitReachedType))
                bucketDetails.Add($"{ViewerText.LimitPrefix}{bucket.RateLimitReachedType}");
            if (bucketDetails.Count > 0)
                content.Children.Add(new TextBlock
                {
                    Text = string.Join(ViewerText.BucketDetailSeparator, bucketDetails),
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = PaletteBrush(ViewerBrushKeys.WarningBrush),
                    TextWrapping = TextWrapping.Wrap
                });
            if (singleBucket)
                _bucketPanel.Children.Add(content);
        }

        _updatedText.Text = $"{ViewerText.LastUpdatedPrefix}{snapshot.FetchedAt.ToLocalTime():t}";
    }

    public void UpdateResetCountdowns()
    {
        foreach (var (text, reset, windowDurationMinutes) in _resetCountdowns)
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(reset) - DateTimeOffset.UtcNow;
            text.Text = remaining > TimeSpan.Zero
                ? ViewerText.ResetCountdown(
                    UsageDisplayFormatter.FormatDuration(remaining),
                    UsageDisplayFormatter.FormatResetDateTime(reset, windowDurationMinutes))
                : ViewerText.ResetDueRefreshing;
        }
    }

    private void ShowEmptyState()
    {
        _bucketPanel.Children.Add(new StackPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = ViewerText.NoUsageData,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = PaletteBrush(ViewerBrushKeys.PrimaryTextBrush)
                },
                new TextBlock
                {
                    Text = ViewerText.UsageLimitsWillAppear,
                    Margin = new Thickness(0, 2, 0, 0),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = PaletteBrush(ViewerBrushKeys.SecondaryTextBrush)
                }
            }
        });
    }

    private UIElement CreateWindowView(LimitWindow window)
    {
        var section = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        var heading = new DockPanel();
        var windowLabel = UsageDisplayFormatter.GetWindowLabel(window);
        heading.Children.Add(new TextBlock
        {
            Text = windowLabel,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = PaletteBrush(ViewerBrushKeys.PrimaryTextBrush),
            TextWrapping = TextWrapping.Wrap
        });
        var usedText = window.UsedPercent is double used
            ? $"{used.ToString(UsageDisplayFormatter.PercentFormat, CultureInfo.InvariantCulture)}{ViewerText.PercentUsedSuffix}"
            : ViewerText.UsageUnavailable;
        var value = new TextBlock
        {
            Text = usedText,
            FontSize = 10,
            Foreground = PaletteBrush(ViewerBrushKeys.SecondaryTextBrush),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(value, Dock.Right);
        heading.Children.Add(value);
        section.Children.Add(heading);

        if (window.UsedPercent is double percent)
        {
            var progress = new ProgressBar
            {
                Minimum = MinimumUsagePercent,
                Maximum = MaximumUsagePercent,
                Value = Math.Clamp(percent, MinimumUsagePercent, MaximumUsagePercent),
                Height = 5,
                Margin = new Thickness(0, 2, 0, 0),
                BorderThickness = new Thickness(0),
                Foreground = PaletteBrush(window.WindowDurationMins == UsageDisplayFormatter.WeeklyWindowMinutes
                    ? ViewerBrushKeys.SecondaryUsageBarBrush : ViewerBrushKeys.PrimaryUsageBarBrush),
                Background = PaletteBrush(ViewerBrushKeys.BorderBrush)
            };
            System.Windows.Automation.AutomationProperties.SetName(progress, ViewerText.UsageAvailableForWindow(usedText, windowLabel));
            section.Children.Add(progress);
        }
        else
        {
            var unavailableTrack = new Border
            {
                Height = 5,
                Margin = new Thickness(0, 2, 0, 0),
                CornerRadius = new CornerRadius(3),
                Background = PaletteBrush(ViewerBrushKeys.BorderBrush)
            };
            System.Windows.Automation.AutomationProperties.SetName(unavailableTrack, ViewerText.UsageUnavailableForWindow(windowLabel));
            section.Children.Add(unavailableTrack);
        }

        if (window.ResetsAt is long reset)
        {
            var countdown = new TextBlock
            {
                FontSize = 9,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = PaletteBrush(ViewerBrushKeys.SecondaryTextBrush)
            };
            section.Children.Add(countdown);
            _resetCountdowns.Add((countdown, reset, window.WindowDurationMins));
        }
        else
            section.Children.Add(new TextBlock
            {
                Text = ViewerText.ResetTimeUnavailable,
                FontSize = 9,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = PaletteBrush(ViewerBrushKeys.SecondaryTextBrush)
            });
        return section;
    }

    private SolidColorBrush PaletteBrush(string key) =>
        (SolidColorBrush)_resourceOwner.FindResource(key);
}
