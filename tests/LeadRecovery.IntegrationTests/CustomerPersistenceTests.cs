using System.Security.Claims;

using LeadRecovery.Application.Customers;
using LeadRecovery.Application.PhoneNumbers;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class CustomerPersistenceTests(LeadRecoveryApiFixture fixture)
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 13, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NormalizerCanonicalizesEquivalentCanadianFormats()
    {
        using IServiceScope scope = fixture.Application.Services.CreateScope();
        IPhoneNumberNormalizer normalizer =
            scope.ServiceProvider.GetRequiredService<IPhoneNumberNormalizer>();

        PhoneNumberNormalizationResult international =
            normalizer.Normalize("+1 416 555 0123", null);
        PhoneNumberNormalizationResult national =
            normalizer.Normalize("(416) 555-0123", "CA");
        PhoneNumberNormalizationResult punctuated =
            normalizer.Normalize("416.555.0123", "ca");

        Assert.True(international.IsSuccess);
        Assert.True(national.IsSuccess);
        Assert.True(punctuated.IsSuccess);
        Assert.Equal("+14165550123", international.PhoneE164);
        Assert.Equal(international.PhoneE164, national.PhoneE164);
        Assert.Equal(international.PhoneE164, punctuated.PhoneE164);
    }

    [Theory]
    [InlineData(null, "CA", PhoneNumberNormalizationFailure.MissingInput)]
    [InlineData("4165550123", null, PhoneNumberNormalizationFailure.MissingDefaultRegion)]
    [InlineData("4165550123", "ZZ", PhoneNumberNormalizationFailure.UnsupportedRegion)]
    [InlineData("not-a-number", "CA", PhoneNumberNormalizationFailure.ParseFailed)]
    [InlineData("+12005550123", null, PhoneNumberNormalizationFailure.Invalid)]
    public void NormalizerReturnsExplicitFailure(
        string? phoneNumber,
        string? defaultRegion,
        PhoneNumberNormalizationFailure expectedFailure)
    {
        using IServiceScope scope = fixture.Application.Services.CreateScope();
        IPhoneNumberNormalizer normalizer =
            scope.ServiceProvider.GetRequiredService<IPhoneNumberNormalizer>();

        PhoneNumberNormalizationResult result =
            normalizer.Normalize(phoneNumber, defaultRegion);

        Assert.False(result.IsSuccess);
        Assert.Null(result.PhoneE164);
        Assert.Equal(expectedFailure, result.Failure);
    }

    [Fact]
    public async Task EquivalentFormatsReturnOnePersistedCustomer()
    {
        Guid tenantId = Guid.CreateVersion7();
        await PersistTenant(tenantId);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        CreateCustomerUseCase useCase =
            scope.ServiceProvider.GetRequiredService<CreateCustomerUseCase>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        CreateCustomerResult first = await useCase.ExecuteAsync(
            new CreateCustomerRequest("(416) 555-0123", "CA", CreatedAtUtc),
            cancellationToken);
        CreateCustomerResult second = await useCase.ExecuteAsync(
            new CreateCustomerRequest("+1 416 555 0123", null, CreatedAtUtc),
            cancellationToken);

        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.NotNull(first.Customer);
        Assert.Same(first.Customer, second.Customer);
        Assert.Equal(1, await dbContext.Customers.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task DatabaseUniquenessIsScopedByTenant()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        await PersistTenant(firstTenantId);
        await PersistTenant(secondTenantId);
        const string phoneE164 = "+14165550124";

        await PersistCustomer(firstTenantId, phoneE164);
        await PersistCustomer(secondTenantId, phoneE164);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, firstTenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Customers.Add(new Customer(
            Guid.CreateVersion7(),
            firstTenantId,
            phoneE164,
            CreatedAtUtc));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CustomerQueriesAreFilteredToActiveTenant()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        await PersistTenant(firstTenantId);
        await PersistTenant(secondTenantId);
        await PersistCustomer(firstTenantId, "+14165550125");
        await PersistCustomer(secondTenantId, "+14165550126");

        Assert.Equal(
            firstTenantId,
            await ReadOnlyVisibleCustomerTenant(firstTenantId));
        Assert.Equal(
            secondTenantId,
            await ReadOnlyVisibleCustomerTenant(secondTenantId));
    }

    [Fact]
    public async Task CrossTenantCustomerWriteFailsClosed()
    {
        Guid activeTenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();
        await PersistTenant(activeTenantId);
        await PersistTenant(otherTenantId);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, activeTenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Customers.Add(new Customer(
            Guid.CreateVersion7(),
            otherTenantId,
            "+14165550127",
            CreatedAtUtc));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingTenantContextCannotQueryCustomers()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        IHttpContextAccessor accessor =
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = null;
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();

        await Assert.ThrowsAsync<TenantContextUnavailableException>(
            () => dbContext.Customers.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task PersistTenant(Guid tenantId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(new Tenant(
            tenantId,
            $"Tenant {tenantId:N}",
            $"tenant-{tenantId:N}",
            "America/Toronto",
            CreatedAtUtc));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task PersistCustomer(Guid tenantId, string phoneE164)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Customers.Add(new Customer(
            Guid.CreateVersion7(),
            tenantId,
            phoneE164,
            CreatedAtUtc));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> ReadOnlyVisibleCustomerTenant(Guid tenantId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Customer customer = await dbContext.Customers.SingleAsync(
            TestContext.Current.CancellationToken);
        return customer.TenantId;
    }

    private sealed class TenantClaimScope : IDisposable
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly HttpContext? _previousContext;

        public TenantClaimScope(IServiceProvider services, Guid tenantId)
        {
            _accessor = services.GetRequiredService<IHttpContextAccessor>();
            _previousContext = _accessor.HttpContext;
            ClaimsIdentity identity = new(
                [new Claim(TenantClaimTypes.TenantId, tenantId.ToString())],
                "IntegrationTest");
            _accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            };
        }

        public void Dispose()
        {
            _accessor.HttpContext = _previousContext;
        }
    }
}
