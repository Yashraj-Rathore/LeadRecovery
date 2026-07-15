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
        LeadInboxCriteria criteria = new(
            null,
            null,
            LeadAssignmentFilter.All,
            Guid.CreateVersion7());

        LeadInboxPage page = await useCase.ExecuteAsync(
            25,
            "cursor",
            criteria,
            TestContext.Current.CancellationToken);

        Assert.Same(query.Page, page);
        Assert.Equal(25, query.PageSize);
        Assert.Equal("cursor", query.Cursor);
        Assert.Same(criteria, query.Criteria);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListRejectsInvalidPageSize(int pageSize)
    {
        ListLeadsUseCase useCase = new(new StubLeadInboxQuery());
        LeadInboxCriteria criteria = new(
            null,
            null,
            LeadAssignmentFilter.All,
            Guid.CreateVersion7());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCase.ExecuteAsync(
                pageSize,
                null,
                criteria,
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

        public LeadInboxCriteria? Criteria { get; private set; }

        public Task<LeadInboxPage> ListAsync(
            int pageSize,
            string? cursor,
            LeadInboxCriteria criteria,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageSize = pageSize;
            Cursor = cursor;
            Criteria = criteria;
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
                null,
                null,
                DateTimeOffset.UnixEpoch,
                false,
                0,
                DateTimeOffset.UnixEpoch);
            return Task.FromResult<LeadInboxItem?>(item);
        }
    }
}
