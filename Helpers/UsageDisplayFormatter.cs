using System;
using System.Globalization;

namespace UsageViewer;

internal static class UsageDisplayFormatter
{
    public const string PercentFormat = "0.#";
    public const int FiveHourWindowMinutes = 300;
    public const int WeeklyWindowMinutes = 10080;
    private const string FiveHourWindowLabel = "5h limit:";
    private const string WeeklyWindowLabel = "7d limit:";
    private const string ZeroDurationLabel = "0m";
    private const string DayUnit = "d";
    private const string HourUnit = "h";
    private const string MinuteUnit = "m";
    private const string ResetTimeFormat = "HH:mm";
    private const string WeeklyResetDateTimeFormat = "HH:mm, d MMM";
    private const string ResetDateTimeFormat = "d MMM yyyy, HH:mm";
    private const int MinimumDisplayedMinutes = 1;
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    public static string GetWindowLabel(LimitWindow window)
    {
        if (window.WindowDurationMins is long minutes)
        {
            if (minutes == FiveHourWindowMinutes) return FiveHourWindowLabel;
            if (minutes == WeeklyWindowMinutes) return WeeklyWindowLabel;
            return FormatWindowDuration(minutes);
        }
        return ToTitle(window.Name);
    }

    public static string FormatWindowDuration(long minutes)
    {
        var duration = TimeSpan.FromMinutes(minutes);
        if (duration >= OneDay)
            return $"{(int)duration.TotalDays}{DayUnit}{(duration.Hours > 0 ? $" {duration.Hours}{HourUnit}" : string.Empty)}";
        if (duration >= OneHour)
            return duration.Minutes > 0
                ? $"{(int)duration.TotalHours}{HourUnit} {duration.Minutes}{MinuteUnit}"
                : $"{(int)duration.TotalHours}{HourUnit}";
        return $"{Math.Max(MinimumDisplayedMinutes, (int)duration.TotalMinutes)}{MinuteUnit}";
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            return ZeroDurationLabel;
        if (duration >= OneDay)
            return $"{(int)duration.TotalDays}{DayUnit} {duration.Hours}{HourUnit}";
        if (duration >= OneHour)
            return $"{(int)duration.TotalHours}{HourUnit} {duration.Minutes}{MinuteUnit}";
        return $"{Math.Max(MinimumDisplayedMinutes, duration.Minutes)}{MinuteUnit}";
    }

    public static string FormatResetDateTime(long unixTimeSeconds, long? windowDurationMinutes)
    {
        var format = windowDurationMinutes switch
        {
            FiveHourWindowMinutes => ResetTimeFormat,
            WeeklyWindowMinutes => WeeklyResetDateTimeFormat,
            _ => ResetDateTimeFormat
        };

        return DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds)
            .ToLocalTime()
            .ToString(format, CultureInfo.CurrentCulture);
    }

    public static string ToTitle(string value) =>
        string.IsNullOrWhiteSpace(value) ? ViewerText.UsageWindowTitle : char.ToUpperInvariant(value[0]) + value[1..];
}
