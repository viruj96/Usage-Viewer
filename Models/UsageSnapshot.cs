using System;
using System.Collections.Generic;

namespace UsageViewer;

public sealed class UsageSnapshot
{
    public DateTimeOffset FetchedAt { get; set; }
    public List<LimitBucket> Buckets { get; set; } = [];
}
