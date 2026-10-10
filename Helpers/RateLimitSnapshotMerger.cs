using System.Linq;
using System.Text.Json;

namespace UsageViewer;

internal static class RateLimitSnapshotMerger
{
    public static bool TryApply(UsageSnapshot? current, JsonElement parameters, out UsageSnapshot snapshot)
    {
        var rateLimits = parameters;
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(RateLimitJson.RateLimits, out var nested))
            rateLimits = nested;

        var update = RateLimitParser.Parse(rateLimits);
        if (rateLimits.ValueKind != JsonValueKind.Object || update.Buckets.Count == 0)
        {
            snapshot = current ?? new UsageSnapshot();
            return false;
        }

        snapshot = current is null ? new UsageSnapshot() : CopySnapshot(current);
        foreach (var incoming in update.Buckets)
        {
            var source = FindBucketPayload(rateLimits, incoming.Id);
            var existing = snapshot.Buckets.FirstOrDefault(bucket => bucket.Id == incoming.Id);
            if (existing is null)
            {
                snapshot.Buckets.Add(incoming);
                continue;
            }

            if (source.ValueKind != JsonValueKind.Object)
            {
                CopyBucket(incoming, existing);
                continue;
            }

            if (source.TryGetProperty(RateLimitJson.LimitName, out _)) existing.Label = incoming.Label;
            if (source.TryGetProperty(RateLimitJson.PlanType, out var planType)) existing.PlanType = planType.ValueKind == JsonValueKind.String ? planType.GetString() : null;
            if (source.TryGetProperty(RateLimitJson.RateLimitReachedType, out _)) existing.RateLimitReachedType = incoming.RateLimitReachedType;

            foreach (var property in source.EnumerateObject())
            {
                if (property.Name is RateLimitJson.Credits or RateLimitJson.RateLimitResetCredits)
                {
                    var credits = property.Value;
                    if (credits.ValueKind == JsonValueKind.Object && credits.TryGetProperty(RateLimitJson.AvailableCount, out var count) && count.TryGetInt32(out var available))
                        existing.ResetCreditsAvailable = available;
                    else if (credits.ValueKind == JsonValueKind.Null)
                        existing.ResetCreditsAvailable = null;
                    continue;
                }

                var incomingWindow = incoming.Windows.FirstOrDefault(window => window.Name == property.Name);
                if (incomingWindow is null)
                    continue;
                var existingWindow = existing.Windows.FirstOrDefault(window => window.Name == property.Name);
                if (existingWindow is null)
                {
                    existing.Windows.Add(incomingWindow);
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (property.Value.TryGetProperty(RateLimitJson.UsedPercent, out _)) existingWindow.UsedPercent = incomingWindow.UsedPercent;
                if (property.Value.TryGetProperty(RateLimitJson.WindowDurationMinutes, out _) || property.Value.TryGetProperty(RateLimitJson.WindowMinutes, out _))
                    existingWindow.WindowDurationMins = incomingWindow.WindowDurationMins;
                if (property.Value.TryGetProperty(RateLimitJson.ResetsAt, out _)) existingWindow.ResetsAt = incomingWindow.ResetsAt;
            }
        }

        snapshot.FetchedAt = System.DateTimeOffset.UtcNow;
        return true;
    }

    private static JsonElement FindBucketPayload(JsonElement rateLimits, string bucketId)
    {
        if (rateLimits.TryGetProperty(RateLimitJson.RateLimitsByLimitId, out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object && buckets.TryGetProperty(bucketId, out var bucket))
            return bucket;
        return rateLimits;
    }

    private static void CopyBucket(LimitBucket source, LimitBucket target)
    {
        target.Label = source.Label;
        target.PlanType = source.PlanType;
        target.RateLimitReachedType = source.RateLimitReachedType;
        target.ResetCreditsAvailable = source.ResetCreditsAvailable;
        target.Windows = source.Windows;
    }

    private static UsageSnapshot CopySnapshot(UsageSnapshot source) => new()
    {
        FetchedAt = source.FetchedAt,
        Buckets = source.Buckets.Select(bucket => new LimitBucket
        {
            Id = bucket.Id,
            Label = bucket.Label,
            PlanType = bucket.PlanType,
            RateLimitReachedType = bucket.RateLimitReachedType,
            ResetCreditsAvailable = bucket.ResetCreditsAvailable,
            Windows = bucket.Windows.Select(window => new LimitWindow
            {
                Name = window.Name,
                UsedPercent = window.UsedPercent,
                WindowDurationMins = window.WindowDurationMins,
                ResetsAt = window.ResetsAt
            }).ToList()
        }).ToList()
    };
}
