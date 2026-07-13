using LeadRecovery.Domain.Customers;

namespace LeadRecovery.Application.Customers;

public interface ICustomerRepository
{
    Task<Customer?> FindByPhoneAsync(
        string phoneE164,
        CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);
}
