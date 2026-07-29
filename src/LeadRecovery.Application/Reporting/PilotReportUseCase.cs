namespace LeadRecovery.Application.Reporting;

public sealed class PilotReportUseCase(IPilotReportStore store, TimeProvider timeProvider)
{
    public Task<PilotReport> ExecuteAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        DateOnly effectiveTo = to ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DateOnly effectiveFrom = from ?? effectiveTo.AddDays(-29);
        if (effectiveFrom > effectiveTo)
        {
            throw new ArgumentException("The report start date cannot be after its end date.", nameof(from));
        }

        if (effectiveTo.DayNumber - effectiveFrom.DayNumber > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "The report range cannot exceed 366 days.");
        }

        DateTimeOffset fromUtc = new(effectiveFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset toUtcExclusive = new(effectiveTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return store.GenerateAsync(fromUtc, toUtcExclusive, cancellationToken);
    }
}
