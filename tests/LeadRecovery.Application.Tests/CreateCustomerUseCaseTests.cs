using LeadRecovery.Application.Customers;
using LeadRecovery.Application.PhoneNumbers;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Customers;

namespace LeadRecovery.Application.Tests;

public sealed class CreateCustomerUseCaseTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 13, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteCreatesCustomerForServerDerivedTenant()
    {
        Guid tenantId = Guid.CreateVersion7();
        InMemoryCustomerRepository repository = new();
        CreateCustomerUseCase useCase = new(
            new FixedTenantContext(tenantId),
            new FixedPhoneNumberNormalizer("+14165550123"),
            repository);
        CreateCustomerRequest request = new(
            "(416) 555-0123",
            "CA",
            CreatedAtUtc,
            Name: "Alex Customer");

        CreateCustomerResult result = await useCase.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Created);
        Assert.NotNull(result.Customer);
        Assert.Equal(tenantId, result.Customer.TenantId);
        Assert.Equal("+14165550123", result.Customer.PhoneE164);
        Assert.Equal(1, repository.AddCount);
    }

    [Fact]
    public async Task ExecuteReturnsExistingCanonicalCustomerWithoutDuplicate()
    {
        Guid tenantId = Guid.CreateVersion7();
        Customer existing = new(
            Guid.CreateVersion7(),
            tenantId,
            "+14165550123",
            CreatedAtUtc);
        InMemoryCustomerRepository repository = new(existing);
        CreateCustomerUseCase useCase = new(
            new FixedTenantContext(tenantId),
            new FixedPhoneNumberNormalizer(existing.PhoneE164),
            repository);

        CreateCustomerResult result = await useCase.ExecuteAsync(
            new CreateCustomerRequest("416-555-0123", "CA", CreatedAtUtc),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Created);
        Assert.Same(existing, result.Customer);
        Assert.Equal(0, repository.AddCount);
    }

    [Fact]
    public async Task ExecuteReturnsExplicitPhoneFailureWithoutPersistence()
    {
        InMemoryCustomerRepository repository = new();
        CreateCustomerUseCase useCase = new(
            new FixedTenantContext(Guid.CreateVersion7()),
            new FixedPhoneNumberNormalizer(PhoneNumberNormalizationFailure.Invalid),
            repository);

        CreateCustomerResult result = await useCase.ExecuteAsync(
            new CreateCustomerRequest("invalid", "CA", CreatedAtUtc),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.False(result.Created);
        Assert.Null(result.Customer);
        Assert.Equal(PhoneNumberNormalizationFailure.Invalid, result.PhoneFailure);
        Assert.Equal(0, repository.FindCount);
        Assert.Equal(0, repository.AddCount);
    }

    [Fact]
    public async Task ExecuteHonorsPreCancelledToken()
    {
        InMemoryCustomerRepository repository = new();
        CreateCustomerUseCase useCase = new(
            new FixedTenantContext(Guid.CreateVersion7()),
            new FixedPhoneNumberNormalizer("+14165550123"),
            repository);
        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(
                new CreateCustomerRequest("416-555-0123", "CA", CreatedAtUtc),
                cancellationSource.Token));

        Assert.Equal(0, repository.FindCount);
        Assert.Equal(0, repository.AddCount);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FixedPhoneNumberNormalizer : IPhoneNumberNormalizer
    {
        private readonly PhoneNumberNormalizationResult _result;

        public FixedPhoneNumberNormalizer(string phoneE164)
        {
            _result = PhoneNumberNormalizationResult.Success(phoneE164);
        }

        public FixedPhoneNumberNormalizer(PhoneNumberNormalizationFailure failure)
        {
            _result = PhoneNumberNormalizationResult.Failed(failure);
        }

        public PhoneNumberNormalizationResult Normalize(
            string? phoneNumber,
            string? defaultRegion) =>
            _result;
    }

    private sealed class InMemoryCustomerRepository(params Customer[] customers)
        : ICustomerRepository
    {
        private readonly List<Customer> _customers = [.. customers];

        public int FindCount { get; private set; }

        public int AddCount { get; private set; }

        public Task<Customer?> FindByPhoneAsync(
            string phoneE164,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCount++;
            return Task.FromResult(
                _customers.SingleOrDefault(customer => customer.PhoneE164 == phoneE164));
        }

        public Task AddAsync(Customer customer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _customers.Add(customer);
            AddCount++;
            return Task.CompletedTask;
        }
    }
}
