using LeadRecovery.Application.PhoneNumbers;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Customers;

namespace LeadRecovery.Application.Customers;

public sealed class CreateCustomerUseCase
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ITenantContext _tenantContext;

    public CreateCustomerUseCase(
        ITenantContext tenantContext,
        IPhoneNumberNormalizer phoneNumberNormalizer,
        ICustomerRepository customerRepository)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(phoneNumberNormalizer);
        ArgumentNullException.ThrowIfNull(customerRepository);

        _tenantContext = tenantContext;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _customerRepository = customerRepository;
    }

    public async Task<CreateCustomerResult> ExecuteAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PhoneNumberNormalizationResult normalization = _phoneNumberNormalizer.Normalize(
            request.PhoneNumber,
            request.DefaultRegion);
        if (!normalization.IsSuccess)
        {
            return CreateCustomerResult.InvalidPhone(
                normalization.Failure ?? PhoneNumberNormalizationFailure.ParseFailed);
        }

        string phoneE164 = normalization.PhoneE164!;
        Customer? existingCustomer = await _customerRepository.FindByPhoneAsync(
            phoneE164,
            cancellationToken);
        if (existingCustomer is not null)
        {
            return CreateCustomerResult.Existing(existingCustomer);
        }

        Customer customer = new(
            Guid.CreateVersion7(),
            _tenantContext.TenantId,
            phoneE164,
            request.CreatedAtUtc,
            request.Name,
            request.Email,
            request.City,
            request.PostalCode,
            request.SmsConsentBasis);

        await _customerRepository.AddAsync(customer, cancellationToken);
        return CreateCustomerResult.New(customer);
    }
}
