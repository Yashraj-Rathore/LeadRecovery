using LeadRecovery.Application.Automations;
using LeadRecovery.Domain.Automations;

namespace LeadRecovery.Application.Tests;

public sealed class BusinessHoursSchedulerTests
{
    private readonly BusinessHoursScheduler scheduler = new();
    private readonly BusinessHoursPolicy policy = new(
        Enumerable.Range(1, 5)
            .Select(day => new BusinessDayHours(
                (DayOfWeek)day,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0)))
            .ToArray(),
        true);

    [Fact]
    public void CandidateInsideTenantBusinessHoursIsUnchanged()
    {
        DateTimeOffset candidateUtc = new(2026, 7, 21, 14, 0, 0, TimeSpan.Zero);

        DateTimeOffset actual = scheduler.GetNextPermittedUtc(
            candidateUtc,
            "America/Toronto",
            policy);

        Assert.Equal(candidateUtc, actual);
    }

    [Fact]
    public void AfterHoursCrossingSpringDstMovesToNextWindow()
    {
        DateTimeOffset fridayAfterHoursUtc =
            new(2026, 3, 6, 23, 0, 0, TimeSpan.Zero);

        DateTimeOffset actual = scheduler.GetNextPermittedUtc(
            fridayAfterHoursUtc,
            "America/Toronto",
            policy);

        Assert.Equal(new DateTimeOffset(2026, 3, 9, 13, 0, 0, TimeSpan.Zero), actual);
    }

    [Fact]
    public void AfterHoursCrossingFallDstMovesToNextWindow()
    {
        DateTimeOffset fridayAfterHoursUtc =
            new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);

        DateTimeOffset actual = scheduler.GetNextPermittedUtc(
            fridayAfterHoursUtc,
            "America/Toronto",
            policy);

        Assert.Equal(new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero), actual);
    }

    [Fact]
    public void UrgentHumanReviewCanBypassAfterHoursPolicy()
    {
        DateTimeOffset afterHoursUtc = new(2026, 7, 21, 23, 0, 0, TimeSpan.Zero);

        DateTimeOffset urgent = scheduler.GetUrgentHumanReviewUtc(
            afterHoursUtc,
            "America/Toronto",
            policy);
        DateTimeOffset ordinary = scheduler.GetUrgentHumanReviewUtc(
            afterHoursUtc,
            "America/Toronto",
            policy with { UrgentHumanReviewAfterHours = false });

        Assert.Equal(afterHoursUtc, urgent);
        Assert.Equal(new DateTimeOffset(2026, 7, 22, 13, 0, 0, TimeSpan.Zero), ordinary);
    }

    [Fact]
    public void InvalidSpringForwardOpeningAdvancesToFirstValidMinute()
    {
        BusinessHoursPolicy springPolicy = new(
            [
                new BusinessDayHours(
                    DayOfWeek.Sunday,
                    new TimeOnly(2, 30),
                    new TimeOnly(4, 0)),
            ],
            false);

        DateTimeOffset actual = scheduler.GetNextPermittedUtc(
            new DateTimeOffset(2026, 3, 7, 20, 0, 0, TimeSpan.Zero),
            "America/Toronto",
            springPolicy);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), actual);
    }

    [Fact]
    public void AmbiguousFallBackOpeningUsesEarliestUtcOccurrence()
    {
        BusinessHoursPolicy fallPolicy = new(
            [
                new BusinessDayHours(
                    DayOfWeek.Sunday,
                    new TimeOnly(1, 30),
                    new TimeOnly(2, 30)),
            ],
            false);

        DateTimeOffset actual = scheduler.GetNextPermittedUtc(
            new DateTimeOffset(2026, 10, 31, 20, 0, 0, TimeSpan.Zero),
            "America/Toronto",
            fallPolicy);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), actual);
    }
}
