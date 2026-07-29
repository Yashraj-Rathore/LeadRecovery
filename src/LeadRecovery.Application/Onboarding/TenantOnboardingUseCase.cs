using System.Globalization;
using System.Net.Mail;

using LeadRecovery.Application.Messaging;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Tenancy;

namespace LeadRecovery.Application.Onboarding;

public sealed class TenantOnboardingUseCase(
    ITenantOnboardingStore store,
    IOnboardingSecretSource secretSource)
{
    private const int SupportedSchemaVersion = 1;

    public static TenantOnboardingValidationResult Validate(TenantOnboardingPlan? plan)
    {
        List<TenantOnboardingValidationError> errors = [];
        if (plan is null)
        {
            errors.Add(new("plan", "The onboarding plan is required."));
            return new(null, errors);
        }

        if (plan.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add(new(
                "schemaVersion",
                $"Only onboarding schema version {SupportedSchemaVersion} is supported."));
        }

        ValidateBusiness(plan.Business, errors);
        ValidatePhone(plan.Phone, errors);
        QualificationQuestionPolicy[] questions =
            ValidateQuestions(plan.Workflow?.QualificationQuestions, errors);
        BusinessHoursPolicy? businessHours =
            ValidateBusinessHours(plan.Workflow, errors);
        FollowUpStepPolicy[] followUps =
            ValidateFollowUps(plan.Workflow?.FollowUps, errors);
        ValidateWorkflow(plan.Workflow, questions, businessHours, followUps, errors);
        TenantOnboardingTemplate[] templates =
            ValidateTemplates(plan.Templates, followUps, errors);
        ValidatedTenantOnboardingUser[] users = ValidateUsers(plan.Users, errors);

        if (errors.Count != 0 ||
            plan.Business is null ||
            plan.Phone is null ||
            plan.Workflow is null ||
            businessHours is null)
        {
            return new(null, errors);
        }

        return new(
            new ValidatedTenantOnboardingPlan(
                plan.Business,
                plan.Phone,
                plan.Workflow,
                questions,
                businessHours,
                followUps,
                templates,
                users,
                plan.EnableAutomation),
            []);
    }

    public async Task<TenantOnboardingResult> ExecuteAsync(
        TenantOnboardingPlan? plan,
        CancellationToken cancellationToken)
    {
        TenantOnboardingValidationResult validation = Validate(plan);
        if (!validation.IsValid)
        {
            return TenantOnboardingResult.ValidationFailed(validation.Errors);
        }

        ValidatedTenantOnboardingPlan validatedPlan = validation.Plan!;
        Dictionary<string, string> passwords = new(StringComparer.OrdinalIgnoreCase);
        List<TenantOnboardingValidationError> errors = [];
        foreach (ValidatedTenantOnboardingUser user in validatedPlan.Users)
        {
            string? password = secretSource.GetSecret(user.PasswordEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(password))
            {
                errors.Add(new(
                    $"users[{user.Email}].passwordEnvironmentVariable",
                    $"Environment variable '{user.PasswordEnvironmentVariable}' is required."));
                continue;
            }

            passwords[user.Email] = password;
        }

        if (errors.Count != 0)
        {
            return TenantOnboardingResult.ValidationFailed(errors);
        }

        return await store.ProvisionAsync(validatedPlan, passwords, cancellationToken);
    }

    private static void ValidateBusiness(
        TenantOnboardingBusiness? business,
        List<TenantOnboardingValidationError> errors)
    {
        if (business is null)
        {
            errors.Add(new("business", "Business configuration is required."));
            return;
        }

        try
        {
            _ = new Tenant(
                Guid.CreateVersion7(),
                business.Name,
                business.Slug,
                business.TimezoneId,
                DateTimeOffset.UnixEpoch);
        }
        catch (ArgumentException exception)
        {
            errors.Add(new($"business.{exception.ParamName ?? "profile"}", exception.Message));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(business.TimezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            errors.Add(new("business.timezoneId", "The timezone ID is not installed."));
        }
        catch (InvalidTimeZoneException)
        {
            errors.Add(new("business.timezoneId", "The timezone definition is invalid."));
        }
    }

    private static void ValidatePhone(
        TenantOnboardingPhone? phone,
        List<TenantOnboardingValidationError> errors)
    {
        if (phone is null)
        {
            errors.Add(new("phone", "Phone configuration is required."));
            return;
        }

        try
        {
            _ = new TenantPhoneNumber(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                phone.Provider,
                phone.PhoneNumberE164,
                phone.ProviderNumberSid,
                phone.RecoverableCallStatuses ?? [],
                phone.InitialDelaySeconds,
                phone.RecoveryCooldownSeconds,
                phone.InboundSmsEnabled,
                phone.MissedCallRecoveryEnabled,
                isPrimary: true);
        }
        catch (ArgumentException exception)
        {
            errors.Add(new($"phone.{exception.ParamName ?? "configuration"}", exception.Message));
        }
    }

    private static QualificationQuestionPolicy[] ValidateQuestions(
        IReadOnlyList<TenantOnboardingQuestion>? source,
        List<TenantOnboardingValidationError> errors)
    {
        if (source is null)
        {
            errors.Add(new("workflow.qualificationQuestions", "Qualification questions are required."));
            return [];
        }

        List<QualificationQuestionPolicy> questions = [];
        for (int index = 0; index < source.Count; index++)
        {
            TenantOnboardingQuestion? question = source[index];
            if (question is null ||
                !Enum.TryParse(
                    question.AnswerKind,
                    ignoreCase: true,
                    out QualificationAnswerKind answerKind) ||
                !Enum.IsDefined(answerKind))
            {
                errors.Add(new(
                    $"workflow.qualificationQuestions[{index}].answerKind",
                    "Answer kind must be RequiredText or Choice."));
                continue;
            }

            try
            {
                questions.Add(new(
                    question.Key,
                    question.Prompt,
                    answerKind,
                    question.AllowedValues?.ToArray() ?? []));
            }
            catch (ArgumentException exception)
            {
                errors.Add(new(
                    $"workflow.qualificationQuestions[{index}].{exception.ParamName ?? "configuration"}",
                    exception.Message));
            }
        }

        return questions.ToArray();
    }

    private static BusinessHoursPolicy? ValidateBusinessHours(
        TenantOnboardingWorkflow? workflow,
        List<TenantOnboardingValidationError> errors)
    {
        if (workflow?.BusinessHours is null)
        {
            errors.Add(new("workflow.businessHours", "Business hours are required."));
            return null;
        }

        List<BusinessDayHours> windows = [];
        for (int index = 0; index < workflow.BusinessHours.Count; index++)
        {
            TenantOnboardingBusinessHours? source = workflow.BusinessHours[index];
            if (source is null ||
                !Enum.TryParse(source.Day, ignoreCase: true, out DayOfWeek day) ||
                !Enum.IsDefined(day))
            {
                errors.Add(new(
                    $"workflow.businessHours[{index}].day",
                    "The business-hours day is invalid."));
                continue;
            }

            if (!TimeOnly.TryParseExact(
                    source.OpensAt,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out TimeOnly opensAt) ||
                !TimeOnly.TryParseExact(
                    source.ClosesAt,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out TimeOnly closesAt))
            {
                errors.Add(new(
                    $"workflow.businessHours[{index}]",
                    "Opening and closing times must use 24-hour HH:mm format."));
                continue;
            }

            try
            {
                windows.Add(new(day, opensAt, closesAt));
            }
            catch (ArgumentException exception)
            {
                errors.Add(new(
                    $"workflow.businessHours[{index}].{exception.ParamName ?? "configuration"}",
                    exception.Message));
            }
        }

        try
        {
            return new BusinessHoursPolicy(
                windows.ToArray(),
                workflow.UrgentHumanReviewAfterHours);
        }
        catch (ArgumentException exception)
        {
            errors.Add(new(
                $"workflow.businessHours.{exception.ParamName ?? "configuration"}",
                exception.Message));
            return null;
        }
    }

    private static FollowUpStepPolicy[] ValidateFollowUps(
        IReadOnlyList<TenantOnboardingFollowUp>? source,
        List<TenantOnboardingValidationError> errors)
    {
        if (source is null)
        {
            errors.Add(new("workflow.followUps", "Follow-up configuration is required."));
            return [];
        }

        List<FollowUpStepPolicy> followUps = [];
        for (int index = 0; index < source.Count; index++)
        {
            TenantOnboardingFollowUp? item = source[index];
            if (item is null)
            {
                errors.Add(new($"workflow.followUps[{index}]", "The follow-up entry is required."));
                continue;
            }

            try
            {
                followUps.Add(new(
                    item.Sequence,
                    item.DelayMinutes,
                    item.TemplatePurpose));
            }
            catch (ArgumentException exception)
            {
                errors.Add(new(
                    $"workflow.followUps[{index}].{exception.ParamName ?? "configuration"}",
                    exception.Message));
            }
        }

        return followUps.ToArray();
    }

    private static void ValidateWorkflow(
        TenantOnboardingWorkflow? workflow,
        IReadOnlyList<QualificationQuestionPolicy> questions,
        BusinessHoursPolicy? businessHours,
        IReadOnlyList<FollowUpStepPolicy> followUps,
        List<TenantOnboardingValidationError> errors)
    {
        if (workflow is null)
        {
            errors.Add(new("workflow", "Workflow configuration is required."));
            return;
        }

        if (businessHours is null)
        {
            return;
        }

        try
        {
            _ = new WorkflowDefinition(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                workflow.Name,
                1,
                workflow.BookingUrl,
                questions.ToArray(),
                businessHours,
                followUps.ToArray(),
                DateTimeOffset.UnixEpoch);
        }
        catch (ArgumentException exception)
        {
            errors.Add(new($"workflow.{exception.ParamName ?? "configuration"}", exception.Message));
        }
    }

    private static TenantOnboardingTemplate[] ValidateTemplates(
        IReadOnlyList<TenantOnboardingTemplate>? source,
        IReadOnlyList<FollowUpStepPolicy> followUps,
        List<TenantOnboardingValidationError> errors)
    {
        if (source is null || source.Count == 0)
        {
            errors.Add(new("templates", "Approved message templates are required."));
            return [];
        }

        TenantOnboardingTemplate[] templates = source
            .Where(template => template is not null)
            .ToArray();
        string[] duplicatePurposes = templates
            .GroupBy(template => template.Purpose, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicatePurposes.Length != 0)
        {
            errors.Add(new(
                "templates",
                "Template purposes must be unique: " + string.Join(", ", duplicatePurposes)));
        }

        HashSet<string> purposes = templates
            .Select(template => template.Purpose)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] requiredPurposes =
        [
            SmsTemplatePurposes.InitialMissedCallRecovery,
            SmsTemplatePurposes.BookingLink,
            .. followUps.Select(step => step.TemplatePurpose),
        ];
        string[] missing = requiredPurposes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(purpose => !purposes.Contains(purpose))
            .ToArray();
        if (missing.Length != 0)
        {
            errors.Add(new(
                "templates",
                "Active templates are required for: " + string.Join(", ", missing)));
        }

        foreach ((TenantOnboardingTemplate template, int index) in templates.Select((item, index) => (item, index)))
        {
            try
            {
                _ = new MessageTemplate(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    template.Name,
                    template.Purpose,
                    template.Body,
                    1,
                    Guid.CreateVersion7(),
                    DateTimeOffset.UnixEpoch);
            }
            catch (ArgumentException exception)
            {
                errors.Add(new(
                    $"templates[{index}].{exception.ParamName ?? "configuration"}",
                    exception.Message));
            }
        }

        return templates;
    }

    private static ValidatedTenantOnboardingUser[] ValidateUsers(
        IReadOnlyList<TenantOnboardingUser>? source,
        List<TenantOnboardingValidationError> errors)
    {
        if (source is null || source.Count == 0)
        {
            errors.Add(new("users", "At least one user is required."));
            return [];
        }

        List<ValidatedTenantOnboardingUser> users = [];
        HashSet<string> emails = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < source.Count; index++)
        {
            TenantOnboardingUser? user = source[index];
            if (user is null)
            {
                errors.Add(new($"users[{index}]", "The user entry is required."));
                continue;
            }

            string email = user.Email?.Trim() ?? string.Empty;
            try
            {
                MailAddress address = new(email);
                if (!address.Address.Equals(email, StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException();
                }
            }
            catch (FormatException)
            {
                errors.Add(new($"users[{index}].email", "The email address is invalid."));
            }

            if (!emails.Add(email))
            {
                errors.Add(new($"users[{index}].email", "User emails must be unique."));
            }

            if (!Enum.TryParse(user.Role, ignoreCase: true, out TenantRole role) ||
                !Enum.IsDefined(role))
            {
                errors.Add(new(
                    $"users[{index}].role",
                    "Role must be Owner, Manager, Staff, or ReadOnly."));
                continue;
            }

            if (!IsEnvironmentVariableName(user.PasswordEnvironmentVariable))
            {
                errors.Add(new(
                    $"users[{index}].passwordEnvironmentVariable",
                    "Password environment variable names may contain only A-Z, 0-9, and underscore."));
            }

            if (string.IsNullOrWhiteSpace(user.DisplayName))
            {
                errors.Add(new($"users[{index}].displayName", "Display name is required."));
            }

            users.Add(new(
                email,
                user.DisplayName,
                role,
                user.PasswordEnvironmentVariable));
        }

        if (users.Count(user => user.Role == TenantRole.Owner) != 1)
        {
            errors.Add(new("users", "Exactly one initial Owner is required."));
        }

        return users.ToArray();
    }

    private static bool IsEnvironmentVariableName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (character is not (>= 'A' and <= 'Z') &&
                character is not (>= '0' and <= '9') &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
