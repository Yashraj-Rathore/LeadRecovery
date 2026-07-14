using LeadRecovery.Application.Customers;
using LeadRecovery.Application.Leads;
using LeadRecovery.Application.PhoneNumbers;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.Infrastructure.Persistence.Automations;
using LeadRecovery.Infrastructure.Persistence.Repositories;
using LeadRecovery.Infrastructure.PhoneNumbers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LeadRecovery.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string databaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseConnectionString);

        services.AddDbContext<LeadRecoveryDbContext>(options =>
            options.UseNpgsql(
                databaseConnectionString,
                npgsqlOptions => npgsqlOptions.SetPostgresVersion(18, 0)));
        services.AddSingleton<IPhoneNumberNormalizer, LibPhoneNumberNormalizer>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<CreateCustomerUseCase>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ILeadAutomationCancellation, ScheduledActionLeadAutomationCancellation>();
        services.AddScoped<BookLeadUseCase>();

        return services;
    }
}
