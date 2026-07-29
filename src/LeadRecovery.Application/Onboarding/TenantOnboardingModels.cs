using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Identity;

namespace LeadRecovery.Application.Onboarding;

public sealed record TenantOnboardingPlan(
    int SchemaVersion,
    TenantOnboardingBusiness Business,
    TenantOnboardingPhone Phone,
    TenantOnboardingWorkflow Workflow,
    IReadOnlyList<TenantOnboardingTemplate> Templates,
    IReadOnlyList<TenantOnboardingUser> Users,
    bool EnableAutomation = false);

public sealed record TenantOnboardingBusiness(
    string Name,
    string Slug,
    string TimezoneId);

public sealed record TenantOnboardingPhone(
    string Provider,
    string PhoneNumberE164,
    string ProviderNumberSid,
    IReadOnlyList<string> RecoverableCallStatuses,
    int InitialDelaySeconds,
    int RecoveryCooldownSeconds,
    bool InboundSmsEnabled = true,
    bool MissedCallRecoveryEnabled = true);

public sealed record TenantOnboardingWorkflow(
    string Name,
    string BookingUrl,
    IReadOnlyList<TenantOnboardingQuestion> QualificationQuestions,
    IReadOnlyList<TenantOnboardingBusinessHours> BusinessHours,
    IReadOnlyList<TenantOnboardingFollowUp> FollowUps,
    bool UrgentHumanReviewAfterHours = true);

public sealed record TenantOnboardingQuestion(
    string Key,
    string Prompt,
    string AnswerKind,
    IReadOnlyList<string> AllowedValues);

public sealed record TenantOnboardingBusinessHours(
    string Day,
    string OpensAt,
    string ClosesAt);

public sealed record TenantOnboardingFollowUp(
    int Sequence,
    int DelayMinutes,
    string TemplatePurpose);

public sealed record TenantOnboardingTemplate(
    string Name,
    string Purpose,
    string Body);

public sealed record TenantOnboardingUser(
    string Email,
    string DisplayName,
    string Role,
    string PasswordEnvironmentVariable);

public sealed record TenantOnboardingValidationError(
    string Field,
    string Message);

public sealed record ValidatedTenantOnboardingUser(
    string Email,
    string DisplayName,
    TenantRole Role,
    string PasswordEnvironmentVariable);

public sealed record ValidatedTenantOnboardingPlan(
    TenantOnboardingBusiness Business,
    TenantOnboardingPhone Phone,
    TenantOnboardingWorkflow Workflow,
    IReadOnlyList<QualificationQuestionPolicy> QualificationQuestions,
    BusinessHoursPolicy BusinessHours,
    IReadOnlyList<FollowUpStepPolicy> FollowUps,
    IReadOnlyList<TenantOnboardingTemplate> Templates,
    IReadOnlyList<ValidatedTenantOnboardingUser> Users,
    bool EnableAutomation);

public sealed record TenantOnboardingValidationResult(
    ValidatedTenantOnboardingPlan? Plan,
    IReadOnlyList<TenantOnboardingValidationError> Errors)
{
    public bool IsValid => Plan is not null && Errors.Count == 0;
}

public enum TenantOnboardingStatus
{
    Activated,
    ValidationFailed,
    Conflict,
}

public sealed record TenantOnboardingResult(
    TenantOnboardingStatus Status,
    Guid? TenantId,
    IReadOnlyList<TenantOnboardingValidationError> Errors)
{
    public static TenantOnboardingResult Activated(Guid tenantId) =>
        new(TenantOnboardingStatus.Activated, tenantId, []);

    public static TenantOnboardingResult ValidationFailed(
        IReadOnlyList<TenantOnboardingValidationError> errors) =>
        new(TenantOnboardingStatus.ValidationFailed, null, errors);

    public static TenantOnboardingResult Conflict(string field, string message) =>
        new(
            TenantOnboardingStatus.Conflict,
            null,
            [new TenantOnboardingValidationError(field, message)]);
}

public interface ITenantOnboardingStore
{
    Task<TenantOnboardingResult> ProvisionAsync(
        ValidatedTenantOnboardingPlan plan,
        IReadOnlyDictionary<string, string> userPasswords,
        CancellationToken cancellationToken);
}

public interface IOnboardingSecretSource
{
    string? GetSecret(string name);
}
