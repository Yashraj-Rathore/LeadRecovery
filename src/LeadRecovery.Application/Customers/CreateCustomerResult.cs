using LeadRecovery.Application.PhoneNumbers;
using LeadRecovery.Domain.Customers;

namespace LeadRecovery.Application.Customers;

public sealed class CreateCustomerResult
{
    private CreateCustomerResult(
        Customer? customer,
        bool created,
        PhoneNumberNormalizationFailure? phoneFailure)
    {
        Customer = customer;
        Created = created;
        PhoneFailure = phoneFailure;
    }

    public bool IsSuccess => Customer is not null;

    public Customer? Customer { get; }

    public bool Created { get; }

    public PhoneNumberNormalizationFailure? PhoneFailure { get; }

    public static CreateCustomerResult New(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return new CreateCustomerResult(customer, true, null);
    }

    public static CreateCustomerResult Existing(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return new CreateCustomerResult(customer, false, null);
    }

    public static CreateCustomerResult InvalidPhone(
        PhoneNumberNormalizationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new CreateCustomerResult(null, false, failure);
    }
}
