using LeadRecovery.Application.Reporting;

namespace LeadRecovery.Application.Tests;

public sealed class PilotReportUseCaseTests
{
    [Fact]
    public async Task DefaultsToThirtyInclusiveUtcDays()
    {
        RecordingStore store = new();
        PilotReportUseCase useCase = new(
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 29, 15, 0, 0, TimeSpan.Zero)));

        _ = await useCase.ExecuteAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Equal(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), store.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero), store.ToUtcExclusive);
    }

    [Fact]
    public async Task RejectsReversedAndOverlongRanges()
    {
        PilotReportUseCase useCase = new(new RecordingStore(), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 1),
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => useCase.ExecuteAsync(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 7, 1),
            TestContext.Current.CancellationToken));
    }

    private sealed class RecordingStore : IPilotReportStore
    {
        public DateTimeOffset FromUtc { get; private set; }
        public DateTimeOffset ToUtcExclusive { get; private set; }

        public Task<PilotReport> GenerateAsync(DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken cancellationToken)
        {
            FromUtc = fromUtc;
            ToUtcExclusive = toUtcExclusive;
            return Task.FromResult(new PilotReport(
                fromUtc, toUtcExclusive, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, null, "Operational only."));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
