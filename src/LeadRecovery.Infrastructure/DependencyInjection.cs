using LeadRecovery.Application.Customers;
using LeadRecovery.Application.Leads;
using LeadRecovery.Application.PhoneNumbers;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.Infrastructure.Persistence.Automations;
using LeadRecovery.Infrastructure.Persistence.Queries;
using LeadRecovery.Infrastructure.Persistence.Repositories;
using LeadRecovery.Infrastructure.PhoneNumbers;

using Microsoft.AspNetCore.Identity;
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
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<LeadRecoveryDbContext>();
        services.AddSingleton<IPhoneNumberNormalizer, LibPhoneNumberNormalizer>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<CreateCustomerUseCase>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ILeadAutomationCancellation, ScheduledActionLeadAutomationCancellation>();
        services.AddScoped<BookLeadUseCase>();
        services.AddScoped<ILeadInboxQuery, LeadInboxQuery>();
        services.AddScoped<ListLeadsUseCase>();
        services.AddScoped<GetLeadUseCase>();

        return services;
    }
}
