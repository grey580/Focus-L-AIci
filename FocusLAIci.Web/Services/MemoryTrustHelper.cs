using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

internal sealed record MemoryTrustSnapshot(
    MemoryVerificationStatus VerificationStatus,
    string VerificationStatusLabel,
    DateTime? LastVerifiedUtc,
    DateTime? ReviewAfterUtc,
    bool IsReviewDue,
    string FreshnessLabel,
    string FreshnessWarning,
    decimal RetrievalAdjustment,
    string RetrievalAdjustmentLabel);

internal static class MemoryTrustHelper
{
    public const int DefaultReviewWindowDays = 90;

    public static bool IsActive(MemoryEntry memory)
        => memory.LifecycleState == MemoryLifecycleState.Active;

    public static string GetLifecycleLabel(MemoryLifecycleState lifecycleState)
        => lifecycleState switch
        {
            MemoryLifecycleState.Archived => "Archived",
            MemoryLifecycleState.Superseded => "Superseded",
            _ => "Active"
        };

    public static MemoryTrustSnapshot Build(MemoryEntry memory, DateTime? utcNow = null)
        => Build(memory.VerificationStatus, memory.UpdatedUtc, memory.LastVerifiedUtc, memory.ReviewAfterUtc, utcNow);

    public static MemoryTrustSnapshot Build(
        MemoryVerificationStatus verificationStatus,
        DateTime updatedUtc,
        DateTime? lastVerifiedUtc,
        DateTime? reviewAfterUtc,
        DateTime? utcNow = null)
    {
        var now = (utcNow ?? DateTime.UtcNow).ToUniversalTime();
        var normalizedUpdatedUtc = NormalizeUtc(updatedUtc);
        var normalizedLastVerifiedUtc = lastVerifiedUtc.HasValue ? (DateTime?)NormalizeUtc(lastVerifiedUtc.Value) : null;
        var normalizedReviewAfterUtc = reviewAfterUtc.HasValue ? (DateTime?)NormalizeUtc(reviewAfterUtc.Value) : null;
        var age = now - normalizedUpdatedUtc;
        var isReviewDue = verificationStatus == MemoryVerificationStatus.NeedsReview
            || (normalizedReviewAfterUtc.HasValue && normalizedReviewAfterUtc.Value <= now);

        if (verificationStatus == MemoryVerificationStatus.Verified && !isReviewDue)
        {
            return new MemoryTrustSnapshot(
                verificationStatus,
                "Verified",
                normalizedLastVerifiedUtc,
                normalizedReviewAfterUtc,
                false,
                "Verified",
                string.Empty,
                1.5m,
                "Verified memory");
        }

        if (verificationStatus == MemoryVerificationStatus.NeedsReview)
        {
            return new MemoryTrustSnapshot(
                verificationStatus,
                "Needs review",
                normalizedLastVerifiedUtc,
                normalizedReviewAfterUtc,
                true,
                "Needs review",
                "Needs review",
                -6m,
                "Needs review");
        }

        if (normalizedReviewAfterUtc.HasValue && normalizedReviewAfterUtc.Value <= now)
        {
            return new MemoryTrustSnapshot(
                verificationStatus,
                "Verified",
                normalizedLastVerifiedUtc,
                normalizedReviewAfterUtc,
                true,
                "Review due",
                "Review due",
                -6m,
                "Review due");
        }

        if (verificationStatus == MemoryVerificationStatus.Unverified && age.TotalDays > 30)
        {
            return new MemoryTrustSnapshot(
                verificationStatus,
                "Unverified",
                normalizedLastVerifiedUtc,
                normalizedReviewAfterUtc,
                false,
                "Unverified",
                "Unverified",
                -3m,
                "Unverified memory");
        }

        return new MemoryTrustSnapshot(
            verificationStatus,
            verificationStatus == MemoryVerificationStatus.Verified ? "Verified" : "Unverified",
            normalizedLastVerifiedUtc,
            normalizedReviewAfterUtc,
            false,
            verificationStatus == MemoryVerificationStatus.Verified ? "Verified" : "Unverified",
            string.Empty,
            0m,
            string.Empty);
    }

    public static DateTime GetEffectiveTimestamp(DateTime updatedUtc, DateTime? lastVerifiedUtc)
    {
        var normalizedUpdatedUtc = NormalizeUtc(updatedUtc);
        var normalizedLastVerifiedUtc = lastVerifiedUtc.HasValue ? NormalizeUtc(lastVerifiedUtc.Value) : (DateTime?)null;
        return normalizedLastVerifiedUtc.HasValue && normalizedLastVerifiedUtc.Value > normalizedUpdatedUtc
            ? normalizedLastVerifiedUtc.Value
            : normalizedUpdatedUtc;
    }

    public static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
