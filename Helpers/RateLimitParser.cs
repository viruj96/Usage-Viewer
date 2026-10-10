using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace UsageViewer;

internal static class RateLimitParser
{
    private static readonly HashSet<string> MetadataFields =
    [
        RateLimitJson.LimitId, RateLimitJson.LimitName, RateLimitJson.PlanType,
        RateLimitJson.RateLimitReachedType, RateLimitJson.Credits, RateLimitJson.RateLimitResetCredits
    ];

    public static UsageSnapshot Parse(JsonElement result)
    {
        var root = result;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(RateLimitJson.RateLimits, out var nested))
            root = nested;

        var snapshot = new UsageSnapshot { FetchedAt = DateTimeOffset.UtcNow };
        if (root.ValueKind != JsonValueKind.Object)
            return snapshot;

        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty(RateLimitJson.RateLimitsByLimitId, out var bucketMap) && bucketMap.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in bucketMap.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Object)
                    snapshot.Buckets.Add(ParseBucket(entry.Name, entry.Value));
            }
        }

        if (snapshot.Buckets.Count == 0 && (HasWindow(root) || root.TryGetProperty(RateLimitJson.LimitId, out _)))
        {
            var id = ReadString(root, RateLimitJson.LimitId) ?? RateLimitJson.DefaultLimitId;
            snapshot.Buckets.Add(ParseBucket(id, root));
        }

        var resetCredits = result.ValueKind == JsonValueKind.Object && result.TryGetProperty(RateLimitJson.RateLimitResetCredits, out var rootCredits)
            ? rootCredits
            : result.ValueKind == JsonValueKind.Object && result.TryGetProperty(RateLimitJson.Credits, out var topLevelCredits)
                ? topLevelCredits
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty(RateLimitJson.Credits, out var nestedCredits)
                    ? nestedCredits
                    : default;
        if (resetCredits.ValueKind == JsonValueKind.Object && resetCredits.TryGetProperty(RateLimitJson.AvailableCount, out var available) && available.TryGetInt32(out var count))
        {
            var bucket = snapshot.Buckets.FirstOrDefault();
            if (bucket is not null)
                bucket.ResetCreditsAvailable = count;
        }

        return snapshot;
    }

    private static LimitBucket ParseBucket(string id, JsonElement value)
    {
        var bucket = new LimitBucket
        {
            Id = ReadString(value, RateLimitJson.LimitId) ?? id,
            Label = ReadString(value, RateLimitJson.LimitName),
            RateLimitReachedType = ReadString(value, RateLimitJson.RateLimitReachedType)
        };

        foreach (var property in value.EnumerateObject())
        {
            if (!MetadataFields.Contains(property.Name) && HasWindow(property.Value))
                bucket.Windows.Add(ParseWindow(property.Name, property.Value));
        }

        return bucket;
    }

    private static bool HasWindow(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        (value.TryGetProperty(RateLimitJson.UsedPercent, out _) || value.TryGetProperty(RateLimitJson.ResetsAt, out _) ||
         value.TryGetProperty(RateLimitJson.WindowDurationMinutes, out _) || value.TryGetProperty(RateLimitJson.WindowMinutes, out _));

    private static LimitWindow ParseWindow(string name, JsonElement value)
    {
        double? used = value.TryGetProperty(RateLimitJson.UsedPercent, out var usedElement) && usedElement.TryGetDouble(out var usage) ? usage : null;
        long? duration = TryLong(value, RateLimitJson.WindowDurationMinutes) ?? TryLong(value, RateLimitJson.WindowMinutes);
        return new LimitWindow { Name = name, UsedPercent = used, WindowDurationMins = duration, ResetsAt = TryLong(value, RateLimitJson.ResetsAt) };
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? TryLong(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
