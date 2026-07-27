using LeadRecovery.Domain.Automations;

namespace LeadRecovery.Application.Automations;

public interface IBusinessHoursScheduler
{
    DateTimeOffset GetNextPermittedUtc(
        DateTimeOffset candidateUtc,
        string timezoneId,
        BusinessHoursPolicy policy);

    DateTimeOffset GetUrgentHumanReviewUtc(
        DateTimeOffset candidateUtc,
        string timezoneId,
        BusinessHoursPolicy policy);
}

public sealed class BusinessHoursScheduler : IBusinessHoursScheduler
{
    public DateTimeOffset GetNextPermittedUtc(
        DateTimeOffset candidateUtc,
        string timezoneId,
        BusinessHoursPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (candidateUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The candidate timestamp must be UTC.", nameof(candidateUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timezoneId);
        TimeZoneInfo timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId.Trim());
        DateTime localCandidate = TimeZoneInfo.ConvertTime(candidateUtc, timezone).DateTime;
        Dictionary<DayOfWeek, BusinessDayHours> windows = policy.Windows
            .ToDictionary(window => window.Day);

        for (int offset = 0; offset <= 14; offset++)
        {
            DateOnly date = DateOnly.FromDateTime(localCandidate).AddDays(offset);
            if (!windows.TryGetValue(date.DayOfWeek, out BusinessDayHours? window))
            {
                continue;
            }

            DateTime open = date.ToDateTime(window.OpensAt, DateTimeKind.Unspecified);
            DateTime close = date.ToDateTime(window.ClosesAt, DateTimeKind.Unspecified);
            if (offset == 0 && localCandidate >= open && localCandidate < close)
            {
                return candidateUtc;
            }

            if (offset > 0 || localCandidate < open)
            {
                return ConvertLocalToUtc(open, timezone);
            }
        }

        throw new InvalidOperationException(
            "No permitted business-hours window exists within the next fourteen days.");
    }

    public DateTimeOffset GetUrgentHumanReviewUtc(
        DateTimeOffset candidateUtc,
        string timezoneId,
        BusinessHoursPolicy policy) =>
        policy.UrgentHumanReviewAfterHours
            ? candidateUtc
            : GetNextPermittedUtc(candidateUtc, timezoneId, policy);

    private static DateTimeOffset ConvertLocalToUtc(
        DateTime local,
        TimeZoneInfo timezone)
    {
        DateTime candidate = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (timezone.IsInvalidTime(candidate))
        {
            candidate = candidate.AddMinutes(1);
        }

        if (timezone.IsAmbiguousTime(candidate))
        {
            TimeSpan offset = timezone.GetAmbiguousTimeOffsets(candidate).Max();
            return new DateTimeOffset(candidate, offset).ToUniversalTime();
        }

        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(candidate, timezone),
            TimeSpan.Zero);
    }
}
