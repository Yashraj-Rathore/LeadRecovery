using LeadRecovery.Application.Leads;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Tests;

public sealed class LeadQueryUseCaseTests
{
    [Fact]
    public async Task ListDelegatesValidatedPagingToQuery()
    {
        StubLeadInboxQuery query = new();
        ListLeadsUseCase useCase = new(query);

        LeadInboxPage page = await useCase.ExecuteAsync(
            25,
            "cursor",
            TestContext.Current.CancellationToken);

        Assert.Same(query.Page, page);
        Assert.Equal(25, query.PageSize);
        Assert.Equal("cursor", query.Cursor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListRejectsInvalidPageSize(int pageSize)
    {
        ListLeadsUseCase useCase = new(new StubLeadInboxQuery());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCase.ExecuteAsync(
                pageSize,
                null,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRejectsEmptyIdAndDelegatesValidId()
    {
        StubLeadInboxQuery query = new();
        GetLeadUseCase useCase = new(query);

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(
            Guid.Empty,
            TestContext.Current.CancellationToken));

        Guid leadId = Guid.CreateVersion7();
        _ = await useCase.ExecuteAsync(leadId, TestContext.Current.CancellationToken);
        Assert.Equal(leadId, query.LeadId);
    }

    private sealed class StubLeadInboxQuery : ILeadInboxQuery
    {
        public LeadInboxPage Page { get; } = new([], null);

        public int PageSize { get; private set; }

        public string? Cursor { get; private set; }

        public Guid LeadId { get; private set; }

        public Task<LeadInboxPage> ListAsync(
            int pageSize,
            string? cursor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageSize = pageSize;
            Cursor = cursor;
            return Task.FromResult(Page);
        }

        public Task<LeadInboxItem?> GetAsync(
            Guid leadId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LeadId = leadId;
            LeadInboxItem item = new(
                leadId,
                "Test Lead",
                "+14165550100",
                LeadSource.Manual,
                LeadStatus.New,
                LeadUrgency.Unknown,
                AutomationState.Active,
                DateTimeOffset.UnixEpoch);
            return Task.FromResult<LeadInboxItem?>(item);
        }
    }
}
