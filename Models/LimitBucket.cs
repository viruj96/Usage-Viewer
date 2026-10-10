using System.Collections.Generic;

namespace UsageViewer;

public sealed class LimitBucket
{
    public string Id { get; set; } = RateLimitDefaults.DefaultBucketId;
    public string? Label { get; set; }
    public string? PlanType { get; set; }
    public string? RateLimitReachedType { get; set; }
    public List<LimitWindow> Windows { get; set; } = [];
    public int? ResetCreditsAvailable { get; set; }
}
