using LeadRecovery.Application.Customers;
using LeadRecovery.Domain.Customers;

using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Infrastructure.Persistence.Repositories;

internal sealed class CustomerRepository(LeadRecoveryDbContext dbContext)
    : ICustomerRepository
{
    public Task<Customer?> FindByPhoneAsync(
        string phoneE164,
        CancellationToken cancellationToken) =>
        dbContext.Customers.SingleOrDefaultAsync(
            customer => customer.PhoneE164 == phoneE164,
            cancellationToken);

    public async Task AddAsync(
        Customer customer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
