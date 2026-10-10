namespace UsageViewer;

public sealed class LimitWindow
{
    public string Name { get; set; } = RateLimitDefaults.DefaultWindowName;
    public double? UsedPercent { get; set; }
    public long? WindowDurationMins { get; set; }
    public long? ResetsAt { get; set; }
}
