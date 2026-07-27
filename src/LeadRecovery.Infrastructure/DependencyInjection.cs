using LeadRecovery.Application.Analysis;
using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Customers;
using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Leads;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.PhoneNumbers;
using LeadRecovery.Infrastructure.Analysis;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Integrations.Twilio;
using LeadRecovery.Infrastructure.Messaging;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.Infrastructure.Persistence.Analysis;
using LeadRecovery.Infrastructure.Persistence.Automations;
using LeadRecovery.Infrastructure.Persistence.Integrations;
using LeadRecovery.Infrastructure.Persistence.Messaging;
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
        string databaseConnectionString,
        LeadAnalysisWorkflowOptions? analysisOptions = null)
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
        services.AddScoped<ICallStatusPersistence, CallStatusPersistence>();
        services.AddSingleton<ICallStatusMetrics, CallStatusMetrics>();
        services.AddScoped<ProcessCallStatusWebhookUseCase>();
        services.AddScoped<ISmsWorkflowPersistence, SmsWorkflowPersistence>();
        services.AddScoped<IManualSmsWorkflowPersistence, ManualSmsWorkflowPersistence>();
        services.AddScoped<IWorkflowSmsPersistence, WorkflowSmsPersistence>();
        services.AddSingleton(
            analysisOptions ?? new LeadAnalysisWorkflowOptions(enabled: false));
        services.AddSingleton<ILeadAnalysisInputHasher, LeadAnalysisInputHasher>();
        services.AddScoped<ILeadAnalysisWorkflowPersistence, LeadAnalysisWorkflowPersistence>();
        services.TryAddSingleton<ILeadAnalysisService, UnavailableLeadAnalysisService>();
        services.TryAddSingleton<ISmsSender, FakeSmsSender>();
        services.AddSingleton<ISmsMetrics, SmsMetrics>();
        services.AddScoped<SendScheduledRecoverySmsUseCase>();
        services.AddScoped<SendScheduledManualSmsUseCase>();
        services.AddScoped<SendScheduledWorkflowSmsUseCase>();
        services.AddScoped<ProcessInboundSmsUseCase>();
        services.AddScoped<ProcessDeliveryStatusUseCase>();
        services.AddScoped<ExecuteScheduledLeadAnalysisUseCase>();
        services.AddSingleton<IBusinessHoursScheduler, BusinessHoursScheduler>();
        services.AddSingleton<IQualificationEvaluator, QualificationEvaluator>();
        services.AddScoped<CreateCustomerUseCase>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ILeadAutomationCancellation, ScheduledActionLeadAutomationCancellation>();
        services.AddScoped<IWorkflowActionScheduler, WorkflowActionScheduler>();
        services.AddScoped<BookLeadUseCase>();
        services.AddScoped<ILeadInboxQuery, LeadInboxQuery>();
        services.AddScoped<ILeadDashboardStore, LeadDashboardStore>();
        services.AddScoped<ListLeadsUseCase>();
        services.AddScoped<GetLeadUseCase>();
        services.AddScoped<LeadDashboardUseCase>();

        return services;
    }

    public static IServiceCollection AddSmsProvider(
        this IServiceCollection services,
        SmsProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.RemoveAll<ISmsSender>();
        services.AddSingleton(options);
        if (options.UseTwilio)
        {
            services.AddSingleton<ISmsSender, TwilioSmsSender>();
        }
        else
        {
            services.AddSingleton<ISmsSender, FakeSmsSender>();
        }

        return services;
    }

    public static IServiceCollection AddOpenAiLeadAnalysis(
        this IServiceCollection services,
        OpenAiLeadAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton<ILeadAnalysisResultValidator, LeadAnalysisResultValidator>();
        services.RemoveAll<ILeadAnalysisService>();
        services.AddSingleton(options);
        services.AddHttpClient<ILeadAnalysisService, OpenAiLeadAnalysisService>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        return services;
    }

    public static IServiceCollection AddTwilioCallIngestion(
        this IServiceCollection services,
        string? authToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ITwilioRequestValidator>(
            new TwilioRequestValidatorAdapter(authToken));
        return services;
    }
}
